using System;
using System.IO;
using System.Security.Cryptography;
using InstaloaderSharp.Util;

namespace InstaloaderSharp.Compression
{
    /// <summary>Integrity check type of an .xz stream.</summary>
    public enum XzCheck
    {
        None = 0,
        Crc32 = 1,
        Crc64 = 4,
        Sha256 = 10
    }

    /// <summary>
    /// Reading and writing of .xz files (and reading of legacy .lzma files), compatible with Python's lzma module.
    /// </summary>
    public static class Xz
    {
        private static readonly byte[] HeaderMagic = { 0xFD, (byte)'7', (byte)'z', (byte)'X', (byte)'Z', 0x00 };
        private static readonly byte[] FooterMagic = { (byte)'Y', (byte)'Z' };

        #region decompression

        /// <summary>Decompresses .xz data (possibly multiple concatenated streams) or legacy .lzma data.</summary>
        public static byte[] Decompress(byte[] data)
        {
            if (data.Length >= 6 && StartsWith(data, 0, HeaderMagic))
                return DecompressXz(data);
            if (data.Length >= 13 && data[0] < 9 * 5 * 5)
                return DecompressLzmaAlone(data);
            throw new LzmaException("Input format not supported by decoder");
        }

        public static byte[] DecompressFile(string path)
        {
            return Decompress(File.ReadAllBytes(path));
        }

        private static bool StartsWith(byte[] data, int offset, byte[] magic)
        {
            if (offset + magic.Length > data.Length)
                return false;
            for (int i = 0; i < magic.Length; i++)
                if (data[offset + i] != magic[i])
                    return false;
            return true;
        }

        private static ulong ReadVarint(byte[] data, ref int pos, int limit)
        {
            ulong result = 0;
            for (int i = 0; i < 9; i++)
            {
                if (pos >= limit)
                    throw new EndOfStreamException("Compressed file ended before the end-of-stream marker was reached");
                byte b = data[pos++];
                result |= (ulong)(b & 0x7F) << (7 * i);
                if ((b & 0x80) == 0)
                {
                    if (b == 0 && i != 0)
                        throw new LzmaException("Corrupt input data");
                    return result;
                }
            }
            throw new LzmaException("Corrupt input data");
        }

        private static int CheckSize(int checkType)
        {
            if (checkType == 0)
                return 0;
            return 4 << ((checkType - 1) / 3);
        }

        private static byte[] DecompressXz(byte[] data)
        {
            var output = new ByteBuffer(data.Length * 4);
            int pos = 0;
            bool firstStream = true;
            while (pos < data.Length)
            {
                if (!firstStream)
                {
                    // Stream padding (multiple of four zero bytes) between concatenated streams
                    int padStart = pos;
                    while (pos < data.Length && data[pos] == 0)
                        pos++;
                    if ((pos - padStart) % 4 != 0)
                        throw new LzmaException("Corrupt input data");
                    if (pos >= data.Length)
                        break;
                }
                firstStream = false;
                pos = DecompressStream(data, pos, output);
            }
            return output.ToArray();
        }

        private static void Need(byte[] data, int pos, int count)
        {
            if (pos + count > data.Length)
                throw new EndOfStreamException("Compressed file ended before the end-of-stream marker was reached");
        }

