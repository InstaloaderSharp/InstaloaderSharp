using System;
using System.IO;

namespace InstaloaderSharp.Compression
{
    /// <summary>Error in compressed data (Python's lzma.LZMAError).</summary>
    public class LzmaException : Exception
    {
        public LzmaException(string message) : base(message) { }
    }

    /// <summary>Growable byte buffer that also serves as LZ dictionary.</summary>
    internal sealed class ByteBuffer
    {
        private byte[] _data;
        private int _length;

        public ByteBuffer(int capacity = 4096)
        {
            _data = new byte[Math.Max(16, capacity)];
        }

        public int Length { get { return _length; } }

        public byte this[int index] { get { return _data[index]; } }

        private void Ensure(int extra)
        {
            if (_length + extra <= _data.Length)
                return;
            long newSize = Math.Max((long)_data.Length * 2, (long)_length + extra);
            if (newSize > int.MaxValue)
                throw new LzmaException("Data too large");
            Array.Resize(ref _data, (int)newSize);
        }

        public void Add(byte b)
        {
            Ensure(1);
            _data[_length++] = b;
        }

        public void Add(byte[] src, int offset, int count)
        {
            Ensure(count);
            Buffer.BlockCopy(src, offset, _data, _length, count);
            _length += count;
        }

        public void CopyMatch(int distance, int length)
        {
            Ensure(length);
            int src = _length - distance;
            for (int i = 0; i < length; i++)
                _data[_length++] = _data[src + i];
        }

        public byte[] ToArray()
        {
            var result = new byte[_length];
            Buffer.BlockCopy(_data, 0, result, 0, _length);
            return result;
        }
    }

    internal static class LzmaConstants
    {
        public const int NumStates = 12;
        public const int NumPosBitsMax = 4;
        public const int NumLenToPosStates = 4;
        public const int NumAlignBits = 4;
        public const int StartPosModelIndex = 4;
        public const int EndPosModelIndex = 14;
        public const int NumFullDistances = 1 << (EndPosModelIndex >> 1);
        public const int MatchMinLen = 2;
        public const int MatchMaxLen = 273;
        public const int NumBitModelTotalBits = 11;
        public const int BitModelTotal = 1 << NumBitModelTotalBits;
        public const int NumMoveBits = 5;
        public const uint TopValue = 1u << 24;

        public static void InitProbs(ushort[] probs)
        {
            for (int i = 0; i < probs.Length; i++)
                probs[i] = BitModelTotal / 2;
        }

        public static int UpdateStateLiteral(int state)
        {
            if (state < 4) return 0;
            if (state < 10) return state - 3;
            return state - 6;
        }

        public static int UpdateStateMatch(int state) { return state < 7 ? 7 : 10; }
        public static int UpdateStateRep(int state) { return state < 7 ? 8 : 11; }
        public static int UpdateStateShortRep(int state) { return state < 7 ? 9 : 11; }
    }

    #region decoder

    internal sealed class RangeDecoder
    {
        private byte[] _input;
        private int _pos;
        private int _end;
        private uint _range;
        private uint _code;

        public void Init(byte[] input, int offset, int count)
        {
            _input = input;
            _pos = offset;
            _end = offset + count;
            _range = 0xFFFFFFFF;
            _code = 0;
            if (NextByte() != 0)
                throw new LzmaException("Corrupt input data");
            for (int i = 0; i < 4; i++)
                _code = (_code << 8) | NextByte();
            if (_code == _range)
                throw new LzmaException("Corrupt input data");
        }

        public bool IsFinishedOK { get { return _code == 0; } }

        public int Position { get { return _pos; } }

        private byte NextByte()
        {
            if (_pos >= _end)
                throw new EndOfStreamException("Compressed file ended before the end-of-stream marker was reached");
            return _input[_pos++];
        }

        private void Normalize()
        {
            if (_range < LzmaConstants.TopValue)
            {
                _range <<= 8;
                _code = (_code << 8) | NextByte();
            }
        }

        public uint DecodeDirectBits(int numBits)
        {
            uint res = 0;
            do
            {
                _range >>= 1;
                _code -= _range;
                uint t = 0 - (_code >> 31);
                _code += _range & t;
                if (_code == _range)
                    throw new LzmaException("Corrupt input data");
                Normalize();
                res <<= 1;
                res += t + 1;
            } while (--numBits > 0);
            return res;
        }

