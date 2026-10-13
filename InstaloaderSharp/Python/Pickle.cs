using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Numerics;
using System.Text;

namespace InstaloaderSharp.Python
{
    /// <summary>
    /// Minimal implementation of Python's pickle format, sufficient to read and write the session files of
    /// Instaloader (which are pickled dicts mapping cookie names to cookie values).
    ///
    /// Unpickling supports all protocols (0 to 5) for the builtin types None, bool, int, float, str, bytes,
    /// tuple, list and dict. Pickling writes protocol 4, like Python 3.8+ does by default.
    /// </summary>
    public static class Pickle
    {
        private sealed class Mark
        {
            public static readonly Mark Instance = new Mark();
        }

        /// <summary>Python tuple, as returned by <see cref="Load"/>.</summary>
        public sealed class Tuple : List<object>
        {
            public Tuple(IEnumerable<object> items) : base(items) { }
        }

        /// <summary>Thrown for malformed or unsupported pickles (pickle.UnpicklingError).</summary>
        public class UnpicklingError : PyValueError
        {
            public UnpicklingError(string message) : base(message) { }
        }

        /// <summary>
        /// Python's pickle.load. Returns null (None), bool, long, BigInteger, double, string, byte[],
        /// <see cref="Tuple"/>, List&lt;object&gt; or Dictionary&lt;object, object&gt; (insertion ordered keys
        /// are not guaranteed for the latter; use <see cref="LoadStringDict"/> for ordered str dicts).
        /// </summary>
        public static object Load(Stream stream)
        {
            var reader = new BinaryReader(stream, Encoding.UTF8);
            var stack = new List<object>();
            var memo = new Dictionary<long, object>();
            while (true)
            {
                int op = stream.ReadByte();
                if (op < 0)
                    throw new EndOfStreamException("Ran out of input");
                switch ((char)op)
                {
                    case '\x80': // PROTO
                        reader.ReadByte();
                        break;
                    case '\x95': // FRAME
                        reader.ReadUInt64();
                        break;
                    case '.': // STOP
                        if (stack.Count == 0)
                            throw new UnpicklingError("unpickling stack underflow");
                        return stack[stack.Count - 1];
                    case '(': // MARK
                        stack.Add(Mark.Instance);
                        break;
                    case 'N': // NONE
                        stack.Add(null);
                        break;
                    case '\x88': // NEWTRUE
                        stack.Add(true);
                        break;
                    case '\x89': // NEWFALSE
                        stack.Add(false);
                        break;
                    case 'I': // INT
                        {
                            string line = ReadLine(stream);
                            if (line == "01")
                                stack.Add(true);
                            else if (line == "00")
                                stack.Add(false);
                            else
                                stack.Add(Normalize(BigInteger.Parse(line, CultureInfo.InvariantCulture)));
                            break;
                        }
                    case 'J': // BININT
                        stack.Add((long)reader.ReadInt32());
                        break;
                    case 'K': // BININT1
                        stack.Add((long)reader.ReadByte());
                        break;
                    case 'M': // BININT2
                        stack.Add((long)reader.ReadUInt16());
                        break;
                    case 'L': // LONG
                        {
                            string line = ReadLine(stream).TrimEnd('L');
                            stack.Add(Normalize(BigInteger.Parse(line, CultureInfo.InvariantCulture)));
                            break;
                        }
                    case '\x8a': // LONG1
                        {
                            int n = reader.ReadByte();
                            stack.Add(DecodeLong(reader.ReadBytes(n)));
                            break;
                        }
                    case '\x8b': // LONG4
                        {
                            int n = reader.ReadInt32();
                            stack.Add(DecodeLong(reader.ReadBytes(n)));
                            break;
                        }
                    case 'F': // FLOAT
                        stack.Add(double.Parse(ReadLine(stream), NumberStyles.Float, CultureInfo.InvariantCulture));
                        break;
                    case 'G': // BINFLOAT (big endian)
                        {
                            byte[] b = reader.ReadBytes(8);
                            Array.Reverse(b);
                            stack.Add(BitConverter.ToDouble(b, 0));
                            break;
                        }
                    case 'S': // STRING (protocol 0, python 2 str)
                        stack.Add(UnquoteString(ReadLine(stream)));
                        break;
                    case 'T': // BINSTRING
                        stack.Add(Latin1(reader.ReadBytes(reader.ReadInt32())));
                        break;
                    case 'U': // SHORT_BINSTRING
                        stack.Add(Latin1(reader.ReadBytes(reader.ReadByte())));
                        break;
                    case 'V': // UNICODE (raw-unicode-escape)
                        stack.Add(DecodeRawUnicodeEscape(ReadLineBytes(stream)));
                        break;
                    case 'X': // BINUNICODE
                        stack.Add(Encoding.UTF8.GetString(reader.ReadBytes(reader.ReadInt32())));
                        break;
                    case '\x8c': // SHORT_BINUNICODE
                        stack.Add(Encoding.UTF8.GetString(reader.ReadBytes(reader.ReadByte())));
                        break;
                    case '\x8d': // BINUNICODE8
                        stack.Add(Encoding.UTF8.GetString(reader.ReadBytes(checked((int)reader.ReadUInt64()))));
                        break;
                    case 'B': // BINBYTES
                        stack.Add(reader.ReadBytes(reader.ReadInt32()));
                        break;
                    case 'C': // SHORT_BINBYTES
                        stack.Add(reader.ReadBytes(reader.ReadByte()));
                        break;
                    case '\x8e': // BINBYTES8
                        stack.Add(reader.ReadBytes(checked((int)reader.ReadUInt64())));
                        break;
                    case '\x96': // BYTEARRAY8
                        stack.Add(reader.ReadBytes(checked((int)reader.ReadUInt64())));
                        break;
                    case ')': // EMPTY_TUPLE
                        stack.Add(new Tuple(new object[0]));
                        break;
                    case 't': // TUPLE
                        stack.Add(new Tuple(PopMark(stack)));
                        break;
                    case '\x85': // TUPLE1
                        stack.Add(new Tuple(PopN(stack, 1)));
                        break;
                    case '\x86': // TUPLE2
                        stack.Add(new Tuple(PopN(stack, 2)));
                        break;
                    case '\x87': // TUPLE3
                        stack.Add(new Tuple(PopN(stack, 3)));
                        break;
                    case ']': // EMPTY_LIST
                        stack.Add(new List<object>());
                        break;
                    case 'l': // LIST
                        stack.Add(new List<object>(PopMark(stack)));
                        break;
                    case 'a': // APPEND
                        {
                            object value = Pop(stack);
                            ((List<object>)Top(stack)).Add(value);
                            break;
                        }
                    case 'e': // APPENDS
                        {
                            List<object> items = PopMark(stack);
                            ((List<object>)Top(stack)).AddRange(items);
                            break;
                        }
                    case '}': // EMPTY_DICT
                        stack.Add(new OrderedDict());
                        break;
                    case 'd': // DICT
                        {
                            List<object> items = PopMark(stack);
                            var dict = new OrderedDict();
                            for (int i = 0; i + 1 < items.Count; i += 2)
                                dict.Set(items[i], items[i + 1]);
                            stack.Add(dict);
                            break;
                        }
                    case 's': // SETITEM
                        {
                            object value = Pop(stack);
                            object key = Pop(stack);
                            ((OrderedDict)Top(stack)).Set(key, value);
                            break;
                        }
                    case 'u': // SETITEMS
                        {
                            List<object> items = PopMark(stack);
                            var dict = (OrderedDict)Top(stack);
                            for (int i = 0; i + 1 < items.Count; i += 2)
                                dict.Set(items[i], items[i + 1]);
                            break;
                        }
                    case '\x8f': // EMPTY_SET
                        stack.Add(new List<object>());
                        break;
                    case '\x90': // ADDITEMS
                        {
                            List<object> items = PopMark(stack);
                            ((List<object>)Top(stack)).AddRange(items);
                            break;
                        }
                    case '\x91': // FROZENSET
                        stack.Add(new List<object>(PopMark(stack)));
                        break;
                    case '0': // POP
                        Pop(stack);
                        break;
                    case '2': // DUP
                        stack.Add(Top(stack));
                        break;
                    case '1': // POP_MARK
                        PopMark(stack);
                        break;
                    case 'p': // PUT
                        memo[long.Parse(ReadLine(stream), CultureInfo.InvariantCulture)] = Top(stack);
                        break;
                    case 'q': // BINPUT
                        memo[reader.ReadByte()] = Top(stack);
                        break;
                    case 'r': // LONG_BINPUT
                        memo[reader.ReadUInt32()] = Top(stack);
                        break;
                    case '\x94': // MEMOIZE
                        memo[memo.Count] = Top(stack);
                        break;
                    case 'g': // GET
                        stack.Add(MemoGet(memo, long.Parse(ReadLine(stream), CultureInfo.InvariantCulture)));
                        break;
                    case 'h': // BINGET
                        stack.Add(MemoGet(memo, reader.ReadByte()));
                        break;
                    case 'j': // LONG_BINGET
                        stack.Add(MemoGet(memo, reader.ReadUInt32()));
                        break;
                    default:
                        throw new UnpicklingError(string.Format(
                            "unsupported pickle opcode 0x{0:x2}; only plain data pickles are supported", op));
                }
            }
        }

