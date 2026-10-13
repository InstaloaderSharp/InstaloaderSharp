using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using System.Text;
using InstaloaderSharp.Python;

namespace InstaloaderSharp.Json
{
    /// <summary>
    /// JSON encoding and decoding that behaves like Python's json module, so that files written by this program
    /// are byte-identical to those written by Python's Instaloader (and vice versa).
    /// </summary>
    public static class PyJson
    {
        #region decoding

        /// <summary>Python's json.loads().</summary>
        public static JToken Loads(string s)
        {
            if (s == null)
                throw new ArgumentNullException("s");
            var parser = new Parser(s);
            return parser.ParseDocument();
        }

        private sealed class Parser
        {
            private readonly string _s;
            private int _pos;

            public Parser(string s)
            {
                _s = s;
                // Be lenient about a byte order mark.
                if (_s.Length > 0 && _s[0] == (char)0xFEFF)
                    _pos = 1;
            }

            public JToken ParseDocument()
            {
                SkipWhitespace();
                JToken value = ParseValue();
                SkipWhitespace();
                if (_pos != _s.Length)
                    throw new JsonDecodeError("Extra data", _s, _pos);
                return value;
            }

            private void SkipWhitespace()
            {
                while (_pos < _s.Length)
                {
                    char c = _s[_pos];
                    if (c == ' ' || c == '\t' || c == '\n' || c == '\r')
                        _pos++;
                    else
                        break;
                }
            }

            private bool StartsWith(string token)
            {
                return string.CompareOrdinal(_s, _pos, token, 0, token.Length) == 0;
            }

            private JToken ParseValue()
            {
                if (_pos >= _s.Length)
                    throw new JsonDecodeError("Expecting value", _s, _pos);
                char c = _s[_pos];
                switch (c)
                {
                    case '{':
                        return ParseObject();
                    case '[':
                        return ParseArray();
                    case '"':
                        return new JValue(ParseString());
                    case 'n':
                        if (StartsWith("null")) { _pos += 4; return JValue.Null; }
                        break;
                    case 't':
                        if (StartsWith("true")) { _pos += 4; return JValue.True; }
                        break;
                    case 'f':
                        if (StartsWith("false")) { _pos += 5; return JValue.False; }
                        break;
                    case 'N':
                        if (StartsWith("NaN")) { _pos += 3; return new JValue(double.NaN); }
                        break;
                    case 'I':
                        if (StartsWith("Infinity")) { _pos += 8; return new JValue(double.PositiveInfinity); }
                        break;
                    case '-':
                        if (StartsWith("-Infinity")) { _pos += 9; return new JValue(double.NegativeInfinity); }
                        break;
                }
                if (c == '-' || (c >= '0' && c <= '9'))
                {
                    JToken number = ParseNumber();
                    if (number != null)
                        return number;
                }
                throw new JsonDecodeError("Expecting value", _s, _pos);
            }

            private JToken ParseNumber()
            {
                int start = _pos;
                int i = _pos;
                if (i < _s.Length && _s[i] == '-')
                    i++;
                if (i >= _s.Length)
                    return null;
                if (_s[i] == '0')
                {
                    i++;
                }
                else if (_s[i] >= '1' && _s[i] <= '9')
                {
                    while (i < _s.Length && _s[i] >= '0' && _s[i] <= '9')
                        i++;
                }
                else
                {
                    return null;
                }
                bool isFloat = false;
                if (i + 1 < _s.Length && _s[i] == '.' && _s[i + 1] >= '0' && _s[i + 1] <= '9')
                {
                    isFloat = true;
                    i += 2;
                    while (i < _s.Length && _s[i] >= '0' && _s[i] <= '9')
                        i++;
                }
                if (i < _s.Length && (_s[i] == 'e' || _s[i] == 'E'))
                {
                    int j = i + 1;
                    if (j < _s.Length && (_s[j] == '+' || _s[j] == '-'))
                        j++;
                    if (j < _s.Length && _s[j] >= '0' && _s[j] <= '9')
                    {
                        while (j < _s.Length && _s[j] >= '0' && _s[j] <= '9')
                            j++;
                        isFloat = true;
                        i = j;
                    }
                }
                string text = _s.Substring(start, i - start);
                _pos = i;
                if (isFloat)
                {
                    double d = double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
                    // .NET Framework parses "-0.0" as positive zero.
                    if (d == 0.0 && text[0] == '-' && 1.0 / d > 0)
                        d = -d;
                    return new JValue(d);
                }
                long l;
                if (long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out l))
                    return new JValue(l);
                return new JValue(BigInteger.Parse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture));
            }