        public int DecodeBit(ushort[] probs, int index)
        {
            uint v = probs[index];
            uint bound = (_range >> LzmaConstants.NumBitModelTotalBits) * v;
            int symbol;
            if (_code < bound)
            {
                v += ((1u << LzmaConstants.NumBitModelTotalBits) - v) >> LzmaConstants.NumMoveBits;
                _range = bound;
                symbol = 0;
            }
            else
            {
                v -= v >> LzmaConstants.NumMoveBits;
                _code -= bound;
                _range -= bound;
                symbol = 1;
            }
            probs[index] = (ushort)v;
            Normalize();
            return symbol;
        }

        public int BitTreeDecode(ushort[] probs, int offset, int numBits)
        {
            int m = 1;
            for (int i = 0; i < numBits; i++)
                m = (m << 1) + DecodeBit(probs, offset + m);
            return m - (1 << numBits);
        }

        public int BitTreeReverseDecode(ushort[] probs, int offset, int numBits)
        {
            int m = 1;
            int symbol = 0;
            for (int i = 0; i < numBits; i++)
            {
                int bit = DecodeBit(probs, offset + m);
                m = (m << 1) + bit;
                symbol |= bit << i;
            }
            return symbol;
        }
    }

    internal sealed class LenDecoder
    {
        private readonly ushort[] _choice = new ushort[2];
        private readonly ushort[] _low = new ushort[(1 << LzmaConstants.NumPosBitsMax) << 3];
        private readonly ushort[] _mid = new ushort[(1 << LzmaConstants.NumPosBitsMax) << 3];
        private readonly ushort[] _high = new ushort[256];

        public void Init()
        {
            LzmaConstants.InitProbs(_choice);
            LzmaConstants.InitProbs(_low);
            LzmaConstants.InitProbs(_mid);
            LzmaConstants.InitProbs(_high);
        }

        public int Decode(RangeDecoder rc, int posState)
        {
            if (rc.DecodeBit(_choice, 0) == 0)
                return rc.BitTreeDecode(_low, posState << 3, 3);
            if (rc.DecodeBit(_choice, 1) == 0)
                return 8 + rc.BitTreeDecode(_mid, posState << 3, 3);
            return 16 + rc.BitTreeDecode(_high, 0, 8);
        }
    }

    /// <summary>LZMA decoder state machine, usable for LZMA2 chunks and for the legacy .lzma format.</summary>
    internal sealed class LzmaDecoder
    {
        private int _lc, _lp, _pb;
        private ushort[] _literalProbs;
        private readonly ushort[] _posSlot = new ushort[LzmaConstants.NumLenToPosStates << 6];
        private readonly ushort[] _posDecoders = new ushort[1 + LzmaConstants.NumFullDistances - LzmaConstants.EndPosModelIndex];
        private readonly ushort[] _align = new ushort[1 << LzmaConstants.NumAlignBits];
        private readonly ushort[] _isMatch = new ushort[LzmaConstants.NumStates << LzmaConstants.NumPosBitsMax];
        private readonly ushort[] _isRep = new ushort[LzmaConstants.NumStates];
        private readonly ushort[] _isRepG0 = new ushort[LzmaConstants.NumStates];
        private readonly ushort[] _isRepG1 = new ushort[LzmaConstants.NumStates];
        private readonly ushort[] _isRepG2 = new ushort[LzmaConstants.NumStates];
        private readonly ushort[] _isRep0Long = new ushort[LzmaConstants.NumStates << LzmaConstants.NumPosBitsMax];
        private readonly LenDecoder _lenDecoder = new LenDecoder();
        private readonly LenDecoder _repLenDecoder = new LenDecoder();
        private int _state;
        private uint _rep0, _rep1, _rep2, _rep3;

        /// <summary>Output position at the last dictionary reset.</summary>
        public int DictStart;

        public void SetProperties(int lc, int lp, int pb)
        {
            if (lc > 8 || lp > 4 || pb > 4)
                throw new LzmaException("Invalid or unsupported options");
            _lc = lc;
            _lp = lp;
            _pb = pb;
            _literalProbs = new ushort[0x300 << (lc + lp)];
        }

        public void SetProperties(byte props)
        {
            if (props >= 9 * 5 * 5)
                throw new LzmaException("Invalid or unsupported options");
            int lc = props % 9;
            props /= 9;
            SetProperties(lc, props % 5, props / 5);
        }