        private static int DecompressStream(byte[] data, int pos, ByteBuffer output)
        {
            Need(data, pos, 12);
            if (!StartsWith(data, pos, HeaderMagic))
                throw new LzmaException("Input format not supported by decoder");
            if (data[pos + 6] != 0 || (data[pos + 7] & 0xF0) != 0)
                throw new LzmaException("Invalid or unsupported options");
            if (Crc32.Compute(data, pos + 6, 2) != BitConverter.ToUInt32(data, pos + 8))
                throw new LzmaException("Corrupt input data");
            int checkType = data[pos + 7] & 0x0F;
            int checkSize = CheckSize(checkType);
            pos += 12;

            while (true)
            {
                Need(data, pos, 1);
                if (data[pos] == 0)
                    break; // Index indicator
                int blockStart = pos;
                int headerSize = (data[pos] + 1) * 4;
                Need(data, pos, headerSize);
                if (Crc32.Compute(data, pos, headerSize - 4) != BitConverter.ToUInt32(data, pos + headerSize - 4))
                    throw new LzmaException("Corrupt input data");
                int hp = pos + 2;
                int hEnd = pos + headerSize - 4;
                byte flags = data[pos + 1];
                if ((flags & 0x3C) != 0)
                    throw new LzmaException("Invalid or unsupported options");
                int numFilters = (flags & 0x03) + 1;
                if ((flags & 0x40) != 0)
                    ReadVarint(data, ref hp, hEnd);
                if ((flags & 0x80) != 0)
                    ReadVarint(data, ref hp, hEnd);
                byte lzma2Props = 0;
                for (int f = 0; f < numFilters; f++)
                {
                    ulong filterId = ReadVarint(data, ref hp, hEnd);
                    ulong propsSize = ReadVarint(data, ref hp, hEnd);
                    if (hp + (long)propsSize > hEnd)
                        throw new LzmaException("Corrupt input data");
                    if (f != numFilters - 1 || filterId != 0x21 || propsSize != 1)
                        throw new LzmaException("Invalid or unsupported options");
                    lzma2Props = data[hp];
                    hp += (int)propsSize;
                }
                if (lzma2Props > 40)
                    throw new LzmaException("Invalid or unsupported options");
                pos += headerSize;
                int blockOutputStart = output.Length;
                pos = DecodeLzma2(data, pos, output);
                // Block padding
                while ((pos - blockStart) % 4 != 0)
                {
                    Need(data, pos, 1);
                    if (data[pos++] != 0)
                        throw new LzmaException("Corrupt input data");
                }
                Need(data, pos, checkSize);
                VerifyCheck(checkType, output, blockOutputStart, data, pos);
                pos += checkSize;
            }

            // Index
            int indexStart = pos;
            pos++;
            ulong records = ReadVarint(data, ref pos, data.Length);
            for (ulong i = 0; i < records; i++)
            {
                ReadVarint(data, ref pos, data.Length);
                ReadVarint(data, ref pos, data.Length);
            }
            while ((pos - indexStart) % 4 != 0)
            {
                Need(data, pos, 1);
                pos++;
            }
            Need(data, pos, 4);
            if (Crc32.Compute(data, indexStart, pos - indexStart) != BitConverter.ToUInt32(data, pos))
                throw new LzmaException("Corrupt input data");
            pos += 4;

            // Footer
            Need(data, pos, 12);
            if (!StartsWith(data, pos + 10, FooterMagic))
                throw new LzmaException("Corrupt input data");
            pos += 12;
            return pos;
        }

        private static void VerifyCheck(int checkType, ByteBuffer output, int start, byte[] data, int pos)
        {
            if (checkType != 1 && checkType != 4 && checkType != 10)
                return;
            byte[] block = new byte[output.Length - start];
            for (int i = 0; i < block.Length; i++)
                block[i] = output[start + i];
            bool ok;
            if (checkType == 1)
            {
                ok = Crc32.Compute(block) == BitConverter.ToUInt32(data, pos);
            }
            else if (checkType == 4)
            {
                ok = Crc64.Update(0, block, 0, block.Length) == BitConverter.ToUInt64(data, pos);
            }
            else
            {
                using (var sha = SHA256.Create())
                {
                    byte[] hash = sha.ComputeHash(block);
                    ok = true;
                    for (int i = 0; i < 32; i++)
                        if (hash[i] != data[pos + i])
                            ok = false;
                }
            }
            if (!ok)
                throw new LzmaException("Corrupt input data");
        }

