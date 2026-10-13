using System;

namespace InstaloaderSharp.Util
{
    /// <summary>CRC-32 (IEEE 802.3), as used by zlib and the .xz format.</summary>
    public static class Crc32
    {
        private static readonly uint[] Table = CreateTable();

        private static uint[] CreateTable()
        {
            var table = new uint[256];
            for (uint i = 0; i < 256; i++)
            {
                uint c = i;
                for (int k = 0; k < 8; k++)
                    c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                table[i] = c;
            }
            return table;
        }

        public static uint Update(uint crc, byte[] data, int offset, int count)
        {
            crc = ~crc;
            for (int i = offset; i < offset + count; i++)
                crc = Table[(crc ^ data[i]) & 0xFF] ^ (crc >> 8);
            return ~crc;
        }

        public static uint Compute(byte[] data, int offset, int count)
        {
            return Update(0, data, offset, count);
        }

        public static uint Compute(byte[] data)
        {
            return Update(0, data, 0, data.Length);
        }
    }

    /// <summary>CRC-64 (ECMA-182 polynomial, reflected), as used by the .xz format.</summary>
    public static class Crc64
    {
        private static readonly ulong[] Table = CreateTable();

        private static ulong[] CreateTable()
        {
            var table = new ulong[256];
            for (ulong i = 0; i < 256; i++)
            {
                ulong c = i;
                for (int k = 0; k < 8; k++)
                    c = (c & 1) != 0 ? 0xC96C5795D7870F42UL ^ (c >> 1) : c >> 1;
                table[i] = c;
            }
            return table;
        }

        public static ulong Update(ulong crc, byte[] data, int offset, int count)
        {
            crc = ~crc;
            for (int i = offset; i < offset + count; i++)
                crc = Table[(crc ^ data[i]) & 0xFF] ^ (crc >> 8);
            return ~crc;
        }
    }

    /// <summary>BLAKE2b (RFC 7693), with variable digest size and no key, like Python's hashlib.blake2b.</summary>
    public sealed class Blake2b
    {
        private static readonly ulong[] IV =
        {
            0x6A09E667F3BCC908UL, 0xBB67AE8584CAA73BUL, 0x3C6EF372FE94F82BUL, 0xA54FF53A5F1D36F1UL,
            0x510E527FADE682D1UL, 0x9B05688C2B3E6C1FUL, 0x1F83D9ABFB41BD6BUL, 0x5BE0CD19137E2179UL
        };

        private static readonly byte[][] Sigma =
        {
            new byte[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15 },
            new byte[] { 14, 10, 4, 8, 9, 15, 13, 6, 1, 12, 0, 2, 11, 7, 5, 3 },
            new byte[] { 11, 8, 12, 0, 5, 2, 15, 13, 10, 14, 3, 6, 7, 1, 9, 4 },
            new byte[] { 7, 9, 3, 1, 13, 12, 11, 14, 2, 6, 5, 10, 4, 0, 15, 8 },
            new byte[] { 9, 0, 5, 7, 2, 4, 10, 15, 14, 1, 11, 12, 6, 8, 3, 13 },
            new byte[] { 2, 12, 6, 10, 0, 11, 8, 3, 4, 13, 7, 5, 15, 14, 1, 9 },
            new byte[] { 12, 5, 1, 15, 14, 13, 4, 10, 0, 7, 6, 3, 9, 2, 8, 11 },
            new byte[] { 13, 11, 7, 14, 12, 1, 3, 9, 5, 0, 15, 4, 8, 6, 2, 10 },
            new byte[] { 6, 15, 14, 9, 11, 3, 0, 8, 12, 2, 13, 7, 1, 4, 10, 5 },
            new byte[] { 10, 2, 8, 4, 7, 6, 1, 5, 15, 11, 9, 14, 3, 12, 13, 0 }
        };

        private readonly ulong[] _h = new ulong[8];
        private readonly byte[] _buffer = new byte[128];
        private int _bufferLength;
        private ulong _t0, _t1;
        private readonly int _digestSize;

        public Blake2b(int digestSize = 64)
        {
            if (digestSize < 1 || digestSize > 64)
                throw new ArgumentOutOfRangeException("digestSize");
            _digestSize = digestSize;
            Array.Copy(IV, _h, 8);
            _h[0] ^= 0x01010000UL ^ (ulong)digestSize;
        }

        public void Update(byte[] data)
        {
            Update(data, 0, data.Length);
        }

        public void Update(byte[] data, int offset, int count)
        {
            while (count > 0)
            {
                if (_bufferLength == 128)
                {
                    IncrementCounter(128);
                    Compress(_buffer, false);
                    _bufferLength = 0;
                }
                int n = Math.Min(128 - _bufferLength, count);
                Buffer.BlockCopy(data, offset, _buffer, _bufferLength, n);
                _bufferLength += n;
                offset += n;
                count -= n;
            }
        }

        public byte[] Digest()
        {
            IncrementCounter((ulong)_bufferLength);
            for (int i = _bufferLength; i < 128; i++)
                _buffer[i] = 0;
            Compress(_buffer, true);
            var output = new byte[_digestSize];
            for (int i = 0; i < _digestSize; i++)
                output[i] = (byte)(_h[i / 8] >> (8 * (i % 8)));
            return output;
        }

        private void IncrementCounter(ulong n)
        {
            _t0 += n;
            if (_t0 < n)
                _t1++;
        }

        private static ulong RotR(ulong x, int n)
        {
            return (x >> n) | (x << (64 - n));
        }

        private void Compress(byte[] block, bool last)
        {
            var m = new ulong[16];
            for (int i = 0; i < 16; i++)
                m[i] = BitConverter.ToUInt64(block, i * 8);
            var v = new ulong[16];
            Array.Copy(_h, v, 8);
            Array.Copy(IV, 0, v, 8, 8);
            v[12] ^= _t0;
            v[13] ^= _t1;
            if (last)
                v[14] = ~v[14];
            for (int r = 0; r < 12; r++)
            {
                byte[] s = Sigma[r % 10];
                G(v, 0, 4, 8, 12, m[s[0]], m[s[1]]);
                G(v, 1, 5, 9, 13, m[s[2]], m[s[3]]);
                G(v, 2, 6, 10, 14, m[s[4]], m[s[5]]);
                G(v, 3, 7, 11, 15, m[s[6]], m[s[7]]);
                G(v, 0, 5, 10, 15, m[s[8]], m[s[9]]);
                G(v, 1, 6, 11, 12, m[s[10]], m[s[11]]);
                G(v, 2, 7, 8, 13, m[s[12]], m[s[13]]);
                G(v, 3, 4, 9, 14, m[s[14]], m[s[15]]);
            }
            for (int i = 0; i < 8; i++)
                _h[i] ^= v[i] ^ v[i + 8];
        }

        private static void G(ulong[] v, int a, int b, int c, int d, ulong x, ulong y)
        {
            v[a] = v[a] + v[b] + x;
            v[d] = RotR(v[d] ^ v[a], 32);
            v[c] = v[c] + v[d];
            v[b] = RotR(v[b] ^ v[c], 24);
            v[a] = v[a] + v[b] + y;
            v[d] = RotR(v[d] ^ v[a], 16);
            v[c] = v[c] + v[d];
            v[b] = RotR(v[b] ^ v[c], 63);
        }

        public static byte[] Hash(byte[] data, int digestSize)
        {
            var b = new Blake2b(digestSize);
            b.Update(data);
            return b.Digest();
        }
    }
}
