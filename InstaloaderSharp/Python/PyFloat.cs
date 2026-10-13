using System;
using System.Globalization;
using System.Text;

namespace InstaloaderSharp.Python
{
    /// <summary>Formatting of floating point numbers exactly like Python's float.__repr__.</summary>
    public static class PyFloat
    {
        /// <summary>Returns the shortest decimal digits that round-trip to <paramref name="value"/>, and the
        /// decimal exponent of the first digit (scientific notation exponent).</summary>
        public static string ShortestDigits(double value, out int exponent)
        {
            double abs = Math.Abs(value);
            if (abs == 0.0)
            {
                exponent = 0;
                return "0";
            }
            string best = null;
            for (int precision = 1; precision <= 17; precision++)
            {
                string s = abs.ToString("E" + (precision - 1).ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
                double parsed;
                if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed) && parsed == abs)
                {
                    best = s;
                    break;
                }
            }
            if (best == null)
                best = abs.ToString("E16", CultureInfo.InvariantCulture);
            int ePos = best.IndexOfAny(new[] { 'E', 'e' });
            string mantissa = best.Substring(0, ePos).Replace(".", "");
            exponent = int.Parse(best.Substring(ePos + 1), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
            mantissa = mantissa.TrimEnd('0');
            if (mantissa.Length == 0)
                mantissa = "0";
            return mantissa;
        }

        /// <summary>repr(float) / str(float) as in Python 3.</summary>
        public static string Repr(double value)
        {
            if (double.IsNaN(value))
                return "nan";
            if (double.IsPositiveInfinity(value))
                return "inf";
            if (double.IsNegativeInfinity(value))
                return "-inf";
            bool negative = value < 0 || (value == 0.0 && 1.0 / value < 0);
            int exp;
            string digits = ShortestDigits(value, out exp);
            int decpt = exp + 1;
            var sb = new StringBuilder();
            if (negative)
                sb.Append('-');
            if (value == 0.0)
            {
                sb.Append("0.0");
                return sb.ToString();
            }
            if (decpt <= -4 || decpt > 16)
            {
                sb.Append(digits[0]);
                if (digits.Length > 1)
                {
                    sb.Append('.');
                    sb.Append(digits, 1, digits.Length - 1);
                }
                sb.Append('e');
                sb.Append(exp < 0 ? '-' : '+');
                sb.Append(Math.Abs(exp).ToString("00", CultureInfo.InvariantCulture));
            }
            else if (decpt <= 0)
            {
                sb.Append("0.");
                sb.Append('0', -decpt);
                sb.Append(digits);
            }
            else if (decpt >= digits.Length)
            {
                sb.Append(digits);
                sb.Append('0', decpt - digits.Length);
                sb.Append(".0");
            }
            else
            {
                sb.Append(digits, 0, decpt);
                sb.Append('.');
                sb.Append(digits, decpt, digits.Length - decpt);
            }
            return sb.ToString();
        }
    }
}
