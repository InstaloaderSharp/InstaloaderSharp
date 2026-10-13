using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;

namespace InstaloaderSharp.Python
{
    /// <summary>Attributes and methods of Python's builtin types and of datetime.</summary>
    internal static class PyBuiltins
    {
        private static object Arg(List<object> args, Dictionary<string, object> kwargs, int index, string name, object defaultValue,
                                  bool required = false)
        {
            if (index < args.Count)
                return args[index];
            object value;
            if (kwargs.TryGetValue(name, out value))
                return value;
            if (required)
                throw new PyTypeError(string.Format("missing required argument '{0}' (pos {1})", name, index + 1));
            return defaultValue;
        }

        private static PyCallable Method(string name, Func<List<object>, Dictionary<string, object>, object> impl)
        {
            return new PyCallable(name, impl);
        }

        private static string StrArg(object v, string method)
        {
            var s = v as string;
            if (s == null)
                throw new PyTypeError(string.Format("{0}() argument must be str, not {1}", method, PyRuntime.TypeName(v)));
            return s;
        }

        private static List<string> PrefixList(object v, string method)
        {
            var tuple = v as PyTuple;
            if (tuple != null)
                return tuple.Items.Select(x => StrArg(x, method)).ToList();
            return new List<string> { StrArg(v, method) };
        }

        public static bool TryGetAttr(object obj, string name, out object result)
        {
            result = null;
            var s = obj as string;
            if (s != null)
                return TryGetStrAttr(s, name, out result);
            var list = obj as List<object>;
            if (list != null)
            {
                switch (name)
                {
                    case "count":
                        result = Method(name, (a, k) => (long)list.Count(x => PyRuntime.Eq(x, Arg(a, k, 0, "value", null, true))));
                        return true;
                    case "index":
                        result = Method(name, (a, k) =>
                        {
                            object value = Arg(a, k, 0, "value", null, true);
                            for (int i = 0; i < list.Count; i++)
                                if (PyRuntime.Eq(list[i], value))
                                    return (long)i;
                            throw new PyValueError(string.Format("{0} is not in list", PyStr.Repr(value)));
                        });
                        return true;
                    case "copy":
                        result = Method(name, (a, k) => list.ToList());
                        return true;
                }
                return false;
            }
            var tuple = obj as PyTuple;
            if (tuple != null)
            {
                if (name == "count")
                {
                    result = Method(name, (a, k) => (long)tuple.Items.Count(x => PyRuntime.Eq(x, Arg(a, k, 0, "value", null, true))));
                    return true;
                }
                if (name == "index")
                {
                    result = Method(name, (a, k) =>
                    {
                        object value = Arg(a, k, 0, "value", null, true);
                        for (int i = 0; i < tuple.Items.Count; i++)
                            if (PyRuntime.Eq(tuple.Items[i], value))
                                return (long)i;
                        throw new PyValueError("tuple.index(x): x not in tuple");
                    });
                    return true;
                }
                return false;
            }
            var dict = obj as PyDict;
            if (dict != null)
            {
                switch (name)
                {
                    case "get":
                        result = Method(name, (a, k) =>
                        {
                            object value;
                            return dict.TryGet(Arg(a, k, 0, "key", null, true), out value) ? value : Arg(a, k, 1, "default", null);
                        });
                        return true;
                    case "keys":
                        result = Method(name, (a, k) => dict.Items.Select(kv => kv.Key).ToList());
                        return true;
                    case "values":
                        result = Method(name, (a, k) => dict.Items.Select(kv => kv.Value).ToList());
                        return true;
                    case "items":
                        result = Method(name, (a, k) => dict.Items.Select(kv => (object)new PyTuple(new[] { kv.Key, kv.Value })).ToList());
                        return true;
                }
                return false;
            }
            var dt = obj as PyDateTime;
            if (dt != null)
                return TryGetDateTimeAttr(dt, name, out result);
            var td = obj as PyTimeDelta;
            if (td != null)
            {
                switch (name)
                {
                    case "days": result = td.Days; return true;
                    case "seconds": result = td.Seconds; return true;
                    case "microseconds": result = td.Microseconds; return true;
                    case "total_seconds": result = Method(name, (a, k) => td.TotalSeconds()); return true;
                }
                return false;
            }
            if (obj is PyDateTimeType)
                return TryGetDateTimeTypeAttr(name, out result);
            if (PyRuntime.IsInt(obj) && !(obj is bool))
            {
                switch (name)
                {
                    case "real":
                    case "numerator":
                        result = obj;
                        return true;
                    case "imag":
                        result = 0L;
                        return true;
                    case "denominator":
                        result = 1L;
                        return true;
                    case "bit_length":
                        result = Method(name, (a, k) =>
                        {
                            BigInteger v = BigInteger.Abs(PyRuntime.ToBig(obj));
                            long bits = 0;
                            while (v > 0)
                            {
                                v >>= 1;
                                bits++;
                            }
                            return bits;
                        });
                        return true;
                }
                return false;
            }
            if (obj is double)
            {
                double d = (double)obj;
                switch (name)
                {
                    case "real": result = d; return true;
                    case "imag": result = 0.0; return true;
                    case "is_integer":
                        result = Method(name, (a, k) => !double.IsInfinity(d) && !double.IsNaN(d) && Math.Floor(d) == d);
                        return true;
                }
                return false;
            }
            return false;
        }