        /// <summary>Loads a pickled dict of str to str, such as an Instaloader session file.</summary>
        public static List<KeyValuePair<string, string>> LoadStringDict(Stream stream)
        {
            object obj = Load(stream);
            var dict = obj as OrderedDict;
            if (dict == null)
                throw new UnpicklingError("pickled object is not a dict");
            var result = new List<KeyValuePair<string, string>>();
            foreach (var kv in dict.Items)
            {
                string key = kv.Key as string;
                if (key == null)
                    throw new UnpicklingError("pickled dict has a non-str key");
                string value = kv.Value as string ?? (kv.Value == null ? null : PyStr.Str(kv.Value));
                result.Add(new KeyValuePair<string, string>(key, value));
            }
            return result;
        }

        /// <summary>
        /// Writes a dict of str to str (None values are written as None) the way Python's pickle.dump with
        /// protocol 4 does.
        /// </summary>
        public static void DumpStringDict(IEnumerable<KeyValuePair<string, string>> dict, Stream stream)
        {
            var body = new MemoryStream();
            body.WriteByte((byte)'}'); // EMPTY_DICT
            body.WriteByte(0x94); // MEMOIZE
            int memoCount = 1;
            var memoizedStrings = new Dictionary<string, int>(StringComparer.Ordinal);
            var items = new List<KeyValuePair<string, string>>(dict);
            int index = 0;
            while (index < items.Count)
            {
                int batch = Math.Min(1000, items.Count - index);
                if (batch > 1)
                    body.WriteByte((byte)'('); // MARK
                for (int i = 0; i < batch; i++)
                {
                    var kv = items[index + i];
                    WriteString(body, kv.Key, memoizedStrings, ref memoCount);
                    if (kv.Value == null)
                        body.WriteByte((byte)'N');
                    else
                        WriteString(body, kv.Value, memoizedStrings, ref memoCount);
                }
                body.WriteByte(batch > 1 ? (byte)'u' : (byte)'s'); // SETITEMS or SETITEM
                index += batch;
            }
            body.WriteByte((byte)'.'); // STOP
            byte[] payload = body.ToArray();
            stream.WriteByte(0x80); // PROTO
            stream.WriteByte(4);
            stream.WriteByte(0x95); // FRAME
            stream.Write(BitConverter.GetBytes((ulong)payload.Length), 0, 8);
            stream.Write(payload, 0, payload.Length);
        }

