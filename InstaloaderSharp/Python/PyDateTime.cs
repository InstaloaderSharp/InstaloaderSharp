using System;
using System.Globalization;
using System.Text;

namespace InstaloaderSharp.Python
{
    /// <summary>
    /// Python's datetime.datetime: a wall clock time, either naive (no offset) or aware (with a UTC offset).
    /// </summary>
    public sealed class PyDateTime : IPyObject, IComparable<PyDateTime>
    {
        private static readonly DateTime Epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Unspecified);

        /// <summary>The wall clock time (Kind is Unspecified), with microsecond precision.</summary>
        public DateTime Wall { get; private set; }

        /// <summary>The UTC offset of an aware datetime, or null for a naive one.</summary>
        public TimeSpan? Offset { get; private set; }

        /// <summary>Time zone name for %Z, if known.</summary>
        public string TzName { get; private set; }

        public PyDateTime(DateTime wall, TimeSpan? offset = null, string tzName = null)
        {
            // Python's datetime has microsecond resolution.
            long ticks = wall.Ticks - wall.Ticks % 10;
            Wall = new DateTime(ticks, DateTimeKind.Unspecified);
            Offset = offset;
            TzName = tzName;
        }

        public bool IsAware { get { return Offset.HasValue; } }

        /// <summary>Converts a .NET DateTime (to a naive datetime) or DateTimeOffset (to an aware datetime).</summary>
        public static PyDateTime FromObject(object value)
        {
            if (value is PyDateTime)
                return (PyDateTime)value;
            if (value is DateTimeOffset)
            {
                var dto = (DateTimeOffset)value;
                return new PyDateTime(dto.DateTime, dto.Offset, LocalTzName(dto));
            }
            if (value is DateTime)
                return new PyDateTime((DateTime)value);
            throw new PyTypeError("expected datetime");
        }