        public void ResetState()
        {
            LzmaConstants.InitProbs(_literalProbs);
            LzmaConstants.InitProbs(_posSlot);
            LzmaConstants.InitProbs(_posDecoders);
            LzmaConstants.InitProbs(_align);
            LzmaConstants.InitProbs(_isMatch);
            LzmaConstants.InitProbs(_isRep);
            LzmaConstants.InitProbs(_isRepG0);
            LzmaConstants.InitProbs(_isRepG1);
            LzmaConstants.InitProbs(_isRepG2);
            LzmaConstants.InitProbs(_isRep0Long);
            _lenDecoder.Init();
            _repLenDecoder.Init();
            _state = 0;
            _rep0 = _rep1 = _rep2 = _rep3 = 0;
        }

        private void DecodeLiteral(RangeDecoder rc, ByteBuffer output)
        {
            int dictPos = output.Length - DictStart;
            int prevByte = dictPos > 0 ? output[output.Length - 1] : 0;
            int litState = ((dictPos & ((1 << _lp) - 1)) << _lc) + (prevByte >> (8 - _lc));
            int baseIndex = 0x300 * litState;
            int symbol = 1;
            if (_state >= 7)
            {
                int matchByte = output[output.Length - (int)_rep0 - 1];
                do
                {
                    int matchBit = (matchByte >> 7) & 1;
                    matchByte <<= 1;
                    int bit = rc.DecodeBit(_literalProbs, baseIndex + ((1 + matchBit) << 8) + symbol);
                    symbol = (symbol << 1) | bit;
                    if (matchBit != bit)
                        break;
                } while (symbol < 0x100);
            }
            while (symbol < 0x100)
                symbol = (symbol << 1) | rc.DecodeBit(_literalProbs, baseIndex + symbol);
            output.Add((byte)(symbol - 0x100));
        }

        private uint DecodeDistance(RangeDecoder rc, int len)
        {
            int lenState = Math.Min(len, LzmaConstants.NumLenToPosStates - 1);
            int posSlot = rc.BitTreeDecode(_posSlot, lenState << 6, 6);
            if (posSlot < 4)
                return (uint)posSlot;
            int numDirectBits = (posSlot >> 1) - 1;
            uint dist = (uint)((2 | (posSlot & 1)) << numDirectBits);
            if (posSlot < LzmaConstants.EndPosModelIndex)
            {
                dist += (uint)rc.BitTreeReverseDecode(_posDecoders, (int)dist - posSlot, numDirectBits);
            }
            else
            {
                dist += rc.DecodeDirectBits(numDirectBits - LzmaConstants.NumAlignBits) << LzmaConstants.NumAlignBits;
                dist += (uint)rc.BitTreeReverseDecode(_align, 0, LzmaConstants.NumAlignBits);
            }
            return dist;
        }