        private static void WriteString(Stream s, string value, Dictionary<string, int> memoized, ref int memoCount)
        {
            int memoIndex;
            // CPython shares the empty string object, which pickle then memoizes and references with BINGET.
            if (value.Length == 0 && memoized.TryGetValue(value, out memoIndex))
            {
                if (memoIndex < 256)
                {
                    s.WriteByte((byte)'h');
                    s.WriteByte((byte)memoIndex);
                }
                else
                {
                    s.WriteByte((byte)'j');
                    s.Write(BitConverter.GetBytes((uint)memoIndex), 0, 4);
                }
                return;
            }
            byte[] bytes = Encoding.UTF8.GetBytes(value);
            if (bytes.Length < 256)
            {
                s.WriteByte(0x8c);
                s.WriteByte((byte)bytes.Length);
            }
            else
            {
                s.WriteByte((byte)'X');
                s.Write(BitConverter.GetBytes((uint)bytes.Length), 0, 4);
            }
            s.Write(bytes, 0, bytes.Length);
            s.WriteByte(0x94); // MEMOIZE
            if (value.Length == 0)
                memoized[value] = memoCount;
            memoCount++;
        }

        /// <summary>A dict as unpickled; keeps insertion order.</summary>
        public sealed class OrderedDict
        {
            private readonly List<KeyValuePair<object, object>> _items = new List<KeyValuePair<object, object>>();

