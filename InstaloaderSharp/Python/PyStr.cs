using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using System.Text;
using InstaloaderSharp.Json;

namespace InstaloaderSharp.Python
{
    /// <summary>Objects that know their Python str() and repr().</summary>
    public interface IPyObject
    {
        /// <summary>Python's str(self).</summary>
        string PyStr();

        /// <summary>Python's repr(self).</summary>
        string PyRepr();
    }

    /// <summary>Python string semantics: code point based lengths and slicing, str(), repr(), and str methods.</summary>
    public static class PyStr
    {
        #region code points

        /// <summary>Python's len(s), counting code points rather than UTF-16 units.</summary>
        public static int Len(string s)
        {
            int n = 0;
            for (int i = 0; i < s.Length; i++)
            {
                if (char.IsHighSurrogate(s[i]) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
                    i++;
                n++;
            }
            return n;
        }

        /// <summary>Splits a string into its code points.</summary>
        public static List<string> CodePoints(string s)
        {
            var result = new List<string>(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                if (char.IsHighSurrogate(s[i]) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
                {
                    result.Add(s.Substring(i, 2));
                    i++;
                }
                else
                {
                    result.Add(s.Substring(i, 1));
                }
            }
            return result;
        }

        /// <summary>Python's s[start:end] (code point based, with Python's index clamping).</summary>
        public static string Slice(string s, int? start, int? end)
        {
            List<string> cps = CodePoints(s);
            int n = cps.Count;
            int b = start ?? 0;
            int e = end ?? n;
            if (b < 0) b = Math.Max(0, b + n);
            if (e < 0) e = Math.Max(0, e + n);
            b = Math.Min(b, n);
            e = Math.Min(e, n);
            var sb = new StringBuilder();
            for (int i = b; i < e; i++)
                sb.Append(cps[i]);
            return sb.ToString();
        }

        /// <summary>Compares two strings by code points, like Python's string comparison.</summary>
        public static int CompareCodePoints(string a, string b)
        {
            int i = 0, j = 0;
            while (i < a.Length && j < b.Length)
            {
                int ca = CodePointAt(a, i);
                int cb = CodePointAt(b, j);
                if (ca != cb)
                    return ca < cb ? -1 : 1;
                i += ca > 0xFFFF ? 2 : 1;
                j += cb > 0xFFFF ? 2 : 1;
            }
            if (i < a.Length) return 1;
            if (j < b.Length) return -1;
            return 0;
        }

        /// <summary>The code point at the given UTF-16 index.</summary>
        public static int CodePointAt(string s, int index)
        {
            if (char.IsHighSurrogate(s[index]) && index + 1 < s.Length && char.IsLowSurrogate(s[index + 1]))
                return char.ConvertToUtf32(s[index], s[index + 1]);
            return s[index];
        }

        #endregion

        #region str() and repr()

        /// <summary>Python's str(obj) for the values that this program deals with.</summary>
        public static string Str(object obj)
        {
            if (obj == null)
                return "None";
            var s = obj as string;
            if (s != null)
                return s;
            var py = obj as IPyObject;
            if (py != null)
                return py.PyStr();
            var token = obj as JToken;
            if (token != null)
            {
                if (token.Type == JTokenType.String)
                    return (string)token.Value;
                return Repr(token);
            }
            if (obj is bool)
                return (bool)obj ? "True" : "False";
            if (obj is double || obj is float)
                return PyFloat.Repr(Convert.ToDouble(obj, CultureInfo.InvariantCulture));
            if (obj is DateTime || obj is DateTimeOffset)
                return PyDateTime.FromObject(obj).PyStr();
            if (obj is TimeSpan)
                return new PyTimeDelta((TimeSpan)obj).PyStr();
            if (IsInteger(obj))
                return Convert.ToString(obj, CultureInfo.InvariantCulture);
            if (obj is IEnumerable || obj is IDictionary)
                return Repr(obj);
            return obj.ToString();
        }

        /// <summary>Python's repr(obj) for the values that this program deals with.</summary>
        public static string Repr(object obj)
        {
            if (obj == null)
                return "None";
            var s = obj as string;
            if (s != null)
                return ReprString(s);
            var py = obj as IPyObject;
            if (py != null)
                return py.PyRepr();
            var token = obj as JToken;
            if (token != null)
            {
                switch (token.Type)
                {
                    case JTokenType.Null: return "None";
                    case JTokenType.Boolean: return (bool)token.Value ? "True" : "False";
                    case JTokenType.Integer: return Convert.ToString(token.Value, CultureInfo.InvariantCulture);
                    case JTokenType.Float: return PyFloat.Repr((double)token.Value);
                    case JTokenType.String: return ReprString((string)token.Value);
                    case JTokenType.Array:
                        {
                            var parts = new List<string>();
                            foreach (JToken item in (JArray)token)
                                parts.Add(Repr(item));
                            return "[" + string.Join(", ", parts) + "]";
                        }
                    default:
                        {
                            var parts = new List<string>();
                            foreach (var kv in (JObject)token)
                                parts.Add(ReprString(kv.Key) + ": " + Repr(kv.Value));
                            return "{" + string.Join(", ", parts) + "}";
                        }
                }
            }
            if (obj is bool || obj is double || obj is float || IsInteger(obj) || obj is DateTime ||
                obj is DateTimeOffset || obj is TimeSpan)
            {
                if (obj is DateTime || obj is DateTimeOffset)
                    return PyDateTime.FromObject(obj).PyRepr();
                if (obj is TimeSpan)
                    return new PyTimeDelta((TimeSpan)obj).PyRepr();
                return Str(obj);
            }
            var dict = obj as IDictionary;
            if (dict != null)
            {
                var parts = new List<string>();
                foreach (DictionaryEntry kv in dict)
                    parts.Add(Repr(kv.Key) + ": " + Repr(kv.Value));
                return "{" + string.Join(", ", parts) + "}";
            }
            var enumerable = obj as IEnumerable;
            if (enumerable != null)
            {
                var parts = new List<string>();
                foreach (object item in enumerable)
                    parts.Add(Repr(item));
                return "[" + string.Join(", ", parts) + "]";
            }
            return obj.ToString();
        }

        public static bool IsInteger(object obj)
        {
            return obj is long || obj is int || obj is short || obj is byte || obj is sbyte || obj is ushort ||
                   obj is uint || obj is ulong || obj is BigInteger;
        }

        /// <summary>Python's repr() of a str.</summary>
        public static string ReprString(string s)
        {
            char quote = '\'';
            if (s.IndexOf('\'') >= 0 && s.IndexOf('"') < 0)
                quote = '"';
            var sb = new StringBuilder();
            sb.Append(quote);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                int cp = c;
                if (char.IsHighSurrogate(c) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
                {
                    cp = char.ConvertToUtf32(c, s[i + 1]);
                }
                if (c == quote || c == '\\')
                {
                    sb.Append('\\');
                    sb.Append(c);
                }
                else if (c == '\t') sb.Append("\\t");
                else if (c == '\n') sb.Append("\\n");
                else if (c == '\r') sb.Append("\\r");
                else if (cp < 0x20 || cp == 0x7F)
                    sb.AppendFormat(CultureInfo.InvariantCulture, "\\x{0:x2}", cp);
                else if (cp < 0x7F)
                    sb.Append(c);
                else if (IsPrintable(cp))
                {
                    if (cp > 0xFFFF)
                    {
                        sb.Append(c);
                        sb.Append(s[i + 1]);
                    }
                    else
                    {
                        sb.Append(c);
                    }
                }
                else if (cp <= 0xFF)
                    sb.AppendFormat(CultureInfo.InvariantCulture, "\\x{0:x2}", cp);
                else if (cp <= 0xFFFF)
                    sb.AppendFormat(CultureInfo.InvariantCulture, "\\u{0:x4}", cp);
                else
                    sb.AppendFormat(CultureInfo.InvariantCulture, "\\U{0:x8}", cp);
                if (cp > 0xFFFF)
                    i++;
            }
            sb.Append(quote);
            return sb.ToString();
        }

        /// <summary>Python's str.isprintable() for a single code point.</summary>
        public static bool IsPrintable(int cp)
        {
            if (cp == ' ')
                return true;
            if (cp >= 0xD800 && cp <= 0xDFFF)
                return false;
            UnicodeCategory cat = CharUnicodeInfo.GetUnicodeCategory(char.ConvertFromUtf32(cp), 0);
            switch (cat)
            {
                case UnicodeCategory.Control:
                case UnicodeCategory.Format:
                case UnicodeCategory.Surrogate:
                case UnicodeCategory.PrivateUse:
                case UnicodeCategory.OtherNotAssigned:
                case UnicodeCategory.LineSeparator:
                case UnicodeCategory.ParagraphSeparator:
                case UnicodeCategory.SpaceSeparator:
                    return false;
            }
            return true;
        }

        /// <summary>Decodes Python backslash escapes in a (non-raw) string literal body.</summary>
        public static string DecodeEscapes(string s)
        {
            if (s.IndexOf('\\') < 0)
                return s;
            var sb = new StringBuilder();
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c != '\\' || i + 1 >= s.Length)
                {
                    sb.Append(c);
                    continue;
                }
                char e = s[++i];
                switch (e)
                {
                    case '\n': break;
                    case '\\': sb.Append('\\'); break;
                    case '\'': sb.Append('\''); break;
                    case '"': sb.Append('"'); break;
                    case 'a': sb.Append('\a'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'v': sb.Append('\v'); break;
                    case 'x':
                    case 'u':
                    case 'U':
                        {
                            int len = e == 'x' ? 2 : e == 'u' ? 4 : 8;
                            int code;
                            if (s.Length - i - 1 >= len &&
                                int.TryParse(s.Substring(i + 1, len), NumberStyles.AllowHexSpecifier,
                                             CultureInfo.InvariantCulture, out code))
                            {
                                sb.Append(char.ConvertFromUtf32(code));
                                i += len;
                            }
                            else
                            {
                                throw new PyValueError(string.Format("truncated \\{0} escape", e));
                            }
                            break;
                        }
                    case 'N':
                        throw new PyValueError("\\N{...} escapes are not supported");
                    default:
                        if (e >= '0' && e <= '7')
                        {
                            int code = e - '0';
                            int n = 1;
                            while (n < 3 && i + 1 < s.Length && s[i + 1] >= '0' && s[i + 1] <= '7')
                            {
                                code = code * 8 + (s[++i] - '0');
                                n++;
                            }
                            sb.Append((char)code);
                        }
                        else
                        {
                            sb.Append('\\');
                            sb.Append(e);
                        }
                        break;
                }
            }
            return sb.ToString();
        }

        #endregion

        #region str methods

        private static bool IsLineBoundary(char c)
        {
            switch (c)
            {
                case '\n':
                case '\r':
                case '\v':
                case '\f':
                case '\x1c':
                case '\x1d':
                case '\x1e':
                case '\x85':
                case (char)0x2028:
                case (char)0x2029:
                    return true;
            }
            return false;
        }

        /// <summary>Python's str.splitlines(keepends).</summary>
        public static List<string> SplitLines(string s, bool keepEnds = false)
        {
            var result = new List<string>();
            int i = 0;
            int start = 0;
            while (i < s.Length)
            {
                char c = s[i];
                if (IsLineBoundary(c))
                {
                    int eol = i;
                    i++;
                    if (c == '\r' && i < s.Length && s[i] == '\n')
                        i++;
                    result.Add(s.Substring(start, (keepEnds ? i : eol) - start));
                    start = i;
                }
                else
                {
                    i++;
                }
            }
            if (start < s.Length)
                result.Add(s.Substring(start));
            return result;
        }

        /// <summary>Python's whitespace definition as used by str.strip() and str.split().</summary>
        public static bool IsSpace(char c)
        {
            switch (c)
            {
                case ' ':
                case '\t':
                case '\n':
                case '\r':
                case '\v':
                case '\f':
                case '\x1c':
                case '\x1d':
                case '\x1e':
                case '\x1f':
                case '\x85':
                    return true;
            }
            return c > 0x7F && (char.IsWhiteSpace(c) || char.GetUnicodeCategory(c) == UnicodeCategory.SpaceSeparator);
        }

        /// <summary>Python's str.strip() without arguments.</summary>
        public static string Strip(string s)
        {
            return RStrip(LStrip(s));
        }

        public static string LStrip(string s)
        {
            int i = 0;
            while (i < s.Length && IsSpace(s[i]))
                i++;
            return s.Substring(i);
        }

        public static string RStrip(string s)
        {
            int i = s.Length;
            while (i > 0 && IsSpace(s[i - 1]))
                i--;
            return s.Substring(0, i);
        }

        /// <summary>Python's str.strip(chars).</summary>
        public static string Strip(string s, string chars)
        {
            if (chars == null)
                return Strip(s);
            return s.Trim(chars.ToCharArray());
        }

        /// <summary>Python's str.lower().</summary>
        public static string Lower(string s)
        {
            return s.ToLowerInvariant();
        }

        /// <summary>Python's str.upper().</summary>
        public static string Upper(string s)
        {
            return s.ToUpperInvariant();
        }

        #endregion
    }