        /// <summary>
        /// Decodes until <paramref name="unpackSize"/> bytes have been produced (if non-negative) or an end marker
        /// was found. Returns true if an end marker was found.
        /// </summary>
        public bool Decode(RangeDecoder rc, ByteBuffer output, long unpackSize, bool allowEndMarker)
        {
            long target = unpackSize >= 0 ? output.Length + unpackSize : long.MaxValue;
            int pbMask = (1 << _pb) - 1;
            while (output.Length < target)
            {
                int dictPos = output.Length - DictStart;
                int posState = dictPos & pbMask;
                if (rc.DecodeBit(_isMatch, (_state << LzmaConstants.NumPosBitsMax) + posState) == 0)
                {
                    DecodeLiteral(rc, output);
                    _state = LzmaConstants.UpdateStateLiteral(_state);
                    continue;
                }
                int len;
                if (rc.DecodeBit(_isRep, _state) != 0)
                {
                    if (dictPos == 0)
                        throw new LzmaException("Corrupt input data");
                    if (rc.DecodeBit(_isRepG0, _state) == 0)
                    {
                        if (rc.DecodeBit(_isRep0Long, (_state << LzmaConstants.NumPosBitsMax) + posState) == 0)
                        {
                            _state = LzmaConstants.UpdateStateShortRep(_state);
                            output.Add(output[output.Length - (int)_rep0 - 1]);
                            continue;
                        }
                    }
                    else
                    {
                        uint dist;
                        if (rc.DecodeBit(_isRepG1, _state) == 0)
                        {
                            dist = _rep1;
                        }
                        else
                        {
                            if (rc.DecodeBit(_isRepG2, _state) == 0)
                            {
                                dist = _rep2;
                            }
                            else
                            {
                                dist = _rep3;
                                _rep3 = _rep2;
                            }
                            _rep2 = _rep1;
                        }
                        _rep1 = _rep0;
                        _rep0 = dist;
                    }
                    len = _repLenDecoder.Decode(rc, posState);
                    _state = LzmaConstants.UpdateStateRep(_state);
                }
                else
                {
                    _rep3 = _rep2;
                    _rep2 = _rep1;
                    _rep1 = _rep0;
                    len = _lenDecoder.Decode(rc, posState);
                    _state = LzmaConstants.UpdateStateMatch(_state);
                    _rep0 = DecodeDistance(rc, len);
                    if (_rep0 == 0xFFFFFFFF)
                    {
                        if (!allowEndMarker)
                            throw new LzmaException("Corrupt input data");
                        return true;
                    }
                    if (_rep0 >= dictPos)
                        throw new LzmaException("Corrupt input data");
                }
                len += LzmaConstants.MatchMinLen;
                if (output.Length + len > target)
                    throw new LzmaException("Corrupt input data");
                output.CopyMatch((int)_rep0 + 1, len);
            }
            return false;
        }
    }

    #endregion

    #region encoder

    internal sealed class RangeEncoder
    {
        private ulong _low;
        private uint _range;
        private byte _cache;
        private long _cacheSize;
        private readonly MemoryStream _output = new MemoryStream();

        public RangeEncoder()
        {
            Reset();
        }

        public void Reset()
        {
            _low = 0;
            _range = 0xFFFFFFFF;
            _cache = 0;
            _cacheSize = 1;
            _output.SetLength(0);
        }

        public byte[] Finish()
        {
            for (int i = 0; i < 5; i++)
                ShiftLow();
            return _output.ToArray();
        }

        private void ShiftLow()
        {
            if ((uint)_low < 0xFF000000u || (int)(_low >> 32) != 0)
            {
                byte temp = _cache;
                do
                {
                    _output.WriteByte((byte)(temp + (byte)(_low >> 32)));
                    temp = 0xFF;
                } while (--_cacheSize != 0);
                _cache = (byte)((uint)_low >> 24);
            }
            _cacheSize++;
            _low = (uint)_low << 8;
        }

        public void EncodeBit(ushort[] probs, int index, int bit)
        {
            uint prob = probs[index];
            uint bound = (_range >> LzmaConstants.NumBitModelTotalBits) * prob;
            if (bit == 0)
            {
                _range = bound;
                probs[index] = (ushort)(prob + ((LzmaConstants.BitModelTotal - prob) >> LzmaConstants.NumMoveBits));
            }
            else
            {
                _low += bound;
                _range -= bound;
                probs[index] = (ushort)(prob - (prob >> LzmaConstants.NumMoveBits));
            }
            while (_range < LzmaConstants.TopValue)
            {
                _range <<= 8;
                ShiftLow();
            }
        }

        public void EncodeDirectBits(uint value, int numBits)
        {
            for (int i = numBits - 1; i >= 0; i--)
            {
                _range >>= 1;
                if (((value >> i) & 1) != 0)
                    _low += _range;
                while (_range < LzmaConstants.TopValue)
                {
                    _range <<= 8;
                    ShiftLow();
                }
            }
        }

        public void BitTreeEncode(ushort[] probs, int offset, int numBits, int symbol)
        {
            int m = 1;
            for (int i = numBits - 1; i >= 0; i--)
            {
                int bit = (symbol >> i) & 1;
                EncodeBit(probs, offset + m, bit);
                m = (m << 1) | bit;
            }
        }

        public void BitTreeReverseEncode(ushort[] probs, int offset, int numBits, int symbol)
        {
            int m = 1;
            for (int i = 0; i < numBits; i++)
            {
                int bit = symbol & 1;
                symbol >>= 1;
                EncodeBit(probs, offset + m, bit);
                m = (m << 1) | bit;
            }
        }
    }