        private static List<string> Split(string s, string sep, int maxsplit)
        {
            var result = new List<string>();
            if (sep == null)
            {
                int i = 0;
                int splits = 0;
                while (true)
                {
                    while (i < s.Length && PyStr.IsSpace(s[i]))
                        i++;
                    if (i >= s.Length)
                        break;
                    if (maxsplit >= 0 && splits >= maxsplit)
                    {
                        result.Add(PyStr.RStrip(s.Substring(i)));
                        break;
                    }
                    int start = i;
                    while (i < s.Length && !PyStr.IsSpace(s[i]))
                        i++;
                    result.Add(s.Substring(start, i - start));
                    splits++;
                }
                return result;
            }
            if (sep.Length == 0)
                throw new PyValueError("empty separator");
            int pos = 0;
            int count = 0;
            while (true)
            {
                int idx = maxsplit >= 0 && count >= maxsplit ? -1 : s.IndexOf(sep, pos, StringComparison.Ordinal);
                if (idx < 0)
                {
                    result.Add(s.Substring(pos));
                    break;
                }
                result.Add(s.Substring(pos, idx - pos));
                pos = idx + sep.Length;
                count++;
            }
            return result;
        }

        private static bool TryGetStrAttr(string s, string name, out object result)
        {
            result = null;
            switch (name)
            {
                case "lower": result = Method(name, (a, k) => PyStr.Lower(s)); return true;
                case "upper": result = Method(name, (a, k) => PyStr.Upper(s)); return true;
                case "casefold": result = Method(name, (a, k) => PyStr.Lower(s)); return true;
                case "swapcase":
                    result = Method(name, (a, k) => new string(s.Select(c => char.IsUpper(c) ? char.ToLowerInvariant(c) : char.ToUpperInvariant(c)).ToArray()));
                    return true;
                case "title":
                    result = Method(name, (a, k) =>
                    {
                        var sb = new StringBuilder();
                        bool prevCased = false;
                        foreach (char c in s)
                        {
                            sb.Append(prevCased ? char.ToLowerInvariant(c) : char.ToUpperInvariant(c));
                            prevCased = char.IsLetter(c);
                        }
                        return sb.ToString();
                    });
                    return true;
                case "capitalize":
                    result = Method(name, (a, k) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s.Substring(1).ToLowerInvariant());
                    return true;
                case "strip":
                case "lstrip":
                case "rstrip":
                    result = Method(name, (a, k) =>
                    {
                        object chars = Arg(a, k, 0, "chars", null);
                        if (chars == null)
                            return name == "strip" ? PyStr.Strip(s) : name == "lstrip" ? PyStr.LStrip(s) : PyStr.RStrip(s);
                        char[] set = StrArg(chars, name).ToCharArray();
                        return name == "strip" ? s.Trim(set) : name == "lstrip" ? s.TrimStart(set) : s.TrimEnd(set);
                    });
                    return true;
                case "startswith":
                case "endswith":
                    result = Method(name, (a, k) =>
                    {
                        List<string> prefixes = PrefixList(Arg(a, k, 0, "prefix", null, true), name);
                        string sub = s;
                        object start = Arg(a, k, 1, "start", null);
                        object end = Arg(a, k, 2, "end", null);
                        if (start != null || end != null)
                            sub = PyStr.Slice(s, start == null ? (int?)null : PyRuntime.ToIndex(start), end == null ? (int?)null : PyRuntime.ToIndex(end));
                        return prefixes.Any(p => name == "startswith"
                                                ? sub.StartsWith(p, StringComparison.Ordinal)
                                                : sub.EndsWith(p, StringComparison.Ordinal));
                    });
                    return true;
                case "split":
                case "rsplit":
                    result = Method(name, (a, k) =>
                    {
                        object sep = Arg(a, k, 0, "sep", null);
                        int maxsplit = PyRuntime.ToIndex(Arg(a, k, 1, "maxsplit", -1L));
                        if (name == "rsplit" && maxsplit >= 0)
                        {
                            // rsplit with limit: split from the right
                            string sepStr = sep == null ? null : StrArg(sep, name);
                            List<string> all = Split(s, sepStr, -1);
                            if (all.Count <= maxsplit + 1)
                                return all.Cast<object>().ToList();
                            int keep = all.Count - maxsplit;
                            string head = string.Join(sepStr ?? " ", all.Take(keep));
                            var r = new List<object> { head };
                            r.AddRange(all.Skip(keep));
                            return r;
                        }
                        return Split(s, sep == null ? null : StrArg(sep, name), maxsplit).Cast<object>().ToList();
                    });
                    return true;
                case "splitlines":
                    result = Method(name, (a, k) => PyStr.SplitLines(s, PyRuntime.Truthy(Arg(a, k, 0, "keepends", false))).Cast<object>().ToList());
                    return true;
                case "replace":
                    result = Method(name, (a, k) =>
                    {
                        string old = StrArg(Arg(a, k, 0, "old", null, true), name);
                        string @new = StrArg(Arg(a, k, 1, "new", null, true), name);
                        int count = PyRuntime.ToIndex(Arg(a, k, 2, "count", -1L));
                        if (count < 0)
                            return old.Length == 0 ? string.Join(@new, PyStr.CodePoints(s).Prepend("").Append("")) : s.Replace(old, @new);
                        var sb = new StringBuilder();
                        int pos = 0;
                        for (int n = 0; n < count; n++)
                        {
                            int idx = s.IndexOf(old, pos, StringComparison.Ordinal);
                            if (idx < 0)
                                break;
                            sb.Append(s, pos, idx - pos).Append(@new);
                            pos = idx + old.Length;
                        }
                        sb.Append(s.Substring(pos));
                        return sb.ToString();
                    });
                    return true;
                case "find":
                case "rfind":
                case "index":
                case "rindex":
                case "count":
                    result = Method(name, (a, k) =>
                    {
                        string sub = StrArg(Arg(a, k, 0, "sub", null, true), name);
                        if (name == "count")
                        {
                            if (sub.Length == 0)
                                return (long)(PyStr.Len(s) + 1);
                            long n = 0;
                            int pos = 0;
                            while ((pos = s.IndexOf(sub, pos, StringComparison.Ordinal)) >= 0)
                            {
                                n++;
                                pos += sub.Length;
                            }
                            return n;
                        }
                        int idx = name.StartsWith("r") ? s.LastIndexOf(sub, StringComparison.Ordinal) : s.IndexOf(sub, StringComparison.Ordinal);
                        if (idx < 0 && name.EndsWith("index"))
                            throw new PyValueError("substring not found");
                        return idx < 0 ? -1L : (long)PyStr.Len(s.Substring(0, idx));
                    });
                    return true;
                case "isdigit":
                case "isdecimal":
                case "isnumeric":
                    result = Method(name, (a, k) => s.Length > 0 && s.All(char.IsDigit));
                    return true;
                case "isalpha":
                    result = Method(name, (a, k) => s.Length > 0 && s.All(char.IsLetter));
                    return true;
                case "isalnum":
                    result = Method(name, (a, k) => s.Length > 0 && s.All(char.IsLetterOrDigit));
                    return true;
                case "isspace":
                    result = Method(name, (a, k) => s.Length > 0 && s.All(PyStr.IsSpace));
                    return true;
                case "islower":
                    result = Method(name, (a, k) => s.Any(char.IsLower) && !s.Any(char.IsUpper));
                    return true;
                case "isupper":
                    result = Method(name, (a, k) => s.Any(char.IsUpper) && !s.Any(char.IsLower));
                    return true;
                case "zfill":
                    result = Method(name, (a, k) =>
                    {
                        int width = PyRuntime.ToIndex(Arg(a, k, 0, "width", null, true));
                        int len = PyStr.Len(s);
                        if (len >= width)
                            return s;
                        string sign = s.Length > 0 && (s[0] == '+' || s[0] == '-') ? s.Substring(0, 1) : "";
                        return sign + new string('0', width - len) + s.Substring(sign.Length);
                    });
                    return true;
                case "center":
                case "ljust":
                case "rjust":
                    result = Method(name, (a, k) =>
                    {
                        int width = PyRuntime.ToIndex(Arg(a, k, 0, "width", null, true));
                        string fill = StrArg(Arg(a, k, 1, "fillchar", " "), name);
                        int pad = width - PyStr.Len(s);
                        if (pad <= 0)
                            return s;
                        if (name == "ljust")
                            return s + string.Concat(Enumerable.Repeat(fill, pad));
                        if (name == "rjust")
                            return string.Concat(Enumerable.Repeat(fill, pad)) + s;
                        int left = pad / 2 + (pad & width & 1);
                        return string.Concat(Enumerable.Repeat(fill, left)) + s + string.Concat(Enumerable.Repeat(fill, pad - left));
                    });
                    return true;
                case "join":
                    result = Method(name, (a, k) =>
                        string.Join(s, PyRuntime.Iterate(Arg(a, k, 0, "iterable", null, true)).Select(x =>
                        {
                            var xs = x as string;
                            if (xs == null)
                                throw new PyTypeError(string.Format("sequence item: expected str instance, {0} found", PyRuntime.TypeName(x)));
                            return xs;
                        })));
                    return true;
                case "partition":
                case "rpartition":
                    result = Method(name, (a, k) =>
                    {
                        string sep = StrArg(Arg(a, k, 0, "sep", null, true), name);
                        int idx = name == "partition" ? s.IndexOf(sep, StringComparison.Ordinal) : s.LastIndexOf(sep, StringComparison.Ordinal);
                        if (idx < 0)
                            return name == "partition"
                                ? new PyTuple(new object[] { s, "", "" })
                                : new PyTuple(new object[] { "", "", s });
                        return new PyTuple(new object[] { s.Substring(0, idx), sep, s.Substring(idx + sep.Length) });
                    });
                    return true;
                case "format":
                    result = Method(name, (a, k) => new PyFormatter().VFormat(s, a, k));
                    return true;
            }
            return false;
        }