    /// <summary>Python int() parsing.</summary>
    public static class PyInt
    {
        /// <summary>Python's int(s) for a str argument (base 10).</summary>
        public static BigInteger Parse(string s)
        {
            string t = PyStr.Strip(s);
            string digits = t;
            bool negative = false;
            if (digits.StartsWith("+") || digits.StartsWith("-"))
            {
                negative = digits[0] == '-';
                digits = digits.Substring(1);
            }
            bool valid = digits.Length > 0 && char.IsDigit(digits[0]) && char.IsDigit(digits[digits.Length - 1]);
            var sb = new StringBuilder();
            for (int i = 0; valid && i < digits.Length; i++)
            {
                char c = digits[i];
                if (c == '_')
                {
                    if (digits[i - 1] == '_')
                        valid = false;
                    continue;
                }
                if (!char.IsDigit(c))
                {
                    valid = false;
                    break;
                }
                sb.Append((char)('0' + (int)char.GetNumericValue(c)));
            }
            if (!valid)
                throw new PyValueError("invalid literal for int() with base 10: " + PyStr.Repr(s));
            BigInteger value = BigInteger.Parse(sb.ToString(), CultureInfo.InvariantCulture);
            return negative ? -value : value;
        }

        /// <summary>Python's int(s), as long.</summary>
        public static long ParseLong(string s)
        {
            BigInteger b = Parse(s);
            if (b > long.MaxValue || b < long.MinValue)
                throw new PyOverflowError("Python int too large to convert to C long");
            return (long)b;
        }

        public static bool TryParseLong(string s, out long value)
        {
            try
            {
                value = ParseLong(s);
                return true;
            }
            catch (PyException)
            {
                value = 0;
                return false;
            }
        }
    }
}