        private static string LocalTzName(DateTimeOffset dto)
        {
            try
            {
                TimeZoneInfo local = TimeZoneInfo.Local;
                if (local.GetUtcOffset(dto.UtcDateTime) != dto.Offset)
                    return null;
                return local.IsDaylightSavingTime(dto.UtcDateTime) ? local.DaylightName : local.StandardName;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>For aware datetimes, the represented instant.</summary>
        public DateTimeOffset ToDateTimeOffset()
        {
            if (Offset.HasValue)
                return new DateTimeOffset(Wall, Offset.Value);
            // Naive datetimes are interpreted as local time, as Python's timestamp() does.
            return new DateTimeOffset(DateTime.SpecifyKind(Wall, DateTimeKind.Local));
        }

        /// <summary>Python's datetime.timestamp().</summary>
        public double Timestamp()
        {
            DateTimeOffset dto = ToDateTimeOffset();
            return (dto.UtcDateTime - Epoch).Ticks / 1e7;
        }

        #region constructors like Python's class methods

        /// <summary>datetime.now(): naive local time.</summary>
        public static PyDateTime Now()
        {
            return new PyDateTime(DateTime.Now);
        }

        /// <summary>datetime.utcnow(): naive UTC time.</summary>
        public static PyDateTime UtcNow()
        {
            return new PyDateTime(DateTime.UtcNow);
        }

        private static DateTime FromUnixTicks(double timestamp)
        {
            // Round to microseconds with round-half-even, like Python.
            double us = Math.Round(timestamp * 1e6, MidpointRounding.ToEven);
            return Epoch.AddTicks((long)us * 10);
        }

        /// <summary>datetime.fromtimestamp(ts): naive local time.</summary>
        public static PyDateTime FromTimestamp(double timestamp)
        {
            DateTime utc = DateTime.SpecifyKind(FromUnixTicks(timestamp), DateTimeKind.Utc);
            return new PyDateTime(utc.ToLocalTime());
        }

        /// <summary>datetime.utcfromtimestamp(ts): naive UTC time.</summary>
        public static PyDateTime UtcFromTimestamp(double timestamp)
        {
            return new PyDateTime(FromUnixTicks(timestamp));
        }

        /// <summary>datetime.fromtimestamp(ts).astimezone(): aware local time.</summary>
        public static DateTimeOffset LocalFromTimestamp(double timestamp)
        {
            DateTime utc = DateTime.SpecifyKind(FromUnixTicks(timestamp), DateTimeKind.Utc);
            return new DateTimeOffset(utc).ToLocalTime();
        }

        /// <summary>Python's datetime.astimezone() without argument: convert to aware local time.</summary>
        public PyDateTime AsTimeZone()
        {
            DateTimeOffset local = ToDateTimeOffset().ToLocalTime();
            return FromObject(local);
        }

        #endregion

        #region components

        public int Year { get { return Wall.Year; } }
        public int Month { get { return Wall.Month; } }
        public int Day { get { return Wall.Day; } }
        public int Hour { get { return Wall.Hour; } }
        public int Minute { get { return Wall.Minute; } }
        public int Second { get { return Wall.Second; } }
        public int Microsecond { get { return (int)(Wall.Ticks % TimeSpan.TicksPerSecond / 10); } }

        /// <summary>Python's weekday(): Monday is 0.</summary>
        public int Weekday()
        {
            return ((int)Wall.DayOfWeek + 6) % 7;
        }

        /// <summary>Python's isoweekday(): Monday is 1.</summary>
        public int IsoWeekday()
        {
            return Weekday() + 1;
        }

        #endregion

        #region comparison and arithmetic

        private static bool BothSameKind(PyDateTime a, PyDateTime b)
        {
            return a.IsAware == b.IsAware;
        }

        /// <summary>Comparison; raises TypeError when comparing naive and aware datetimes, like Python.</summary>
        public int CompareTo(PyDateTime other)
        {
            if (!BothSameKind(this, other))
                throw new PyTypeError("can't compare offset-naive and offset-aware datetimes");
            if (IsAware)
                return ToDateTimeOffset().UtcTicks.CompareTo(other.ToDateTimeOffset().UtcTicks);
            return Wall.Ticks.CompareTo(other.Wall.Ticks);
        }

        /// <summary>Python's ==, which is False (not an error) for naive vs aware.</summary>
        public bool PyEquals(PyDateTime other)
        {
            if (other == null || !BothSameKind(this, other))
                return false;
            return CompareTo(other) == 0;
        }

        public override bool Equals(object obj)
        {
            return PyEquals(obj as PyDateTime);
        }

        public override int GetHashCode()
        {
            return IsAware ? ToDateTimeOffset().UtcTicks.GetHashCode() : Wall.Ticks.GetHashCode();
        }

        public PyTimeDelta Subtract(PyDateTime other)
        {
            if (!BothSameKind(this, other))
                throw new PyTypeError("can't subtract offset-naive and offset-aware datetimes");
            if (IsAware)
                return new PyTimeDelta(ToDateTimeOffset().UtcTicks - other.ToDateTimeOffset().UtcTicks);
            return new PyTimeDelta(Wall.Ticks - other.Wall.Ticks);
        }

        public PyDateTime Add(PyTimeDelta delta)
        {
            return new PyDateTime(Wall.AddTicks(delta.Ticks), Offset, TzName);
        }

        #endregion

        #region formatting

        private static string FormatOffset(TimeSpan offset, bool colon)
        {
            var sb = new StringBuilder();
            sb.Append(offset < TimeSpan.Zero ? '-' : '+');
            TimeSpan abs = offset.Duration();
            sb.Append(((int)abs.TotalHours).ToString("00", CultureInfo.InvariantCulture));
            if (colon) sb.Append(':');
            sb.Append(abs.Minutes.ToString("00", CultureInfo.InvariantCulture));
            if (abs.Seconds != 0 || abs.Milliseconds != 0)
            {
                if (colon) sb.Append(':');
                sb.Append(abs.Seconds.ToString("00", CultureInfo.InvariantCulture));
            }
            return sb.ToString();
        }

        /// <summary>Python's str(datetime): 'YYYY-MM-DD HH:MM:SS[.ffffff][+HH:MM]'.</summary>
        public string PyStr()
        {
            return IsoFormat(' ');
        }

        /// <summary>Python's datetime.isoformat(sep).</summary>
        public string IsoFormat(char sep = 'T')
        {
            var sb = new StringBuilder();
            sb.Append(Wall.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            sb.Append(sep);
            sb.Append(Wall.ToString("HH:mm:ss", CultureInfo.InvariantCulture));
            if (Microsecond != 0)
                sb.Append('.').Append(Microsecond.ToString("000000", CultureInfo.InvariantCulture));
            if (Offset.HasValue)
                sb.Append(FormatOffset(Offset.Value, true));
            return sb.ToString();
        }

        public string PyRepr()
        {
            var sb = new StringBuilder("datetime.datetime(");
            sb.AppendFormat(CultureInfo.InvariantCulture, "{0}, {1}, {2}, {3}, {4}", Year, Month, Day, Hour, Minute);
            if (Second != 0 || Microsecond != 0)
                sb.AppendFormat(CultureInfo.InvariantCulture, ", {0}", Second);
            if (Microsecond != 0)
                sb.AppendFormat(CultureInfo.InvariantCulture, ", {0}", Microsecond);
            if (Offset.HasValue)
            {
                if (Offset.Value == TimeSpan.Zero)
                    sb.Append(", tzinfo=datetime.timezone.utc");
                else
                    sb.AppendFormat(CultureInfo.InvariantCulture,
                                    ", tzinfo=datetime.timezone(datetime.timedelta(seconds={0}), {1})",
                                    (long)Offset.Value.TotalSeconds, Python.PyStr.ReprString(TzName ?? ""));
            }
            sb.Append(')');
            return sb.ToString();
        }

        public override string ToString()
        {
            return PyStr();
        }

        private static readonly string[] DayNames =
            { "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday" };
        private static readonly string[] MonthNames =
        {
            "January", "February", "March", "April", "May", "June", "July", "August", "September", "October",
            "November", "December"
        };

        private static void IsoWeek(DateTime d, out int isoYear, out int isoWeek)
        {
            int weekday = ((int)d.DayOfWeek + 6) % 7; // Monday = 0
            DateTime thursday = d.AddDays(3 - weekday);
            isoYear = thursday.Year;
            isoWeek = (thursday.DayOfYear - 1) / 7 + 1;
        }

        private static string Num(int value, int width, bool noPad, char pad = '0')
        {
            string s = value.ToString(CultureInfo.InvariantCulture);
            if (noPad)
                return s;
            return s.Length >= width ? s : new string(pad, width - s.Length) + s;
        }

        /// <summary>Python's datetime.strftime(format), with the directives supported on Windows.</summary>
        public string Strftime(string format)
        {
            var sb = new StringBuilder();
            DateTime d = Wall;
            for (int i = 0; i < format.Length; i++)
            {
                char c = format[i];
                if (c != '%')
                {
                    sb.Append(c);
                    continue;
                }
                if (i + 1 >= format.Length)
                    throw new PyValueError("Invalid format string");
                char f = format[++i];
                bool noPad = false;
                if (f == '#')
                {
                    if (i + 1 >= format.Length)
                        throw new PyValueError("Invalid format string");
                    noPad = true;
                    f = format[++i];
                }
                if (f == ':' && i + 1 < format.Length && format[i + 1] == 'z')
                {
                    i++;
                    if (Offset.HasValue)
                        sb.Append(FormatOffset(Offset.Value, true));
                    continue;
                }
                int weekday = Weekday();
                int sundayWeekday = (int)d.DayOfWeek;
                switch (f)
                {
                    case 'a': sb.Append(DayNames[weekday].Substring(0, 3)); break;
                    case 'A': sb.Append(DayNames[weekday]); break;
                    case 'b':
                    case 'h': sb.Append(MonthNames[d.Month - 1].Substring(0, 3)); break;
                    case 'B': sb.Append(MonthNames[d.Month - 1]); break;
                    case 'c':
                        // As Python 3.14 formats it on Windows
                        if (noPad)
                            sb.Append(Strftime("%A, %B %d, %Y %H:%M:%S"));
                        else
                            sb.Append(Strftime("%a %b %e %H:%M:%S %Y"));
                        break;
                    case 'C': sb.Append(Num(d.Year / 100, 2, noPad)); break;
                    case 'd': sb.Append(Num(d.Day, 2, noPad)); break;
                    case 'D': sb.Append(Strftime("%m/%d/%y")); break;
                    case 'e': sb.Append(Num(d.Day, 2, noPad, ' ')); break;
                    case 'f': sb.Append(Microsecond.ToString("000000", CultureInfo.InvariantCulture)); break;
                    case 'F': sb.Append(Strftime("%Y-%m-%d")); break;
                    case 'g':
                        {
                            int isoYear, isoWeek;
                            IsoWeek(d, out isoYear, out isoWeek);
                            sb.Append(Num(isoYear % 100, 2, noPad));
                            break;
                        }
                    case 'G':
                        {
                            int isoYear, isoWeek;
                            IsoWeek(d, out isoYear, out isoWeek);
                            sb.Append(Num(isoYear, 4, noPad));
                            break;
                        }
                    case 'H': sb.Append(Num(d.Hour, 2, noPad)); break;
                    case 'I': sb.Append(Num(d.Hour % 12 == 0 ? 12 : d.Hour % 12, 2, noPad)); break;
                    case 'j': sb.Append(Num(d.DayOfYear, 3, noPad)); break;
                    case 'm': sb.Append(Num(d.Month, 2, noPad)); break;
                    case 'M': sb.Append(Num(d.Minute, 2, noPad)); break;
                    case 'n': sb.Append('\n'); break;
                    case 'p': sb.Append(d.Hour < 12 ? "AM" : "PM"); break;
                    case 'r': sb.Append(Strftime("%I:%M:%S %p")); break;
                    case 'R': sb.Append(Strftime("%H:%M")); break;
                    case 'S': sb.Append(Num(d.Second, 2, noPad)); break;
                    case 't': sb.Append('\t'); break;
                    case 'T': sb.Append(Strftime("%H:%M:%S")); break;
                    case 'u': sb.Append(IsoWeekday()); break;
                    case 'U': sb.Append(Num((d.DayOfYear - 1 + 7 - sundayWeekday) / 7, 2, noPad)); break;
                    case 'V':
                        {
                            int isoYear, isoWeek;
                            IsoWeek(d, out isoYear, out isoWeek);
                            sb.Append(Num(isoWeek, 2, noPad));
                            break;
                        }
                    case 'w': sb.Append(sundayWeekday); break;
                    case 'W': sb.Append(Num((d.DayOfYear - 1 + 7 - weekday) / 7, 2, noPad)); break;
                    case 'x':
                        sb.Append(noPad ? Strftime("%A, %B %d, %Y") : Strftime("%m/%d/%y"));
                        break;
                    case 'X': sb.Append(Strftime("%H:%M:%S")); break;
                    case 'y': sb.Append(Num(d.Year % 100, 2, noPad)); break;
                    case 'Y': sb.Append(Num(d.Year, 4, noPad)); break;
                    case 'z':
                        if (Offset.HasValue)
                            sb.Append(FormatOffset(Offset.Value, false));
                        break;
                    case 'Z':
                        if (Offset.HasValue)
                            sb.Append(TzName ?? (Offset.Value == TimeSpan.Zero ? "UTC" : "UTC" + FormatOffset(Offset.Value, true)));
                        break;
                    case '%': sb.Append('%'); break;
                    default:
                        throw new PyValueError("Invalid format string");
                }
            }
            return sb.ToString();
        }

        /// <summary>
        /// Parses a timestamp in the format '%Y-%m-%dT%H:%M:%S.%f%z' like Python's datetime.strptime would.
        /// </summary>
        public static DateTimeOffset ParseIsoWithOffset(string s)
        {
            // %Y-%m-%dT%H:%M:%S.%f%z; %f takes 1 to 6 digits, %z accepts +HHMM[SS[.ffffff]], +HH:MM[:SS] or Z.
            var m = System.Text.RegularExpressions.Regex.Match(
                s, @"^(\d{4})-(\d{1,2})-(\d{1,2})T(\d{1,2}):(\d{1,2}):(\d{1,2})\.(\d{1,6})(Z|[+-]\d\d:?\d\d(?::?\d\d)?)$");
            if (!m.Success)
                throw new PyValueError(string.Format("time data {0} does not match format '%Y-%m-%dT%H:%M:%S.%f%z'",
                                                     Python.PyStr.Repr(s)));
            int year = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            int month = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
            int day = int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture);
            int hour = int.Parse(m.Groups[4].Value, CultureInfo.InvariantCulture);
            int minute = int.Parse(m.Groups[5].Value, CultureInfo.InvariantCulture);
            int second = int.Parse(m.Groups[6].Value, CultureInfo.InvariantCulture);
            int micro = int.Parse(m.Groups[7].Value.PadRight(6, '0'), CultureInfo.InvariantCulture);
            TimeSpan offset = TimeSpan.Zero;
            string z = m.Groups[8].Value;
            if (z != "Z")
            {
                string digits = z.Substring(1).Replace(":", "");
                int oh = int.Parse(digits.Substring(0, 2), CultureInfo.InvariantCulture);
                int om = int.Parse(digits.Substring(2, 2), CultureInfo.InvariantCulture);
                int os = digits.Length >= 6 ? int.Parse(digits.Substring(4, 2), CultureInfo.InvariantCulture) : 0;
                offset = new TimeSpan(oh, om, os);
                if (z[0] == '-')
                    offset = offset.Negate();
            }
            try
            {
                var wall = new DateTime(year, month, day, hour, minute, second, DateTimeKind.Unspecified)
                    .AddTicks(micro * 10L);
                // DateTimeOffset requires whole-minute offsets.
                long offsetMinutes = (long)Math.Truncate(offset.TotalMinutes);
                var dto = new DateTimeOffset(wall.AddMinutes(0), TimeSpan.FromMinutes(offsetMinutes));
                return dto.AddSeconds(-(offset.TotalSeconds - offsetMinutes * 60));
            }
            catch (ArgumentException ex)
            {
                throw new PyValueError(ex.Message);
            }
        }

        /// <summary>Formats a DateTimeOffset with '%Y-%m-%dT%H:%M:%S.%f%z'.</summary>
        public static string FormatIsoWithOffset(DateTimeOffset dto)
        {
            return FromObject(dto).Strftime("%Y-%m-%dT%H:%M:%S.%f%z");
        }

        #endregion
    }

    /// <summary>Python's datetime.timedelta.</summary>
    public sealed class PyTimeDelta : IPyObject, IComparable<PyTimeDelta>
    {
        public PyTimeDelta(long ticks)
        {
            Ticks = ticks - ticks % 10;
        }

        public PyTimeDelta(TimeSpan span) : this(span.Ticks) { }

        public long Ticks { get; private set; }

        public long Days { get { return FloorDiv(Ticks, TimeSpan.TicksPerDay); } }

        public long Seconds
        {
            get { return FloorMod(Ticks, TimeSpan.TicksPerDay) / TimeSpan.TicksPerSecond; }
        }

        public long Microseconds
        {
            get { return FloorMod(Ticks, TimeSpan.TicksPerSecond) / 10; }
        }

        public double TotalSeconds()
        {
            return Ticks / 1e7;
        }

        private static long FloorDiv(long a, long b)
        {
            long q = a / b;
            if ((a % b != 0) && ((a < 0) != (b < 0)))
                q--;
            return q;
        }

        private static long FloorMod(long a, long b)
        {
            long m = a % b;
            if (m != 0 && ((m < 0) != (b < 0)))
                m += b;
            return m;
        }

        public int CompareTo(PyTimeDelta other)
        {
            return Ticks.CompareTo(other.Ticks);
        }

        public override bool Equals(object obj)
        {
            var other = obj as PyTimeDelta;
            return other != null && other.Ticks == Ticks;
        }

        public override int GetHashCode()
        {
            return Ticks.GetHashCode();
        }

        public string PyStr()
        {
            var sb = new StringBuilder();
            long days = Days;
            if (days != 0)
                sb.AppendFormat(CultureInfo.InvariantCulture, "{0} day{1}, ", days, Math.Abs(days) != 1 ? "s" : "");
            long secs = Seconds;
            sb.AppendFormat(CultureInfo.InvariantCulture, "{0}:{1:00}:{2:00}", secs / 3600, secs / 60 % 60, secs % 60);
            if (Microseconds != 0)
                sb.AppendFormat(CultureInfo.InvariantCulture, ".{0:000000}", Microseconds);
            return sb.ToString();
        }

        public string PyRepr()
        {
            var parts = new System.Collections.Generic.List<string>();
            if (Days != 0) parts.Add("days=" + Days.ToString(CultureInfo.InvariantCulture));
            if (Seconds != 0) parts.Add("seconds=" + Seconds.ToString(CultureInfo.InvariantCulture));
            if (Microseconds != 0) parts.Add("microseconds=" + Microseconds.ToString(CultureInfo.InvariantCulture));
            return "datetime.timedelta(" + (parts.Count == 0 ? "0" : string.Join(", ", parts)) + ")";
        }

        public override string ToString()
        {
            return PyStr();
        }
    }
}