        private static bool TryGetDateTimeAttr(PyDateTime dt, string name, out object result)
        {
            result = null;
            switch (name)
            {
                case "year": result = (long)dt.Year; return true;
                case "month": result = (long)dt.Month; return true;
                case "day": result = (long)dt.Day; return true;
                case "hour": result = (long)dt.Hour; return true;
                case "minute": result = (long)dt.Minute; return true;
                case "second": result = (long)dt.Second; return true;
                case "microsecond": result = (long)dt.Microsecond; return true;
                case "tzinfo":
                    result = dt.Offset.HasValue ? new PyTimeZone(dt.Offset.Value, dt.TzName) : null;
                    return true;
                case "weekday": result = Method(name, (a, k) => (long)dt.Weekday()); return true;
                case "isoweekday": result = Method(name, (a, k) => (long)dt.IsoWeekday()); return true;
                case "timestamp": result = Method(name, (a, k) => dt.Timestamp()); return true;
                case "strftime":
                    result = Method(name, (a, k) => dt.Strftime(StrArg(Arg(a, k, 0, "format", null, true), name)));
                    return true;
                case "isoformat":
                    result = Method(name, (a, k) =>
                    {
                        string sep = StrArg(Arg(a, k, 0, "sep", "T"), name);
                        if (sep.Length != 1)
                            throw new PyTypeError("isoformat() argument 1 must be a unicode character");
                        return dt.IsoFormat(sep[0]);
                    });
                    return true;
                case "astimezone":
                    result = Method(name, (a, k) => dt.AsTimeZone());
                    return true;
                case "utcoffset":
                    result = Method(name, (a, k) => dt.Offset.HasValue ? new PyTimeDelta(dt.Offset.Value) : null);
                    return true;
                case "replace":
                    result = Method(name, (a, k) =>
                    {
                        if (a.Count > 0)
                            throw new PyTypeError("replace() takes keyword arguments only in filters");
                        Func<string, int, int> get = (key, current) =>
                        {
                            object v;
                            return k.TryGetValue(key, out v) ? PyRuntime.ToIndex(v) : current;
                        };
                        try
                        {
                            var wall = new DateTime(get("year", dt.Year), get("month", dt.Month), get("day", dt.Day),
                                                    get("hour", dt.Hour), get("minute", dt.Minute), get("second", dt.Second))
                                .AddTicks(get("microsecond", dt.Microsecond) * 10L);
                            TimeSpan? offset = dt.Offset;
                            string tzName = dt.TzName;
                            object tz;
                            if (k.TryGetValue("tzinfo", out tz))
                            {
                                var zone = tz as PyTimeZone;
                                offset = zone != null ? zone.Offset : (TimeSpan?)null;
                                tzName = zone != null ? zone.Name : null;
                            }
                            return new PyDateTime(wall, offset, tzName);
                        }
                        catch (ArgumentOutOfRangeException ex)
                        {
                            throw new PyValueError(ex.Message);
                        }
                    });
                    return true;
            }
            return false;
        }