    internal sealed class LenEncoder
    {
        private readonly ushort[] _choice = new ushort[2];
        private readonly ushort[] _low = new ushort[(1 << LzmaConstants.NumPosBitsMax) << 3];
        private readonly ushort[] _mid = new ushort[(1 << LzmaConstants.NumPosBitsMax) << 3];
        private readonly ushort[] _high = new ushort[256];

        public void Init()
        {
            LzmaConstants.InitProbs(_choice);
            LzmaConstants.InitProbs(_low);
            LzmaConstants.InitProbs(_mid);
            LzmaConstants.InitProbs(_high);
        }

        public void Encode(RangeEncoder rc, int len, int posState)
        {
            if (len < 8)
            {
                rc.EncodeBit(_choice, 0, 0);
                rc.BitTreeEncode(_low, posState << 3, 3, len);
            }
            else if (len < 16)
            {
                rc.EncodeBit(_choice, 0, 1);
                rc.EncodeBit(_choice, 1, 0);
                rc.BitTreeEncode(_mid, posState << 3, 3, len - 8);
            }
            else
            {
                rc.EncodeBit(_choice, 0, 1);
                rc.EncodeBit(_choice, 1, 1);
                rc.BitTreeEncode(_high, 0, 8, len - 16);
            }
        }
    }

    /// <summary>
    /// A simple greedy LZMA encoder (hash chain match finder) producing LZMA2 chunk payloads. Its output is
    /// decodable by any LZMA2 decoder (such as Python's lzma module / liblzma).
    /// </summary>
    internal sealed class LzmaEncoder
    {
        public const int Lc = 3, Lp = 0, Pb = 2;

        private const int HashBits = 16;
        private const int MaxChainLength = 64;
        private const int NiceLength = 128;

        private readonly byte[] _data;
        private readonly int[] _head = new int[1 << HashBits];
        private readonly int[] _prev;
        private int _inserted;

        private readonly ushort[] _literalProbs = new ushort[0x300 << (Lc + Lp)];
        private readonly ushort[] _posSlot = new ushort[LzmaConstants.NumLenToPosStates << 6];
        private readonly ushort[] _posEncoders = new ushort[1 + LzmaConstants.NumFullDistances - LzmaConstants.EndPosModelIndex];
        private readonly ushort[] _align = new ushort[1 << LzmaConstants.NumAlignBits];
        private readonly ushort[] _isMatch = new ushort[LzmaConstants.NumStates << LzmaConstants.NumPosBitsMax];
        private readonly ushort[] _isRep = new ushort[LzmaConstants.NumStates];
        private readonly ushort[] _isRepG0 = new ushort[LzmaConstants.NumStates];
        private readonly ushort[] _isRepG1 = new ushort[LzmaConstants.NumStates];
        private readonly ushort[] _isRepG2 = new ushort[LzmaConstants.NumStates];
        private readonly ushort[] _isRep0Long = new ushort[LzmaConstants.NumStates << LzmaConstants.NumPosBitsMax];
        private readonly LenEncoder _lenEncoder = new LenEncoder();
        private readonly LenEncoder _repLenEncoder = new LenEncoder();
        private int _state;
        private readonly int[] _reps = new int[4];

        private readonly RangeEncoder _rc = new RangeEncoder();

        public LzmaEncoder(byte[] data)
        {
            _data = data;
            _prev = new int[Math.Max(1, data.Length)];
            for (int i = 0; i < _head.Length; i++)
                _head[i] = -1;
            ResetState();
        }

        public void ResetState()
        {
            LzmaConstants.InitProbs(_literalProbs);
            LzmaConstants.InitProbs(_posSlot);
            LzmaConstants.InitProbs(_posEncoders);
            LzmaConstants.InitProbs(_align);
            LzmaConstants.InitProbs(_isMatch);
            LzmaConstants.InitProbs(_isRep);
            LzmaConstants.InitProbs(_isRepG0);
            LzmaConstants.InitProbs(_isRepG1);
            LzmaConstants.InitProbs(_isRepG2);
            LzmaConstants.InitProbs(_isRep0Long);
            _lenEncoder.Init();
            _repLenEncoder.Init();
            _state = 0;
            _reps[0] = _reps[1] = _reps[2] = _reps[3] = 0;
        }

        private int Hash(int pos)
        {
            int h = _data[pos] | (_data[pos + 1] << 8) | (_data[pos + 2] << 16);
            return (int)(((uint)h * 2654435761u) >> (32 - HashBits));
        }

