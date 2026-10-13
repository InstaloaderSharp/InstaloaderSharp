using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Text;

namespace InstaloaderSharp.Python
{
    /// <summary>A parsed element of a format string, like the tuples of string.Formatter.parse().</summary>
    public sealed class FormatElement
    {
        public string LiteralText;

        /// <summary>The field name, or null if this element has no replacement field.</summary>
        public string FieldName;

        public string FormatSpec;

        /// <summary>The conversion character, or null.</summary>
        public char? Conversion;
    }

    /// <summary>Python's string.Formatter.</summary>
    public class PyFormatter
    {
        /// <summary>string.Formatter.parse(format_string)</summary>
        public static List<FormatElement> Parse(string s)
        {
            var result = new List<FormatElement>();
            int i = 0;
            var literal = new StringBuilder();
            while (i < s.Length)
            {
                char c = s[i];
                if (c == '{')
                {
                    if (i + 1 < s.Length && s[i + 1] == '{')
                    {
                        literal.Append('{');
                        i += 2;
                        continue;
                    }
                    // replacement field: find the matching '}'
                    int count = 1;
                    int start = i + 1;
                    int j = start;
                    while (j < s.Length)
                    {
                        char d = s[j];
                        if (d == '[')
                        {
                            // skip to ']' (field names may contain braces in brackets)
                            int close = s.IndexOf(']', j);
                            if (close < 0)
                                break;
                            j = close + 1;
                            continue;
                        }
                        if (d == '{')
                            count++;
                        else if (d == '}')
                        {
                            count--;
                            if (count == 0)
                                break;
                        }
                        j++;
                    }
                    if (j >= s.Length)
                        throw new PyValueError("expected '}' before end of string");
                    var element = ParseField(s.Substring(start, j - start));
                    element.LiteralText = literal.ToString();
                    literal.Clear();
                    result.Add(element);
                    i = j + 1;
                    continue;
                }
                if (c == '}')
                {
                    if (i + 1 < s.Length && s[i + 1] == '}')
                    {
                        literal.Append('}');
                        i += 2;
                        continue;
                    }
                    throw new PyValueError("Single '}' encountered in format string");
                }
                literal.Append(c);
                i++;
            }
            if (literal.Length > 0)
                result.Add(new FormatElement { LiteralText = literal.ToString() });
            return result;
        }

        private static FormatElement ParseField(string field)
        {
            int i = 0;
            char c = '\0';
            while (i < field.Length)
            {
                c = field[i++];
                if (c == '{')
                    throw new PyValueError("unexpected '{' in field name");
                if (c == '[')
                {
                    while (i < field.Length && field[i] != ']')
                        i++;
                    c = '\0';
                    continue;
                }
                if (c == ':' || c == '!')
                    break;
                c = '\0';
            }
            var element = new FormatElement { FormatSpec = "" };
            if (c == ':' || c == '!')
            {
                element.FieldName = field.Substring(0, i - 1);
                if (c == '!')
                {
                    if (i >= field.Length)
                        throw new PyValueError("end of string while looking for conversion specifier");
                    element.Conversion = field[i++];
                    if (i < field.Length)
                    {
                        if (field[i] != ':')
                            throw new PyValueError("expected ':' after conversion specifier");
                        i++;
                    }
                }
                element.FormatSpec = field.Substring(Math.Min(i, field.Length));
            }
            else
            {
                element.FieldName = field;
            }
            return element;
        }

        /// <summary>Splits a field name into its first part and the attribute/index accesses.</summary>
        public static void SplitFieldName(string fieldName, out object first, out List<KeyValuePair<bool, object>> rest)
        {
            int i = 0;
            while (i < fieldName.Length && fieldName[i] != '.' && fieldName[i] != '[')
                i++;
            string firstStr = fieldName.Substring(0, i);
            long index;
            if (firstStr.Length > 0 && firstStr.All(char.IsDigit) &&
                long.TryParse(firstStr, NumberStyles.None, CultureInfo.InvariantCulture, out index))
                first = index;
            else
                first = firstStr;
            rest = new List<KeyValuePair<bool, object>>();
            while (i < fieldName.Length)
            {
                char c = fieldName[i++];
                if (c == '.')
                {
                    int start = i;
                    while (i < fieldName.Length && fieldName[i] != '.' && fieldName[i] != '[')
                        i++;
                    if (i == start)
                        throw new PyValueError("Empty attribute in format string");
                    rest.Add(new KeyValuePair<bool, object>(true, fieldName.Substring(start, i - start)));
                }
                else if (c == '[')
                {
                    int close = fieldName.IndexOf(']', i);
                    if (close < 0)
                        throw new PyValueError("Missing ']' in format string");
                    string key = fieldName.Substring(i, close - i);
                    if (key.Length == 0)
                        throw new PyValueError("Empty attribute in format string");
                    object k = key;
                    long n;
                    if (key.All(char.IsDigit) && long.TryParse(key, NumberStyles.None, CultureInfo.InvariantCulture, out n))
                        k = n;
                    rest.Add(new KeyValuePair<bool, object>(false, k));
                    i = close + 1;
                    if (i < fieldName.Length && fieldName[i] != '.' && fieldName[i] != '[')
                        throw new PyValueError("Only '.' or '[' may follow ']' in format field specifier");
                }
            }
        }