        private static bool TryGetDateTimeTypeAttr(string name, out object result)
        {
            result = null;
            switch (name)
            {
                case "now":
                    result = Method(name, (a, k) =>
                    {
                        object tz = Arg(a, k, 0, "tz", null);
                        if (tz is PyTimeZone)
                        {
                            var zone = (PyTimeZone)tz;
                            DateTimeOffset now = DateTimeOffset.UtcNow.ToOffset(zone.Offset);
                            return new PyDateTime(now.DateTime, zone.Offset, zone.Name);
                        }
                        return PyDateTime.Now();
                    });
                    return true;
                case "today":
                    result = Method(name, (a, k) => PyDateTime.Now());
                    return true;
                case "utcnow":
                    result = Method(name, (a, k) => PyDateTime.UtcNow());
                    return true;
                case "fromtimestamp":
                    result = Method(name, (a, k) =>
                    {
                        double ts = PyRuntime.ToDouble(Arg(a, k, 0, "timestamp", null, true));
                        object tz = Arg(a, k, 1, "tz", null);
                        if (tz is PyTimeZone)
                        {
                            var zone = (PyTimeZone)tz;
                            PyDateTime utc = PyDateTime.UtcFromTimestamp(ts);
                            return new PyDateTime(utc.Wall.Add(zone.Offset), zone.Offset, zone.Name);
                        }
                        return PyDateTime.FromTimestamp(ts);
                    });
                    return true;
                case "utcfromtimestamp":
                    result = Method(name, (a, k) => PyDateTime.UtcFromTimestamp(PyRuntime.ToDouble(Arg(a, k, 0, "timestamp", null, true))));
                    return true;
                case "fromisoformat":
                    result = Method(name, (a, k) => FromIsoFormat(StrArg(Arg(a, k, 0, "date_string", null, true), name)));
                    return true;
                case "strptime":
                    result = Method(name, (a, k) => Strptime(StrArg(Arg(a, k, 0, "date_string", null, true), name),
                                                             StrArg(Arg(a, k, 1, "format", null, true), name)));
                    return true;
                case "min":
                    result = new PyDateTime(new DateTime(1, 1, 1));
                    return true;
                case "max":
                    result = new PyDateTime(new DateTime(9999, 12, 31, 23, 59, 59).AddTicks(9999990));
                    return true;
            }
            return false;
        }