        /// <summary>Inserts all positions before <paramref name="pos"/> into the hash chains.</summary>
        private void InsertUpTo(int pos)
        {
            int limit = Math.Min(pos, _data.Length - 2);
            while (_inserted < limit)
            {
                int h = Hash(_inserted);
                _prev[_inserted] = _head[h];
                _head[h] = _inserted;
                _inserted++;
            }
            if (_inserted < pos)
                _inserted = pos;
        }

        private int MatchLength(int pos, int distance, int maxLen)
        {
            int src = pos - distance - 1;
            int len = 0;
            while (len < maxLen && _data[src + len] == _data[pos + len])
                len++;
            return len;
        }

        private int FindLongestMatch(int pos, int maxLen, out int bestDistance)
        {
            bestDistance = 0;
            int bestLen = 0;
            if (maxLen < 3 || pos + 2 >= _data.Length)
                return 0;
            InsertUpTo(pos);
            int candidate = _head[Hash(pos)];
            int chain = MaxChainLength;
            while (candidate >= 0 && chain-- > 0)
            {
                if (_data[candidate + bestLen] == _data[pos + bestLen] || bestLen == 0)
                {
                    int distance = pos - candidate - 1;
                    int len = MatchLength(pos, distance, maxLen);
                    if (len > bestLen)
                    {
                        bestLen = len;
                        bestDistance = distance;
                        if (len >= maxLen || len >= NiceLength)
                            break;
                    }
                }
                candidate = _prev[candidate];
            }
            return bestLen;
        }

        /// <summary>
        /// Encodes data[start..end) as one LZMA chunk payload (range coder data, flushed). The dictionary
        /// consists of all data before <paramref name="start"/> (back to position 0).
        /// </summary>
        public byte[] EncodeChunk(int start, int end)
        {
            _rc.Reset();
            int pos = start;
            int pbMask = (1 << Pb) - 1;
            while (pos < end)
            {
                int posState = pos & pbMask;
                int avail = Math.Min(end - pos, LzmaConstants.MatchMaxLen);

                // Repeat matches
                int bestRepLen = 0, bestRepIndex = 0;
                if (avail >= 2)
                {
                    for (int i = 0; i < 4; i++)
                    {
                        if (_reps[i] >= pos)
                            continue;
                        int len = MatchLength(pos, _reps[i], avail);
                        if (len > bestRepLen)
                        {
                            bestRepLen = len;
                            bestRepIndex = i;
                        }
                    }
                }

                int distance = 0;
                int mainLen = avail >= 3 ? FindLongestMatch(pos, avail, out distance) : 0;

                if (bestRepLen >= 2 && (bestRepLen + 1 >= mainLen || (bestRepLen + 2 >= mainLen && distance >= (1 << 9))))
                {
                    EncodeRepMatch(bestRepIndex, bestRepLen, posState);
                    pos += bestRepLen;
                }
                else if (mainLen >= 3 && !(mainLen == 3 && distance >= (1 << 14)))
                {
                    EncodeMatch(distance, mainLen, posState);
                    pos += mainLen;
                }
                else if (pos > 0 && _reps[0] < pos && _data[pos - _reps[0] - 1] == _data[pos])
                {
                    EncodeShortRep(posState);
                    pos += 1;
                }
                else
                {
                    EncodeLiteral(pos, posState);
                    pos += 1;
                }
            }
            InsertUpTo(end);
            return _rc.Finish();
        }

        private void EncodeLiteral(int pos, int posState)
        {
            _rc.EncodeBit(_isMatch, (_state << LzmaConstants.NumPosBitsMax) + posState, 0);
            int prevByte = pos > 0 ? _data[pos - 1] : 0;
            int litState = ((pos & ((1 << Lp) - 1)) << Lc) + (prevByte >> (8 - Lc));
            int baseIndex = 0x300 * litState;
            int cur = _data[pos];
            int symbol = 1;
            if (_state >= 7)
            {
                int matchByte = _data[pos - _reps[0] - 1];
                bool matchMode = true;
                for (int i = 7; i >= 0; i--)
                {
                    int bit = (cur >> i) & 1;
                    if (matchMode)
                    {
                        int matchBit = (matchByte >> i) & 1;
                        _rc.EncodeBit(_literalProbs, baseIndex + ((1 + matchBit) << 8) + symbol, bit);
                        if (matchBit != bit)
                            matchMode = false;
                    }
                    else
                    {
                        _rc.EncodeBit(_literalProbs, baseIndex + symbol, bit);
                    }
                    symbol = (symbol << 1) | bit;
                }
            }
            else
            {
                for (int i = 7; i >= 0; i--)
                {
                    int bit = (cur >> i) & 1;
                    _rc.EncodeBit(_literalProbs, baseIndex + symbol, bit);
                    symbol = (symbol << 1) | bit;
                }
            }
            _state = LzmaConstants.UpdateStateLiteral(_state);
        }