            private string ParseString()
            {
                int begin = _pos;
                _pos++; // opening quote
                var sb = new StringBuilder();
                while (true)
                {
                    if (_pos >= _s.Length)
                        throw new JsonDecodeError("Unterminated string starting at", _s, begin);
                    char c = _s[_pos];
                    if (c == '"')
                    {
                        _pos++;
                        return sb.ToString();
                    }
                    if (c == '\\')
                    {
                        _pos++;
                        if (_pos >= _s.Length)
                            throw new JsonDecodeError("Unterminated string starting at", _s, begin);
                        char e = _s[_pos];
                        switch (e)
                        {
                            case '"': sb.Append('"'); _pos++; break;
                            case '\\': sb.Append('\\'); _pos++; break;
                            case '/': sb.Append('/'); _pos++; break;
                            case 'b': sb.Append('\b'); _pos++; break;
                            case 'f': sb.Append('\f'); _pos++; break;
                            case 'n': sb.Append('\n'); _pos++; break;
                            case 'r': sb.Append('\r'); _pos++; break;
                            case 't': sb.Append('\t'); _pos++; break;
                            case 'u':
                                {
                                    if (_pos + 5 > _s.Length)
                                        throw new JsonDecodeError("Invalid \\uXXXX escape", _s, _pos - 1);
                                    int code;
                                    if (!int.TryParse(_s.Substring(_pos + 1, 4), NumberStyles.AllowHexSpecifier,
                                                      CultureInfo.InvariantCulture, out code))
                                        throw new JsonDecodeError("Invalid \\uXXXX escape", _s, _pos - 1);
                                    sb.Append((char)code);
                                    _pos += 5;
                                    break;
                                }
                            default:
                                throw new JsonDecodeError("Invalid \\escape", _s, _pos - 1);
                        }
                        continue;
                    }
                    if (c < 0x20)
                        throw new JsonDecodeError("Invalid control character at", _s, _pos);
                    sb.Append(c);
                    _pos++;
                }
            }

            private JObject ParseObject()
            {
                int begin = _pos;
                _pos++;
                var obj = new JObject();
                SkipWhitespace();
                if (_pos < _s.Length && _s[_pos] == '}')
                {
                    _pos++;
                    return obj;
                }
                while (true)
                {
                    if (_pos >= _s.Length || _s[_pos] != '"')
                        throw new JsonDecodeError("Expecting property name enclosed in double quotes", _s, _pos);
                    string key = ParseString();
                    SkipWhitespace();
                    if (_pos >= _s.Length || _s[_pos] != ':')
                        throw new JsonDecodeError("Expecting ':' delimiter", _s, _pos);
                    _pos++;
                    SkipWhitespace();
                    obj[key] = ParseValue();
                    SkipWhitespace();
                    if (_pos < _s.Length && _s[_pos] == '}')
                    {
                        _pos++;
                        return obj;
                    }
                    if (_pos >= _s.Length || _s[_pos] != ',')
                        throw new JsonDecodeError("Expecting ',' delimiter", _s, _pos);
                    _pos++;
                    SkipWhitespace();
                    if (_pos < _s.Length && _s[_pos] == '}')
                        throw new JsonDecodeError("Illegal trailing comma before end of object", _s, _pos - 1);
                }
            }