        private static int DecodeLzma2(byte[] data, int pos, ByteBuffer output)
        {
            var decoder = new LzmaDecoder();
            var rc = new RangeDecoder();
            bool needDictReset = true;
            bool needProps = true;
            while (true)
            {
                Need(data, pos, 1);
                int control = data[pos++];
                if (control == 0x00)
                    return pos;
                if (control == 0x01 || control == 0x02)
                {
                    if (control == 0x02 && needDictReset)
                        throw new LzmaException("Corrupt input data");
                    if (control == 0x01)
                    {
                        decoder.DictStart = output.Length;
                        needDictReset = false;
                    }
                    Need(data, pos, 2);
                    int size = ((data[pos] << 8) | data[pos + 1]) + 1;
                    pos += 2;
                    Need(data, pos, size);
                    output.Add(data, pos, size);
                    pos += size;
                    continue;
                }
                if (control < 0x80)
                    throw new LzmaException("Corrupt input data");
                Need(data, pos, 4);
                int unpackedSize = ((control & 0x1F) << 16) + (data[pos] << 8) + data[pos + 1] + 1;
                int packedSize = (data[pos + 2] << 8) + data[pos + 3] + 1;
                pos += 4;
                int reset = (control >> 5) & 0x03;
                if (reset == 3)
                {
                    decoder.DictStart = output.Length;
                    needDictReset = false;
                }
                else if (needDictReset)
                {
                    throw new LzmaException("Corrupt input data");
                }
                if (reset >= 2)
                {
                    Need(data, pos, 1);
                    byte props = data[pos++];
                    int lc = props % 9;
                    if (lc + (props / 9) % 5 > 4)
                        throw new LzmaException("Invalid or unsupported options");
                    decoder.SetProperties(props);
                    needProps = false;
                }
                else if (needProps)
                {
                    throw new LzmaException("Corrupt input data");
                }
                if (reset >= 1)
                    decoder.ResetState();
                Need(data, pos, packedSize);
                rc.Init(data, pos, packedSize);
                decoder.Decode(rc, output, unpackedSize, false);
                pos += packedSize;
            }
        }

        private static byte[] DecompressLzmaAlone(byte[] data)
        {
            byte props = data[0];
            long unpackSize = BitConverter.ToInt64(data, 5);
            var decoder = new LzmaDecoder();
            decoder.SetProperties(props);
            decoder.ResetState();
            var output = new ByteBuffer(data.Length * 4);
            var rc = new RangeDecoder();
            rc.Init(data, 13, data.Length - 13);
            bool marker = decoder.Decode(rc, output, unpackSize, true);
            if (unpackSize < 0 && !marker)
                throw new EndOfStreamException("Compressed file ended before the end-of-stream marker was reached");
            return output.ToArray();
        }

        #endregion

        #region compression

        private static void WriteVarint(Stream s, ulong value)
        {
            while (value >= 0x80)
            {
                s.WriteByte((byte)(value | 0x80));
                value >>= 7;
            }
            s.WriteByte((byte)value);
        }

        private static void WriteUInt32(Stream s, uint value)
        {
            s.Write(BitConverter.GetBytes(value), 0, 4);
        }

        private static byte DictSizeProperty(int dataLength)
        {
            // Smallest encoded dictionary size that holds the whole input (at least 4 KiB).
            for (int b = 0; b < 40; b++)
            {
                long size = (long)(2 | (b & 1)) << (b / 2 + 11);
                if (size >= dataLength)
                    return (byte)b;
            }
            return 40;
        }