        /// <summary>datetime(year, month, day, hour=0, minute=0, second=0, microsecond=0, tzinfo=None)</summary>
        public static object DateTimeConstructor(List<object> args, Dictionary<string, object> kwargs)
        {
            string[] names = { "year", "month", "day", "hour", "minute", "second", "microsecond" };
            var values = new int[7];
            for (int i = 0; i < 7; i++)
            {
                object v = Arg(args, kwargs, i, names[i], i < 3 ? null : (object)0L, i < 3);
                if (!PyRuntime.IsInt(v))
                    throw new PyTypeError(string.Format("'{0}' object cannot be interpreted as an integer", PyRuntime.TypeName(v)));
                values[i] = PyRuntime.ToIndex(v);
            }
            object tz = Arg(args, kwargs, 7, "tzinfo", null);
            try
            {
                if (values[0] < 1 || values[0] > 9999)
                    throw new PyValueError(string.Format("year {0} is out of range", values[0]));
                if (values[1] < 1 || values[1] > 12)
                    throw new PyValueError("month must be in 1..12");
                if (values[2] < 1 || values[2] > DateTime.DaysInMonth(values[0], values[1]))
                    throw new PyValueError("day is out of range for month");
                if (values[3] < 0 || values[3] > 23)
                    throw new PyValueError("hour must be in 0..23");
                if (values[4] < 0 || values[4] > 59)
                    throw new PyValueError("minute must be in 0..59");
                if (values[5] < 0 || values[5] > 59)
                    throw new PyValueError("second must be in 0..59");
                if (values[6] < 0 || values[6] > 999999)
                    throw new PyValueError("microsecond must be in 0..999999");
                var wall = new DateTime(values[0], values[1], values[2], values[3], values[4], values[5]).AddTicks(values[6] * 10L);
                var zone = tz as PyTimeZone;
                return new PyDateTime(wall, zone != null ? zone.Offset : (TimeSpan?)null, zone != null ? zone.Name : null);
            }
            catch (ArgumentOutOfRangeException ex)
            {
                throw new PyValueError(ex.Message);
            }
        }