            private JArray ParseArray()
            {
                _pos++;
                var arr = new JArray();
                SkipWhitespace();
                if (_pos < _s.Length && _s[_pos] == ']')
                {
                    _pos++;
                    return arr;
                }
                while (true)
                {
                    arr.Add(ParseValue());
                    SkipWhitespace();
                    if (_pos < _s.Length && _s[_pos] == ']')
                    {
                        _pos++;
                        return arr;
                    }
                    if (_pos >= _s.Length || _s[_pos] != ',')
                        throw new JsonDecodeError("Expecting ',' delimiter", _s, _pos);
                    _pos++;
                    SkipWhitespace();
                    if (_pos < _s.Length && _s[_pos] == ']')
                        throw new JsonDecodeError("Illegal trailing comma before end of array", _s, _pos - 1);
                }
            }
        }

        #endregion

        #region encoding

        /// <summary>
        /// Python's json.dumps(obj, indent=indent, separators=separators, sort_keys=sortKeys) with the default
        /// ensure_ascii=True. Newlines are '\n' (as in Python; text-mode file writing converts them).
        /// </summary>
        /// <param name="token">Value to encode.</param>
        /// <param name="indent">Indentation, or null for single-line output.</param>
        /// <param name="itemSeparator">Item separator; default ', ' if indent is null, ',' otherwise.</param>
        /// <param name="keySeparator">Key separator; default ': '.</param>
        /// <param name="sortKeys">Whether to sort dictionary keys.</param>
        public static string Dumps(JToken token, int? indent = null, string itemSeparator = null,
                                   string keySeparator = null, bool sortKeys = false)
        {
            if (itemSeparator == null)
                itemSeparator = indent.HasValue ? "," : ", ";
            if (keySeparator == null)
                keySeparator = ": ";
            var sb = new StringBuilder();
            Encode(sb, token ?? JValue.Null, indent, itemSeparator, keySeparator, sortKeys, 0);
            return sb.ToString();
        }

        private static void Encode(StringBuilder sb, JToken token, int? indent, string itemSep, string keySep,
                                   bool sortKeys, int level)
        {
            switch (token.Type)
            {
                case JTokenType.Null:
                    sb.Append("null");
                    return;
                case JTokenType.Boolean:
                    sb.Append((bool)token.Value ? "true" : "false");
                    return;
                case JTokenType.Integer:
                    sb.Append(Convert.ToString(token.Value, CultureInfo.InvariantCulture));
                    return;
                case JTokenType.Float:
                    sb.Append(FloatString((double)token.Value));
                    return;
                case JTokenType.String:
                    EncodeString(sb, (string)token.Value);
                    return;
                case JTokenType.Array:
                    {
                        var arr = (JArray)token;
                        if (arr.Count == 0)
                        {
                            sb.Append("[]");
                            return;
                        }
                        sb.Append('[');
                        string newlineIndent = null;
                        string separator = itemSep;
                        if (indent.HasValue)
                        {
                            newlineIndent = "\n" + new string(' ', indent.Value * (level + 1));
                            separator = itemSep + newlineIndent;
                            sb.Append(newlineIndent);
                        }
                        bool first = true;
                        foreach (JToken item in arr)
                        {
                            if (!first)
                                sb.Append(separator);
                            first = false;
                            Encode(sb, item, indent, itemSep, keySep, sortKeys, level + 1);
                        }
                        if (indent.HasValue)
                        {
                            sb.Append('\n');
                            sb.Append(' ', indent.Value * level);
                        }
                        sb.Append(']');
                        return;
                    }
                case JTokenType.Object:
                    {
                        var obj = (JObject)token;
                        if (obj.Count == 0)
                        {
                            sb.Append("{}");
                            return;
                        }
                        sb.Append('{');
                        string separator = itemSep;
                        if (indent.HasValue)
                        {
                            string newlineIndent = "\n" + new string(' ', indent.Value * (level + 1));
                            separator = itemSep + newlineIndent;
                            sb.Append(newlineIndent);
                        }
                        List<KeyValuePair<string, JToken>> items = obj.ToList();
                        if (sortKeys)
                            items.Sort((a, b) => PyStr.CompareCodePoints(a.Key, b.Key));
                        bool first = true;
                        foreach (var kv in items)
                        {
                            if (!first)
                                sb.Append(separator);
                            first = false;
                            EncodeString(sb, kv.Key);
                            sb.Append(keySep);
                            Encode(sb, kv.Value, indent, itemSep, keySep, sortKeys, level + 1);
                        }
                        if (indent.HasValue)
                        {
                            sb.Append('\n');
                            sb.Append(' ', indent.Value * level);
                        }
                        sb.Append('}');
                        return;
                    }
            }
        }

        private static string FloatString(double d)
        {
            if (double.IsNaN(d))
                return "NaN";
            if (double.IsPositiveInfinity(d))
                return "Infinity";
            if (double.IsNegativeInfinity(d))
                return "-Infinity";
            return PyFloat.Repr(d);
        }

        private static readonly char[] HexDigits = "0123456789abcdef".ToCharArray();

        /// <summary>Python's json encoder string encoding with ensure_ascii=True.</summary>
        public static void EncodeString(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    default:
                        if (c < ' ' || c > '~')
                        {
                            sb.Append("\\u");
                            sb.Append(HexDigits[(c >> 12) & 0xF]);
                            sb.Append(HexDigits[(c >> 8) & 0xF]);
                            sb.Append(HexDigits[(c >> 4) & 0xF]);
                            sb.Append(HexDigits[c & 0xF]);
                        }
                        else
                        {
                            sb.Append(c);
                        }
                        break;
                }
            }
            sb.Append('"');
        }

        #endregion
    }
}