        /// <summary>Python's str.format(*args, **kwargs).</summary>
        public string Format(string formatString, Dictionary<string, object> kwargs)
        {
            return VFormat(formatString, new List<object>(), kwargs);
        }

        public string VFormat(string formatString, List<object> args, Dictionary<string, object> kwargs)
        {
            int autoArgIndex = 0;
            return VFormat(formatString, args, kwargs, 2, ref autoArgIndex);
        }

        private string VFormat(string formatString, List<object> args, Dictionary<string, object> kwargs, int recursionDepth,
                               ref int autoArgIndex)
        {
            if (recursionDepth < 0)
                throw new PyValueError("Max string recursion exceeded");
            var result = new StringBuilder();
            foreach (FormatElement element in Parse(formatString))
            {
                if (!string.IsNullOrEmpty(element.LiteralText))
                    result.Append(element.LiteralText);
                if (element.FieldName == null)
                    continue;
                string fieldName = element.FieldName;
                if (fieldName.Length == 0)
                {
                    if (autoArgIndex < 0)
                        throw new PyValueError("cannot switch from manual field specification to automatic field numbering");
                    fieldName = autoArgIndex.ToString(CultureInfo.InvariantCulture);
                    autoArgIndex++;
                }
                else if (fieldName.All(char.IsDigit))
                {
                    if (autoArgIndex > 0)
                        throw new PyValueError("cannot switch from manual field specification to automatic field numbering");
                    autoArgIndex = -1;
                }
                object obj = GetField(fieldName, args, kwargs);
                obj = ConvertField(obj, element.Conversion);
                string spec = VFormat(element.FormatSpec ?? "", args, kwargs, recursionDepth - 1, ref autoArgIndex);
                result.Append(FormatField(obj, spec));
            }
            return result.ToString();
        }

        public virtual object GetField(string fieldName, List<object> args, Dictionary<string, object> kwargs)
        {
            object first;
            List<KeyValuePair<bool, object>> rest;
            SplitFieldName(fieldName, out first, out rest);
            object obj = GetValue(first, args, kwargs);
            foreach (var access in rest)
            {
                if (access.Key)
                    obj = PyRuntime.GetAttr(obj, (string)access.Value);
                else
                    obj = PyRuntime.GetItem(obj, access.Value);
            }
            return obj;
        }

        /// <summary>string.Formatter.get_value: positional (long key) or keyword (string key) argument.</summary>
        public virtual object GetValue(object key, List<object> args, Dictionary<string, object> kwargs)
        {
            if (key is long)
            {
                long index = (long)key;
                if (index >= args.Count)
                    throw new PyIndexError(string.Format("Replacement index {0} out of range for positional args tuple", index));
                return args[(int)index];
            }
            object value;
            if (kwargs.TryGetValue((string)key, out value))
                return value;
            throw new PyKeyError(key);
        }

        public virtual object ConvertField(object value, char? conversion)
        {
            if (conversion == null)
                return value;
            switch (conversion.Value)
            {
                case 's': return PyStr.Str(value);
                case 'r': return PyStr.Repr(value);
                case 'a':
                    {
                        string r = PyStr.Repr(value);
                        var sb = new StringBuilder();
                        foreach (string cp in PyStr.CodePoints(r))
                        {
                            int code = char.ConvertToUtf32(cp, 0);
                            if (code < 0x80) sb.Append(cp);
                            else if (code <= 0xFF) sb.AppendFormat(CultureInfo.InvariantCulture, "\\x{0:x2}", code);
                            else if (code <= 0xFFFF) sb.AppendFormat(CultureInfo.InvariantCulture, "\\u{0:x4}", code);
                            else sb.AppendFormat(CultureInfo.InvariantCulture, "\\U{0:x8}", code);
                        }
                        return sb.ToString();
                    }
            }
            throw new PyValueError(string.Format("Unknown conversion specifier {0}", conversion.Value));
        }