        private void EncodeShortRep(int posState)
        {
            _rc.EncodeBit(_isMatch, (_state << LzmaConstants.NumPosBitsMax) + posState, 1);
            _rc.EncodeBit(_isRep, _state, 1);
            _rc.EncodeBit(_isRepG0, _state, 0);
            _rc.EncodeBit(_isRep0Long, (_state << LzmaConstants.NumPosBitsMax) + posState, 0);
            _state = LzmaConstants.UpdateStateShortRep(_state);
        }

        private void EncodeRepMatch(int repIndex, int len, int posState)
        {
            _rc.EncodeBit(_isMatch, (_state << LzmaConstants.NumPosBitsMax) + posState, 1);
            _rc.EncodeBit(_isRep, _state, 1);
            if (repIndex == 0)
            {
                _rc.EncodeBit(_isRepG0, _state, 0);
                _rc.EncodeBit(_isRep0Long, (_state << LzmaConstants.NumPosBitsMax) + posState, 1);
            }
            else
            {
                _rc.EncodeBit(_isRepG0, _state, 1);
                int dist = _reps[repIndex];
                if (repIndex == 1)
                {
                    _rc.EncodeBit(_isRepG1, _state, 0);
                }
                else
                {
                    _rc.EncodeBit(_isRepG1, _state, 1);
                    _rc.EncodeBit(_isRepG2, _state, repIndex == 3 ? 1 : 0);
                    if (repIndex == 3)
                        _reps[3] = _reps[2];
                    _reps[2] = _reps[1];
                }
                _reps[1] = _reps[0];
                _reps[0] = dist;
            }
            _repLenEncoder.Encode(_rc, len - LzmaConstants.MatchMinLen, posState);
            _state = LzmaConstants.UpdateStateRep(_state);
        }

        private static int GetPosSlot(int dist)
        {
            if (dist < 4)
                return dist;
            int n = 31;
            while (((uint)dist >> n) == 0)
                n--;
            return (n << 1) | ((dist >> (n - 1)) & 1);
        }

        private void EncodeMatch(int distance, int len, int posState)
        {
            _rc.EncodeBit(_isMatch, (_state << LzmaConstants.NumPosBitsMax) + posState, 1);
            _rc.EncodeBit(_isRep, _state, 0);
            int lenCode = len - LzmaConstants.MatchMinLen;
            _lenEncoder.Encode(_rc, lenCode, posState);
            int lenState = Math.Min(lenCode, LzmaConstants.NumLenToPosStates - 1);
            int posSlot = GetPosSlot(distance);
            _rc.BitTreeEncode(_posSlot, lenState << 6, 6, posSlot);
            if (posSlot >= LzmaConstants.StartPosModelIndex)
            {
                int footerBits = (posSlot >> 1) - 1;
                int baseDist = (2 | (posSlot & 1)) << footerBits;
                int reduced = distance - baseDist;
                if (posSlot < LzmaConstants.EndPosModelIndex)
                {
                    _rc.BitTreeReverseEncode(_posEncoders, baseDist - posSlot, footerBits, reduced);
                }
                else
                {
                    _rc.EncodeDirectBits((uint)reduced >> LzmaConstants.NumAlignBits, footerBits - LzmaConstants.NumAlignBits);
                    _rc.BitTreeReverseEncode(_align, 0, LzmaConstants.NumAlignBits, reduced & ((1 << LzmaConstants.NumAlignBits) - 1));
                }
            }
            _reps[3] = _reps[2];
            _reps[2] = _reps[1];
            _reps[1] = _reps[0];
            _reps[0] = distance;
            _state = LzmaConstants.UpdateStateMatch(_state);
        }
    }

    #endregion
}