        private static PyDateTime FromIsoFormat(string s)
        {
            Match m = Regex.Match(s, @"^(\d{4})-(\d{2})-(\d{2})(?:[T ](\d{2})(?::?(\d{2})(?::?(\d{2})(?:[.,](\d{1,6}))?)?)?)?(Z|[+-]\d{2}:?\d{2}(?::?\d{2})?)?$");
            if (!m.Success)
                throw new PyValueError(string.Format("Invalid isoformat string: {0}", PyStr.Repr(s)));
            Func<int, int> g = i => m.Groups[i].Success ? int.Parse(m.Groups[i].Value, CultureInfo.InvariantCulture) : 0;
            int micro = m.Groups[7].Success ? int.Parse(m.Groups[7].Value.PadRight(6, '0'), CultureInfo.InvariantCulture) : 0;
            var args = new List<object> { (long)g(1), (long)g(2), (long)g(3), (long)g(4), (long)g(5), (long)g(6), (long)micro };
            var dt = (PyDateTime)DateTimeConstructor(args, new Dictionary<string, object>());
            if (m.Groups[8].Success)
            {
                string z = m.Groups[8].Value;
                TimeSpan offset = TimeSpan.Zero;
                if (z != "Z")
                {
                    string digits = z.Substring(1).Replace(":", "");
                    offset = new TimeSpan(int.Parse(digits.Substring(0, 2), CultureInfo.InvariantCulture),
                                          int.Parse(digits.Substring(2, 2), CultureInfo.InvariantCulture),
                                          digits.Length >= 6 ? int.Parse(digits.Substring(4, 2), CultureInfo.InvariantCulture) : 0);
                    if (z[0] == '-')
                        offset = offset.Negate();
                }
                return new PyDateTime(dt.Wall, offset);
            }
            return dt;
        }

        private static readonly string[] MonthNames =
            { "january", "february", "march", "april", "may", "june", "july", "august", "september", "october", "november", "december" };
        private static readonly string[] DayNames =
            { "monday", "tuesday", "wednesday", "thursday", "friday", "saturday", "sunday" };