        /// <summary>Compresses data into the .xz format (single block, LZMA2 filter).</summary>
        public static byte[] Compress(byte[] data, XzCheck check = XzCheck.None)
        {
            var ms = new MemoryStream();
            int checkType = (int)check;

            // Stream header
            ms.Write(HeaderMagic, 0, HeaderMagic.Length);
            byte[] streamFlags = { 0x00, (byte)checkType };
            ms.Write(streamFlags, 0, 2);
            WriteUInt32(ms, Crc32.Compute(streamFlags));

            var indexRecords = new MemoryStream();
            ulong recordCount = 0;
            if (data.Length > 0)
            {
                // Block header
                var header = new MemoryStream();
                header.WriteByte(0); // size placeholder
                header.WriteByte(0x00); // flags: one filter, no sizes
                WriteVarint(header, 0x21); // LZMA2
                WriteVarint(header, 1);
                header.WriteByte(DictSizeProperty(data.Length));
                while ((header.Length + 4) % 4 != 0)
                    header.WriteByte(0);
                byte[] headerBytes = header.ToArray();
                headerBytes[0] = (byte)((headerBytes.Length + 4) / 4 - 1);
                ms.Write(headerBytes, 0, headerBytes.Length);
                WriteUInt32(ms, Crc32.Compute(headerBytes));
                long headerSize = headerBytes.Length + 4;

                byte[] compressed = EncodeLzma2(data);
                ms.Write(compressed, 0, compressed.Length);
                long unpadded = headerSize + compressed.Length + CheckSize(checkType);
                for (long padded = headerSize + compressed.Length; padded % 4 != 0; padded++)
                    ms.WriteByte(0);
                byte[] checkBytes = ComputeCheck(check, data);
                ms.Write(checkBytes, 0, checkBytes.Length);

                WriteVarint(indexRecords, (ulong)unpadded);
                WriteVarint(indexRecords, (ulong)data.Length);
                recordCount = 1;
            }

            // Index
            var index = new MemoryStream();
            index.WriteByte(0x00);
            WriteVarint(index, recordCount);
            byte[] records = indexRecords.ToArray();
            index.Write(records, 0, records.Length);
            while (index.Length % 4 != 0)
                index.WriteByte(0);
            byte[] indexBytes = index.ToArray();
            ms.Write(indexBytes, 0, indexBytes.Length);
            WriteUInt32(ms, Crc32.Compute(indexBytes));

            // Stream footer
            var footer = new MemoryStream();
            WriteUInt32(footer, (uint)((indexBytes.Length + 4) / 4 - 1));
            footer.Write(streamFlags, 0, 2);
            byte[] footerBytes = footer.ToArray();
            WriteUInt32(ms, Crc32.Compute(footerBytes));
            ms.Write(footerBytes, 0, footerBytes.Length);
            ms.Write(FooterMagic, 0, 2);
            return ms.ToArray();
        }

        private static byte[] ComputeCheck(XzCheck check, byte[] data)
        {
            switch (check)
            {
                case XzCheck.Crc32:
                    return BitConverter.GetBytes(Crc32.Compute(data));
                case XzCheck.Crc64:
                    return BitConverter.GetBytes(Crc64.Update(0, data, 0, data.Length));
                case XzCheck.Sha256:
                    using (var sha = SHA256.Create())
                        return sha.ComputeHash(data);
                default:
                    return new byte[0];
            }
        }

        private const int ChunkSize = 1 << 16;

        private static byte[] EncodeLzma2(byte[] data)
        {
            var output = new MemoryStream();
            var encoder = new LzmaEncoder(data);
            byte props = (byte)((LzmaEncoder.Pb * 5 + LzmaEncoder.Lp) * 9 + LzmaEncoder.Lc);
            bool dictResetDone = false;
            bool propsSent = false;
            bool needStateReset = true;
            int pos = 0;
            while (pos < data.Length)
            {
                int size = Math.Min(ChunkSize, data.Length - pos);
                if (needStateReset)
                    encoder.ResetState();
                byte[] packed = encoder.EncodeChunk(pos, pos + size);
                if (packed.Length <= ChunkSize && packed.Length < size)
                {
                    int control;
                    if (!dictResetDone)
                        control = 0xE0; // dictionary reset, new properties, state reset
                    else if (!propsSent)
                        control = 0xC0; // new properties, state reset
                    else if (needStateReset)
                        control = 0xA0; // state reset
                    else
                        control = 0x80; // continue
                    int u = size - 1;
                    int p = packed.Length - 1;
                    output.WriteByte((byte)(control | (u >> 16)));
                    output.WriteByte((byte)(u >> 8));
                    output.WriteByte((byte)u);
                    output.WriteByte((byte)(p >> 8));
                    output.WriteByte((byte)p);
                    if (control >= 0xC0)
                        output.WriteByte(props);
                    output.Write(packed, 0, packed.Length);
                    dictResetDone = true;
                    propsSent = true;
                    needStateReset = false;
                }
                else
                {
                    // Store uncompressed. The encoder state advanced without the decoder seeing it, so the next
                    // LZMA chunk has to reset the state.
                    output.WriteByte((byte)(dictResetDone ? 0x02 : 0x01));
                    output.WriteByte((byte)((size - 1) >> 8));
                    output.WriteByte((byte)(size - 1));
                    output.Write(data, pos, size);
                    dictResetDone = true;
                    needStateReset = true;
                }
                pos += size;
            }
            output.WriteByte(0x00);
            return output.ToArray();
        }

        #endregion
    }
}