            public List<KeyValuePair<object, object>> Items { get { return _items; } }

            public void Set(object key, object value)
            {
                for (int i = 0; i < _items.Count; i++)
                {
                    if (Equals(_items[i].Key, key))
                    {
                        _items[i] = new KeyValuePair<object, object>(key, value);
                        return;
                    }
                }
                _items.Add(new KeyValuePair<object, object>(key, value));
            }
        }

        private static object MemoGet(Dictionary<long, object> memo, long index)
        {
            object value;
            if (!memo.TryGetValue(index, out value))
                throw new UnpicklingError(string.Format("Memo value not found at index {0}", index));
            return value;
        }

        private static object Top(List<object> stack)
        {
            if (stack.Count == 0)
                throw new UnpicklingError("unpickling stack underflow");
            return stack[stack.Count - 1];
        }

        private static object Pop(List<object> stack)
        {
            object value = Top(stack);
            stack.RemoveAt(stack.Count - 1);
            return value;
        }

        private static List<object> PopN(List<object> stack, int n)
        {
            if (stack.Count < n)
                throw new UnpicklingError("unpickling stack underflow");
            var items = stack.GetRange(stack.Count - n, n);
            stack.RemoveRange(stack.Count - n, n);
            return items;
        }

        private static List<object> PopMark(List<object> stack)
        {
            for (int i = stack.Count - 1; i >= 0; i--)
            {
                if (ReferenceEquals(stack[i], Mark.Instance))
                {
                    var items = stack.GetRange(i + 1, stack.Count - i - 1);
                    stack.RemoveRange(i, stack.Count - i);
                    return items;
                }
            }
            throw new UnpicklingError("could not find MARK");
        }

        private static object Normalize(BigInteger value)
        {
            if (value >= long.MinValue && value <= long.MaxValue)
                return (long)value;
            return value;
        }

        private static object DecodeLong(byte[] bytes)
        {
            if (bytes.Length == 0)
                return 0L;
            // Little-endian two's complement, which is what BigInteger(byte[]) expects.
            return Normalize(new BigInteger(bytes));
        }

        private static byte[] ReadLineBytes(Stream stream)
        {
            var bytes = new List<byte>();
            while (true)
            {
                int b = stream.ReadByte();
                if (b < 0)
                    throw new EndOfStreamException("Ran out of input");
                if (b == '\n')
                    break;
                bytes.Add((byte)b);
            }
            return bytes.ToArray();
        }

        private static string ReadLine(Stream stream)
        {
            return Latin1(ReadLineBytes(stream)).TrimEnd('\r');
        }

        private static string Latin1(byte[] bytes)
        {
            var chars = new char[bytes.Length];
            for (int i = 0; i < bytes.Length; i++)
                chars[i] = (char)bytes[i];
            return new string(chars);
        }

        private static string UnquoteString(string repr)
        {
            if (repr.Length >= 2 && (repr[0] == '\'' || repr[0] == '"') && repr[repr.Length - 1] == repr[0])
                repr = repr.Substring(1, repr.Length - 2);
            return PyStr.DecodeEscapes(repr);
        }

        private static string DecodeRawUnicodeEscape(byte[] bytes)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < bytes.Length; i++)
            {
                if (bytes[i] == '\\' && i + 1 < bytes.Length && (bytes[i + 1] == 'u' || bytes[i + 1] == 'U'))
                {
                    int len = bytes[i + 1] == 'u' ? 4 : 8;
                    if (i + 2 + len <= bytes.Length)
                    {
                        string hex = Latin1(bytes).Substring(i + 2, len);
                        int code;
                        if (int.TryParse(hex, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out code))
                        {
                            sb.Append(char.ConvertFromUtf32(code));
                            i += 1 + len;
                            continue;
                        }
                    }
                }
                sb.Append((char)bytes[i]);
            }
            return sb.ToString();
        }
    }
}