        public virtual string FormatField(object value, string formatSpec)
        {
            return PyFormat.Format(value, formatSpec);
        }

        /// <summary>Whether the format string references the given key (instaloader.format_string_contains_key).</summary>
        public static bool FormatStringContainsKey(string formatString, string key)
        {
            foreach (FormatElement element in Parse(formatString))
            {
                string fieldName = element.FieldName;
                if (!string.IsNullOrEmpty(fieldName) && (fieldName == key || fieldName.StartsWith(key + ".")))
                    return true;
            }
            return false;
        }
    }

    /// <summary>Python's builtin format(value, spec).</summary>
    public static class PyFormat
    {
        private sealed class Spec
        {
            public char Fill = ' ';
            public char? Align;
            public char Sign = '-';
            public bool SignGiven;
            public bool NoNegZero;
            public bool Alternate;
            public int? Width;
            public char? Grouping;
            public int? Precision;
            public char? Type;
        }

        private static Spec ParseSpec(string spec, char defaultAlign)
        {
            var s = new Spec();
            int i = 0;
            if (spec.Length >= 2 && "<>=^".IndexOf(spec[1]) >= 0)
            {
                s.Fill = spec[0];
                s.Align = spec[1];
                i = 2;
            }
            else if (spec.Length >= 1 && "<>=^".IndexOf(spec[0]) >= 0)
            {
                s.Align = spec[0];
                i = 1;
            }
            if (i < spec.Length && "+- ".IndexOf(spec[i]) >= 0)
            {
                s.Sign = spec[i++];
                s.SignGiven = true;
            }
            if (i < spec.Length && spec[i] == 'z')
            {
                s.NoNegZero = true;
                i++;
            }
            if (i < spec.Length && spec[i] == '#')
            {
                s.Alternate = true;
                i++;
            }
            if (i < spec.Length && spec[i] == '0')
            {
                if (!s.Align.HasValue)
                {
                    s.Fill = '0';
                    s.Align = '=';
                }
                i++;
            }
            int start = i;
            while (i < spec.Length && char.IsDigit(spec[i]))
                i++;
            if (i > start)
                s.Width = int.Parse(spec.Substring(start, i - start), CultureInfo.InvariantCulture);
            if (i < spec.Length && (spec[i] == ',' || spec[i] == '_'))
                s.Grouping = spec[i++];
            if (i < spec.Length && spec[i] == '.')
            {
                i++;
                start = i;
                while (i < spec.Length && char.IsDigit(spec[i]))
                    i++;
                if (i == start)
                    throw new PyValueError("Format specifier missing precision");
                s.Precision = int.Parse(spec.Substring(start, i - start), CultureInfo.InvariantCulture);
            }
            if (i < spec.Length)
                s.Type = spec[i++];
            if (i < spec.Length)
                throw new PyValueError("Invalid format specifier '" + spec + "' for object");
            if (!s.Align.HasValue)
                s.Align = defaultAlign;
            return s;
        }

        private static string Pad(string body, string signPrefix, Spec s)
        {
            int len = PyStr.Len(body) + PyStr.Len(signPrefix);
            int width = s.Width ?? 0;
            if (len >= width)
                return signPrefix + body;
            int pad = width - len;
            string fill = new string(s.Fill, 1);
            switch (s.Align)
            {
                case '<':
                    return signPrefix + body + Repeat(fill, pad);
                case '>':
                    return Repeat(fill, pad) + signPrefix + body;
                case '^':
                    {
                        int left = pad / 2;
                        return Repeat(fill, left) + signPrefix + body + Repeat(fill, pad - left);
                    }
                default: // '='
                    return signPrefix + Repeat(fill, pad) + body;
            }
        }