        /// <summary>datetime.strptime for the common directives.</summary>
        public static PyDateTime Strptime(string s, string format)
        {
            var regex = new StringBuilder("^");
            var groups = new List<char>();
            for (int i = 0; i < format.Length; i++)
            {
                char c = format[i];
                if (c != '%')
                {
                    if (char.IsWhiteSpace(c))
                        regex.Append(@"\s+");
                    else
                        regex.Append(Regex.Escape(c.ToString()));
                    continue;
                }
                if (++i >= format.Length)
                    throw new PyValueError("stray % in format " + PyStr.Repr(format));
                char f = format[i];
                switch (f)
                {
                    case 'Y': regex.Append(@"(\d{4})"); break;
                    case 'y': regex.Append(@"(\d{2})"); break;
                    case 'm': case 'd': case 'H': case 'I': case 'M': case 'S': regex.Append(@"(\d{1,2})"); break;
                    case 'j': regex.Append(@"(\d{1,3})"); break;
                    case 'f': regex.Append(@"(\d{1,6})"); break;
                    case 'p': regex.Append(@"(AM|PM|am|pm)"); break;
                    case 'b': case 'h': case 'B': regex.Append(@"([A-Za-z]+)"); break;
                    case 'a': case 'A': regex.Append(@"([A-Za-z]+)"); break;
                    case 'z': regex.Append(@"(Z|[+-]\d{2}:?\d{2}(?::?\d{2})?)"); break;
                    case '%': regex.Append("%"); continue;
                    default:
                        throw new PyValueError(string.Format("'{0}' is a bad directive in format {1}", f, PyStr.Repr(format)));
                }
                groups.Add(f);
            }
            regex.Append("$");
            Match m = Regex.Match(s, regex.ToString(), RegexOptions.IgnoreCase);
            if (!m.Success)
                throw new PyValueError(string.Format("time data {0} does not match format {1}", PyStr.Repr(s), PyStr.Repr(format)));
            int year = 1900, month = 1, day = 1, hour = 0, minute = 0, second = 0, micro = 0, yday = -1;
            bool pm = false, hasAmPm = false;
            TimeSpan? offset = null;
            for (int gi = 0; gi < groups.Count; gi++)
            {
                string v = m.Groups[gi + 1].Value;
                switch (groups[gi])
                {
                    case 'Y': year = int.Parse(v, CultureInfo.InvariantCulture); break;
                    case 'y':
                        year = int.Parse(v, CultureInfo.InvariantCulture);
                        year += year < 69 ? 2000 : 1900;
                        break;
                    case 'm': month = int.Parse(v, CultureInfo.InvariantCulture); break;
                    case 'd': day = int.Parse(v, CultureInfo.InvariantCulture); break;
                    case 'H': hour = int.Parse(v, CultureInfo.InvariantCulture); break;
                    case 'I': hour = int.Parse(v, CultureInfo.InvariantCulture) % 12; break;
                    case 'M': minute = int.Parse(v, CultureInfo.InvariantCulture); break;
                    case 'S': second = int.Parse(v, CultureInfo.InvariantCulture); break;
                    case 'j': yday = int.Parse(v, CultureInfo.InvariantCulture); break;
                    case 'f': micro = int.Parse(v.PadRight(6, '0'), CultureInfo.InvariantCulture); break;
                    case 'p':
                        hasAmPm = true;
                        pm = v.ToUpperInvariant() == "PM";
                        break;
                    case 'b':
                    case 'h':
                    case 'B':
                        {
                            string lower = v.ToLowerInvariant();
                            int idx = Array.FindIndex(MonthNames, n => n == lower || n.Substring(0, 3) == lower);
                            if (idx < 0)
                                throw new PyValueError(string.Format("time data {0} does not match format {1}", PyStr.Repr(s), PyStr.Repr(format)));
                            month = idx + 1;
                            break;
                        }
                    case 'a':
                    case 'A':
                        {
                            string lower = v.ToLowerInvariant();
                            if (!DayNames.Any(n => n == lower || n.Substring(0, 3) == lower))
                                throw new PyValueError(string.Format("time data {0} does not match format {1}", PyStr.Repr(s), PyStr.Repr(format)));
                            break;
                        }
                    case 'z':
                        if (v == "Z" || v == "z")
                        {
                            offset = TimeSpan.Zero;
                        }
                        else
                        {
                            string digits = v.Substring(1).Replace(":", "");
                            TimeSpan o = new TimeSpan(int.Parse(digits.Substring(0, 2), CultureInfo.InvariantCulture),
                                                      int.Parse(digits.Substring(2, 2), CultureInfo.InvariantCulture),
                                                      digits.Length >= 6 ? int.Parse(digits.Substring(4, 2), CultureInfo.InvariantCulture) : 0);
                            offset = v[0] == '-' ? o.Negate() : o;
                        }
                        break;
                }
            }
            if (hasAmPm && pm)
                hour += 12;
            if (yday > 0 && !groups.Contains('m') && !groups.Contains('d'))
            {
                DateTime d = new DateTime(year, 1, 1).AddDays(yday - 1);
                month = d.Month;
                day = d.Day;
            }
            var dt = (PyDateTime)DateTimeConstructor(
                new List<object> { (long)year, (long)month, (long)day, (long)hour, (long)minute, (long)second, (long)micro },
                new Dictionary<string, object>());
            return offset.HasValue ? new PyDateTime(dt.Wall, offset) : dt;
        }
    }
}
