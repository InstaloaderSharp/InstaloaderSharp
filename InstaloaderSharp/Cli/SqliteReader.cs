using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace InstaloaderSharp.Cli
{
    /// <summary>
    /// A minimal read-only reader for SQLite database files (table b-trees only), including uncheckpointed changes
    /// in a write-ahead log (-wal) file. Used to read browser cookie databases.
    /// </summary>
    public sealed class SqliteReader
    {
        private readonly byte[] _db;
        private readonly int _pageSize;
        private readonly int _usableSize;
        private readonly Encoding _textEncoding;
        private readonly Dictionary<uint, byte[]> _walPages = new Dictionary<uint, byte[]>();

        public SqliteReader(string path)
        {
            _db = ReadShared(path);
            if (_db.Length < 100 || Encoding.ASCII.GetString(_db, 0, 15) != "SQLite format 3")
                throw new InvalidDataException("Not an SQLite 3 database: " + path);
            int pageSize = (_db[16] << 8) | _db[17];
            _pageSize = pageSize == 1 ? 65536 : pageSize;
            _usableSize = _pageSize - _db[20];
            int encoding = (int)ReadUInt32(_db, 56);
            _textEncoding = encoding == 2 ? Encoding.Unicode : encoding == 3 ? Encoding.BigEndianUnicode : (Encoding)new UTF8Encoding(false);
            string walPath = path + "-wal";
            if (File.Exists(walPath))
            {
                try
                {
                    LoadWal(ReadShared(walPath));
                }
                catch (IOException)
                {
                    // ignore an unreadable WAL; the main database is still consistent
                }
            }
        }

        private static byte[] ReadShared(string path)
        {
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                var ms = new MemoryStream();
                fs.CopyTo(ms);
                return ms.ToArray();
            }
        }

        private static uint ReadUInt32(byte[] b, int offset)
        {
            return (uint)((b[offset] << 24) | (b[offset + 1] << 16) | (b[offset + 2] << 8) | b[offset + 3]);
        }

        private void LoadWal(byte[] wal)
        {
            if (wal.Length < 32)
                return;
            uint magic = ReadUInt32(wal, 0);
            if (magic != 0x377f0682 && magic != 0x377f0683)
                return;
            bool bigEndian = (magic & 1) != 0;
            int pageSize = (int)ReadUInt32(wal, 8);
            if (pageSize != _pageSize)
                return;
            uint salt1 = ReadUInt32(wal, 16);
            uint salt2 = ReadUInt32(wal, 20);
            uint s0 = 0, s1 = 0;
            Checksum(wal, 0, 24, bigEndian, ref s0, ref s1);
            if (s0 != ReadUInt32(wal, 24) || s1 != ReadUInt32(wal, 28))
                return;
            var pending = new Dictionary<uint, byte[]>();
            int frameSize = 24 + pageSize;
            for (int offset = 32; offset + frameSize <= wal.Length; offset += frameSize)
            {
                if (ReadUInt32(wal, offset + 8) != salt1 || ReadUInt32(wal, offset + 12) != salt2)
                    break;
                Checksum(wal, offset, 8, bigEndian, ref s0, ref s1);
                Checksum(wal, offset + 24, pageSize, bigEndian, ref s0, ref s1);
                if (s0 != ReadUInt32(wal, offset + 16) || s1 != ReadUInt32(wal, offset + 20))
                    break;
                uint pageNumber = ReadUInt32(wal, offset);
                var page = new byte[pageSize];
                Buffer.BlockCopy(wal, offset + 24, page, 0, pageSize);
                pending[pageNumber] = page;
                if (ReadUInt32(wal, offset + 4) != 0)
                {
                    // commit frame
                    foreach (var kv in pending)
                        _walPages[kv.Key] = kv.Value;
                    pending.Clear();
                }
            }
        }

        private static void Checksum(byte[] data, int offset, int length, bool bigEndian, ref uint s0, ref uint s1)
        {
            for (int i = offset; i + 8 <= offset + length; i += 8)
            {
                uint x0 = bigEndian ? ReadUInt32(data, i) : BitConverter.ToUInt32(data, i);
                uint x1 = bigEndian ? ReadUInt32(data, i + 4) : BitConverter.ToUInt32(data, i + 4);
                s0 += x0 + s1;
                s1 += x1 + s0;
            }
        }

        private byte[] GetPage(uint pageNumber)
        {
            byte[] page;
            if (_walPages.TryGetValue(pageNumber, out page))
                return page;
            long offset = (long)(pageNumber - 1) * _pageSize;
            if (pageNumber == 0 || offset + _pageSize > _db.Length)
                throw new InvalidDataException("SQLite page out of range: " + pageNumber);
            page = new byte[_pageSize];
            Buffer.BlockCopy(_db, (int)offset, page, 0, _pageSize);
            return page;
        }

        private static long ReadVarint(byte[] b, ref int pos)
        {
            long result = 0;
            for (int i = 0; i < 8; i++)
            {
                byte c = b[pos++];
                result = (result << 7) | (long)(c & 0x7F);
                if ((c & 0x80) == 0)
                    return result;
            }
            result = (result << 8) | b[pos++];
            return result;
        }

        /// <summary>Iterates the records (rowid, column values) of a table b-tree.</summary>
        private IEnumerable<KeyValuePair<long, object[]>> ScanTable(uint rootPage)
        {
            var stack = new Stack<uint>();
            stack.Push(rootPage);
            while (stack.Count > 0)
            {
                uint pageNumber = stack.Pop();
                byte[] page = GetPage(pageNumber);
                int headerOffset = pageNumber == 1 ? 100 : 0;
                byte type = page[headerOffset];
                int cellCount = (page[headerOffset + 3] << 8) | page[headerOffset + 4];
                if (type == 0x05)
                {
                    uint rightMost = ReadUInt32(page, headerOffset + 8);
                    var children = new List<uint>();
                    for (int i = 0; i < cellCount; i++)
                    {
                        int cellOffset = (page[headerOffset + 12 + 2 * i] << 8) | page[headerOffset + 13 + 2 * i];
                        children.Add(ReadUInt32(page, cellOffset));
                    }
                    children.Add(rightMost);
                    for (int i = children.Count - 1; i >= 0; i--)
                        stack.Push(children[i]);
                }
                else if (type == 0x0D)
                {
                    for (int i = 0; i < cellCount; i++)
                    {
                        int pos = (page[headerOffset + 8 + 2 * i] << 8) | page[headerOffset + 9 + 2 * i];
                        long payloadSize = ReadVarint(page, ref pos);
                        long rowid = ReadVarint(page, ref pos);
                        byte[] payload = ReadPayload(page, pos, payloadSize);
                        yield return new KeyValuePair<long, object[]>(rowid, DecodeRecord(payload));
                    }
                }
                else
                {
                    throw new InvalidDataException("Unexpected SQLite b-tree page type " + type);
                }
            }
        }

        private byte[] ReadPayload(byte[] page, int pos, long payloadSize)
        {
            int u = _usableSize;
            int x = u - 35;
            var payload = new byte[payloadSize];
            if (payloadSize <= x)
            {
                Buffer.BlockCopy(page, pos, payload, 0, (int)payloadSize);
                return payload;
            }
            int m = ((u - 12) * 32 / 255) - 23;
            int k = (int)(m + ((payloadSize - m) % (u - 4)));
            int local = k <= x ? k : m;
            Buffer.BlockCopy(page, pos, payload, 0, local);
            int copied = local;
            uint overflow = ReadUInt32(page, pos + local);
            while (copied < payloadSize && overflow != 0)
            {
                byte[] ov = GetPage(overflow);
                int n = (int)Math.Min(u - 4, payloadSize - copied);
                Buffer.BlockCopy(ov, 4, payload, copied, n);
                copied += n;
                overflow = ReadUInt32(ov, 0);
            }
            return payload;
        }

        private object[] DecodeRecord(byte[] payload)
        {
            int pos = 0;
            long headerSize = ReadVarint(payload, ref pos);
            var types = new List<long>();
            while (pos < headerSize)
                types.Add(ReadVarint(payload, ref pos));
            int dataPos = (int)headerSize;
            var values = new object[types.Count];
            for (int i = 0; i < types.Count; i++)
            {
                long t = types[i];
                switch (t)
                {
                    case 0: values[i] = null; break;
                    case 1: values[i] = (long)(sbyte)payload[dataPos]; dataPos += 1; break;
                    case 2: values[i] = ReadInt(payload, dataPos, 2); dataPos += 2; break;
                    case 3: values[i] = ReadInt(payload, dataPos, 3); dataPos += 3; break;
                    case 4: values[i] = ReadInt(payload, dataPos, 4); dataPos += 4; break;
                    case 5: values[i] = ReadInt(payload, dataPos, 6); dataPos += 6; break;
                    case 6: values[i] = ReadInt(payload, dataPos, 8); dataPos += 8; break;
                    case 7:
                        {
                            var b = new byte[8];
                            Buffer.BlockCopy(payload, dataPos, b, 0, 8);
                            Array.Reverse(b);
                            values[i] = BitConverter.ToDouble(b, 0);
                            dataPos += 8;
                            break;
                        }
                    case 8: values[i] = 0L; break;
                    case 9: values[i] = 1L; break;
                    default:
                        if (t >= 12)
                        {
                            int len = (int)((t - (t % 2 == 0 ? 12 : 13)) / 2);
                            if (t % 2 == 0)
                            {
                                var blob = new byte[len];
                                Buffer.BlockCopy(payload, dataPos, blob, 0, len);
                                values[i] = blob;
                            }
                            else
                            {
                                values[i] = _textEncoding.GetString(payload, dataPos, len);
                            }
                            dataPos += len;
                        }
                        break;
                }
            }
            return values;
        }

        private static long ReadInt(byte[] b, int offset, int size)
        {
            long v = (sbyte)b[offset];
            for (int i = 1; i < size; i++)
                v = (v << 8) | b[offset + i];
            return v;
        }

        private sealed class TableInfo
        {
            public uint RootPage;
            public List<string> Columns;
            public int IntegerPrimaryKey = -1;
        }

        private TableInfo FindTable(string name)
        {
            foreach (var row in ScanTable(1))
            {
                object[] v = row.Value;
                if (v.Length >= 5 && "table".Equals(v[0]) && name.Equals(v[1] as string, StringComparison.OrdinalIgnoreCase))
                {
                    var info = new TableInfo { RootPage = (uint)(long)v[3], Columns = new List<string>() };
                    ParseColumns((string)v[4], info);
                    return info;
                }
            }
            throw new InvalidDataException("no such table: " + name);
        }

        private static void ParseColumns(string sql, TableInfo info)
        {
            int open = sql.IndexOf('(');
            int close = sql.LastIndexOf(')');
            string body = sql.Substring(open + 1, close - open - 1);
            var parts = new List<string>();
            int depth = 0;
            var current = new StringBuilder();
            char quote = '\0';
            foreach (char c in body)
            {
                if (quote != '\0')
                {
                    current.Append(c);
                    if (c == quote)
                        quote = '\0';
                    continue;
                }
                if (c == '\'' || c == '"' || c == '`')
                    quote = c;
                if (c == '[')
                    quote = ']';
                if (c == '(')
                    depth++;
                if (c == ')')
                    depth--;
                if (c == ',' && depth == 0)
                {
                    parts.Add(current.ToString());
                    current.Clear();
                    continue;
                }
                current.Append(c);
            }
            parts.Add(current.ToString());
            foreach (string part in parts)
            {
                string p = part.Trim();
                string upper = p.ToUpperInvariant();
                if (upper.StartsWith("PRIMARY KEY") || upper.StartsWith("UNIQUE") || upper.StartsWith("CONSTRAINT") ||
                    upper.StartsWith("CHECK") || upper.StartsWith("FOREIGN KEY"))
                    continue;
                Match m = Regex.Match(p, "^(?:\"([^\"]*)\"|`([^`]*)`|\\[([^\\]]*)\\]|'([^']*)'|(\\S+))(.*)$", RegexOptions.Singleline);
                if (!m.Success)
                    continue;
                string column = m.Groups[1].Success ? m.Groups[1].Value
                    : m.Groups[2].Success ? m.Groups[2].Value
                    : m.Groups[3].Success ? m.Groups[3].Value
                    : m.Groups[4].Success ? m.Groups[4].Value
                    : m.Groups[5].Value;
                string rest = m.Groups[6].Value.ToUpperInvariant();
                if (Regex.IsMatch(rest, @"^\s*INTEGER\s+PRIMARY\s+KEY"))
                    info.IntegerPrimaryKey = info.Columns.Count;
                info.Columns.Add(column);
            }
        }

        /// <summary>Reads all rows of a table as dictionaries of column name to value.</summary>
        public IEnumerable<Dictionary<string, object>> ReadTable(string tableName)
        {
            TableInfo info = FindTable(tableName);
            foreach (var row in ScanTable(info.RootPage))
            {
                var dict = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < info.Columns.Count; i++)
                {
                    object value = i < row.Value.Length ? row.Value[i] : null;
                    if (i == info.IntegerPrimaryKey && value == null)
                        value = row.Key;
                    dict[info.Columns[i]] = value;
                }
                yield return dict;
            }
        }
    }
}