        private static string Repeat(string s, int n)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < n; i++)
                sb.Append(s);
            return sb.ToString();
        }

        /// <summary>format(value, spec)</summary>
        public static string Format(object value, string spec)
        {
            spec = spec ?? "";
            var dt = value as PyDateTime;
            if (dt != null)
                return spec.Length > 0 ? dt.Strftime(spec) : dt.PyStr();
            var str = value as string;
            if (str != null)
                return FormatString(str, spec);
            if (value is bool)
            {
                if (spec.Length == 0)
                    return (bool)value ? "True" : "False";
                return FormatInt((bool)value ? BigInteger.One : BigInteger.Zero, spec);
            }
            if (value is long || value is BigInteger)
                return FormatInt(PyRuntime.ToBig(value), spec);
            if (value is double)
                return FormatFloat((double)value, spec);
            if (spec.Length == 0)
                return PyStr.Str(value);
            throw new PyTypeError(string.Format("unsupported format string passed to {0}.__format__", PyRuntime.TypeName(value)));
        }

        public static string FormatString(string value, string specText)
        {
            if (specText.Length == 0)
                return value;
            Spec s = ParseSpec(specText, '<');
            if (s.Type.HasValue && s.Type != 's')
                throw new PyValueError(string.Format("Unknown format code '{0}' for object of type 'str'", s.Type));
            if (s.SignGiven)
                throw new PyValueError("Sign not allowed in string format specifier");
            if (s.Alternate)
                throw new PyValueError("Alternate form (#) not allowed in string format specifier");
            if (s.Align == '=')
                throw new PyValueError("'=' alignment not allowed in string format specifier");
            if (s.Grouping.HasValue)
                throw new PyValueError("Cannot specify ',' with 's'.");
            string body = s.Precision.HasValue ? PyStr.Slice(value, null, s.Precision.Value) : value;
            return Pad(body, "", s);
        }

        private static string Group(string digits, char separator, int interval)
        {
            var sb = new StringBuilder();
            int count = 0;
            for (int i = digits.Length - 1; i >= 0; i--)
            {
                sb.Insert(0, digits[i]);
                count++;
                if (count % interval == 0 && i > 0)
                    sb.Insert(0, separator);
            }
            return sb.ToString();
        }

        private static string SignPrefix(bool negative, char sign)
        {
            if (negative)
                return "-";
            if (sign == '+')
                return "+";
            if (sign == ' ')
                return " ";
            return "";
        }

        public static string FormatInt(BigInteger value, string specText)
        {
            if (specText.Length == 0)
                return value.ToString(CultureInfo.InvariantCulture);
            Spec s = ParseSpec(specText, '>');
            char type = s.Type ?? 'd';
            if ("eEfFgG%".IndexOf(type) >= 0)
                return FormatFloat((double)value, specText);
            if (s.Precision.HasValue)
                throw new PyValueError("Precision not allowed in integer format specifier");
            bool negative = value.Sign < 0;
            BigInteger abs = BigInteger.Abs(value);
            string digits;
            string prefix = "";
            switch (type)
            {
                case 'd':
                case 'n':
                    digits = abs.ToString(CultureInfo.InvariantCulture);
                    if (s.Grouping.HasValue)
                        digits = Group(digits, s.Grouping.Value, 3);
                    break;
                case 'b':
                    digits = ToBase(abs, 2);
                    if (s.Alternate) prefix = "0b";
                    if (s.Grouping == '_') digits = Group(digits, '_', 4);
                    break;
                case 'o':
                    digits = ToBase(abs, 8);
                    if (s.Alternate) prefix = "0o";
                    if (s.Grouping == '_') digits = Group(digits, '_', 4);
                    break;
                case 'x':
                case 'X':
                    digits = ToBase(abs, 16);
                    if (type == 'X') digits = digits.ToUpperInvariant();
                    if (s.Alternate) prefix = type == 'X' ? "0X" : "0x";
                    if (s.Grouping == '_') digits = Group(digits, '_', 4);
                    break;
                case 'c':
                    if (value < 0 || value > 0x10FFFF)
                        throw new PyOverflowError("%c arg not in range(0x110000)");
                    return Pad(char.ConvertFromUtf32((int)value), "", s);
                default:
                    throw new PyValueError(string.Format("Unknown format code '{0}' for object of type 'int'", type));
            }
            string signPrefix = SignPrefix(negative, s.Sign) + prefix;
            if (s.Align == '=' && s.Fill == '0' && s.Grouping.HasValue && s.Width.HasValue)
            {
                // zero padding is also grouped
                int needed = s.Width.Value - signPrefix.Length;
                string raw = digits.Replace(s.Grouping.Value.ToString(), "");
                int interval = type == 'd' || type == 'n' ? 3 : 4;
                while (digits.Length < needed)
                {
                    raw = "0" + raw;
                    digits = Group(raw, s.Grouping.Value, interval);
                }
                return signPrefix + digits;
            }
            return Pad(digits, signPrefix, s);
        }

        private static string ToBase(BigInteger value, int radix)
        {
            if (value.IsZero)
                return "0";
            const string chars = "0123456789abcdef";
            var sb = new StringBuilder();
            while (value > 0)
            {
                sb.Insert(0, chars[(int)(value % radix)]);
                value /= radix;
            }
            return sb.ToString();
        }

        #region exact float formatting

        /// <summary>The exact decimal value of a finite non-negative double: digits * 10^-scale.</summary>
        private static void ExactDecimal(double value, out BigInteger digits, out int scale)
        {
            long bits = BitConverter.DoubleToInt64Bits(value);
            int exponent = (int)((bits >> 52) & 0x7FF);
            long mantissa = bits & 0xFFFFFFFFFFFFFL;
            if (exponent == 0)
                exponent = 1;
            else
                mantissa |= 1L << 52;
            exponent -= 1075;
            if (exponent >= 0)
            {
                digits = new BigInteger(mantissa) << exponent;
                scale = 0;
            }
            else
            {
                digits = new BigInteger(mantissa) * BigInteger.Pow(5, -exponent);
                scale = -exponent;
            }
        }

        /// <summary>Rounds digits*10^-scale to the given number of decimals with round-half-even.</summary>
        private static BigInteger RoundToScale(BigInteger digits, int scale, int newScale)
        {
            if (newScale >= scale)
                return digits * BigInteger.Pow(10, newScale - scale);
            BigInteger divisor = BigInteger.Pow(10, scale - newScale);
            BigInteger remainder;
            BigInteger q = BigInteger.DivRem(digits, divisor, out remainder);
            BigInteger twice = remainder * 2;
            int cmp = twice.CompareTo(divisor);
            if (cmp > 0 || (cmp == 0 && !q.IsEven))
                q += 1;
            return q;
        }

        private static int DigitCount(BigInteger v)
        {
            return v.IsZero ? 1 : v.ToString(CultureInfo.InvariantCulture).Length;
        }

        /// <summary>Fixed-point notation with the given number of decimals ('f').</summary>
        private static string Fixed(double abs, int precision, bool alternate)
        {
            BigInteger digits;
            int scale;
            ExactDecimal(abs, out digits, out scale);
            BigInteger rounded = RoundToScale(digits, scale, precision);
            string s = rounded.ToString(CultureInfo.InvariantCulture).PadLeft(precision + 1, '0');
            string intPart = s.Substring(0, s.Length - precision);
            string frac = s.Substring(s.Length - precision);
            if (precision == 0)
                return intPart + (alternate ? "." : "");
            return intPart + "." + frac;
        }

        /// <summary>Rounds to the given number of significant digits; returns the digits and decimal exponent.</summary>
        private static string Significant(double abs, int significant, out int exponent)
        {
            if (abs == 0.0)
            {
                exponent = 0;
                return new string('0', significant);
            }
            BigInteger digits;
            int scale;
            ExactDecimal(abs, out digits, out scale);
            int totalDigits = DigitCount(digits);
            // value = digits * 10^-scale; the first digit has exponent totalDigits - 1 - scale
            exponent = totalDigits - 1 - scale;
            int newScale = significant - 1 - exponent;
            BigInteger rounded = RoundToScale(digits, scale, newScale);
            if (DigitCount(rounded) > significant)
            {
                // rounding carried over, e.g. 9.99 -> 10.0
                exponent++;
                rounded = RoundToScale(rounded, newScale, newScale - 1);
            }
            return rounded.ToString(CultureInfo.InvariantCulture).PadLeft(significant, '0');
        }

        private static string Exponential(double abs, int precision, char type, bool alternate)
        {
            int exponent;
            string digits = Significant(abs, precision + 1, out exponent);
            var sb = new StringBuilder();
            sb.Append(digits[0]);
            if (precision > 0 || alternate)
                sb.Append('.');
            sb.Append(digits, 1, digits.Length - 1);
            sb.Append(type == 'E' ? 'E' : 'e');
            sb.Append(exponent < 0 ? '-' : '+');
            sb.Append(Math.Abs(exponent).ToString("00", CultureInfo.InvariantCulture));
            return sb.ToString();
        }

        private static string General(double abs, int precision, char type, bool alternate, bool reprStyle)
        {
            if (precision == 0)
                precision = 1;
            int exponent;
            string digits = Significant(abs, precision, out exponent);
            // CPython: decpt <= -4 || decpt > (add_dot_0 ? precision - 1 : precision), with decpt = exponent + 1
            bool useExponent = exponent < -4 || exponent + 1 > (reprStyle ? precision - 1 : precision);
            string result;
            if (useExponent)
            {
                string mantissa = digits.Substring(1);
                if (!alternate)
                    mantissa = mantissa.TrimEnd('0');
                result = digits[0] + (mantissa.Length > 0 || alternate ? "." + mantissa : "") +
                         (type == 'G' ? "E" : "e") + (exponent < 0 ? "-" : "+") +
                         Math.Abs(exponent).ToString("00", CultureInfo.InvariantCulture);
            }
            else
            {
                string intPart, frac;
                if (exponent >= 0)
                {
                    string d = digits.PadRight(exponent + 1, '0');
                    intPart = d.Substring(0, exponent + 1);
                    frac = d.Substring(exponent + 1);
                }
                else
                {
                    intPart = "0";
                    frac = new string('0', -exponent - 1) + digits;
                }
                if (!alternate)
                    frac = frac.TrimEnd('0');
                if (reprStyle && frac.Length == 0)
                    frac = "0";
                result = intPart + (frac.Length > 0 || alternate ? "." + frac : "");
            }
            return result;
        }

        #endregion

        public static string FormatFloat(double value, string specText)
        {
            if (specText.Length == 0)
                return PyFloat.Repr(value);
            Spec s = ParseSpec(specText, '>');
            char type = s.Type ?? '\0';
            bool negative = value < 0 || (value == 0.0 && 1.0 / value < 0);
            double abs = Math.Abs(value);
            string body;
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                body = double.IsNaN(value) ? "nan" : "inf";
                if ("EFG".IndexOf(type) >= 0)
                    body = body.ToUpperInvariant();
                if (type == '%')
                    body += "%";
                negative = !double.IsNaN(value) && value < 0;
            }
            else
            {
                switch (type)
                {
                    case 'f':
                    case 'F':
                        body = Fixed(abs, s.Precision ?? 6, s.Alternate);
                        break;
                    case 'e':
                    case 'E':
                        body = Exponential(abs, s.Precision ?? 6, type, s.Alternate);
                        break;
                    case 'g':
                    case 'G':
                    case 'n':
                        body = General(abs, s.Precision ?? 6, type == 'n' ? 'g' : type, s.Alternate, false);
                        break;
                    case '%':
                        {
                            BigInteger digits;
                            int scale;
                            ExactDecimal(abs, out digits, out scale);
                            body = FixedFromExact(digits * 100, scale, s.Precision ?? 6, s.Alternate) + "%";
                            break;
                        }
                    case '\0':
                        if (s.Precision.HasValue)
                            body = General(abs, s.Precision.Value, 'g', s.Alternate, true);
                        else
                            body = PyFloat.Repr(abs);
                        break;
                    default:
                        throw new PyValueError(string.Format("Unknown format code '{0}' for object of type 'float'", type));
                }
                if (s.NoNegZero && negative && body.Trim('0', '.', '%', 'e', 'E', '+', '-').Length == 0)
                    negative = false;
            }
            if (s.Grouping.HasValue)
            {
                int dot = body.IndexOfAny(new[] { '.', 'e', 'E', '%' });
                string intPart = dot >= 0 ? body.Substring(0, dot) : body;
                string rest = dot >= 0 ? body.Substring(dot) : "";
                if (intPart.All(char.IsDigit))
                    body = Group(intPart, s.Grouping.Value, 3) + rest;
            }
            return Pad(body, SignPrefix(negative, s.Sign), s);
        }

        private static string FixedFromExact(BigInteger digits, int scale, int precision, bool alternate)
        {
            BigInteger rounded = RoundToScale(digits, scale, precision);
            string str = rounded.ToString(CultureInfo.InvariantCulture).PadLeft(precision + 1, '0');
            string intPart = str.Substring(0, str.Length - precision);
            string frac = str.Substring(str.Length - precision);
            if (precision == 0)
                return intPart + (alternate ? "." : "");
            return intPart + "." + frac;
        }
    }
}
