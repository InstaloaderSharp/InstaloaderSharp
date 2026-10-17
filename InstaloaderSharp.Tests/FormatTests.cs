using System;
using System.Collections.Generic;
using System.Numerics;
using InstaloaderSharp.Python;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace InstaloaderSharp.Tests
{
    [TestClass]
    public class FormatTests
    {
        private static string Format(string pattern, params object[] keyValues)
        {
            var kwargs = new Dictionary<string, object>();
            for (int i = 0; i + 1 < keyValues.Length; i += 2)
                kwargs[(string)keyValues[i]] = PyRuntime.ToPy(keyValues[i + 1]);
            return new PyFormatter().Format(pattern, kwargs);
        }

        [TestMethod]
        public void IntegerFormatSpecs()
        {
            // Expected values from Python's format(value, spec)
            Assert.AreEqual("00150", PyFormat.Format(150L, "05d"));
            Assert.AreEqual("     150", PyFormat.Format(150L, ">8"));
            Assert.AreEqual("1,234,567", PyFormat.Format(1234567L, ","));
            Assert.AreEqual("1_234_567", PyFormat.Format(1234567L, "_"));
            Assert.AreEqual("96", PyFormat.Format(150L, "x"));
            Assert.AreEqual("0x96", PyFormat.Format(150L, "#x"));
            Assert.AreEqual("0b1010", PyFormat.Format(10L, "#b"));
            Assert.AreEqual("+42", PyFormat.Format(42L, "+"));
            Assert.AreEqual("-0042", PyFormat.Format(-42L, "05"));
            Assert.AreEqual("**42**", PyFormat.Format(42L, "*^6"));
            Assert.AreEqual("150.00", PyFormat.Format(150L, ".2f"));
            Assert.AreEqual("1.500000e+02", PyFormat.Format(150L, "e"));
            Assert.AreEqual("15000.000000%", PyFormat.Format(150L, "%"));
            Assert.AreEqual("12345678901234567890", PyFormat.Format(BigInteger.Parse("12345678901234567890"), ""));
        }

        [TestMethod]
        public void FloatFormatSpecsRoundExactlyLikePython()
        {
            // 2.675 and 0.125 are the classic cases where decimal-based rounding differs from Python
            Assert.AreEqual("2.67", PyFormat.Format(2.675, ".2f"));
            Assert.AreEqual("0.12", PyFormat.Format(0.125, ".2f"));
            Assert.AreEqual("0.38", PyFormat.Format(0.375, ".2f"));
            Assert.AreEqual("12.5", PyFormat.Format(12.5, ""));
            Assert.AreEqual("12.5", PyFormat.Format(12.5, ".1f"));
            Assert.AreEqual("1.234e+03", PyFormat.Format(1234.0, ".4"));
            Assert.AreEqual("123.0", PyFormat.Format(123.0, ".4"));
            Assert.AreEqual("123", PyFormat.Format(123.0, ".4g"));
            Assert.AreEqual("1e+16", PyFormat.Format(1e16, "g"));
            Assert.AreEqual("0.0001", PyFormat.Format(0.0001, "g"));
            Assert.AreEqual("1e-05", PyFormat.Format(0.00001, "g"));
            Assert.AreEqual("1,234.57", PyFormat.Format(1234.5678, ",.2f"));
            Assert.AreEqual("  -1.50", PyFormat.Format(-1.5, "7.2f"));
            Assert.AreEqual("nan", PyFormat.Format(double.NaN, ".2f"));
            Assert.AreEqual("-INF", PyFormat.Format(double.NegativeInfinity, "F"));
        }

        [TestMethod]
        public void StringFormatSpecs()
        {
            Assert.AreEqual("CqX", PyFormat.Format("CqXy_12-Ab", ".3"));
            Assert.AreEqual("        some", PyFormat.Format("someuser", ">12.4"));
            Assert.AreEqual("*someuser**", PyFormat.Format("someuser", "*^11"));
            Assert.AreEqual("ab  ", PyFormat.Format("ab", "4"));
        }

        [TestMethod]
        public void InvalidFormatSpecsFailLikePython()
        {
            try
            {
                PyFormat.Format("abc", "d");
                Assert.Fail();
            }
            catch (PyValueError err)
            {
                Assert.AreEqual("Unknown format code 'd' for object of type 'str'", err.Message);
            }
            try
            {
                PyFormat.Format(1L, ".2d");
                Assert.Fail();
            }
            catch (PyValueError err)
            {
                Assert.AreEqual("Precision not allowed in integer format specifier", err.Message);
            }
        }

        [TestMethod]
        public void StrFormatFieldsConversionsAndNesting()
        {
            Assert.AreEqual("{literal} 5", Format("{{literal}} {a}", "a", 5L));
            Assert.AreEqual("'x' x", Format("{a!r} {a!s}", "a", "x"));
            Assert.AreEqual("   ab", Format("{a:>{w}}", "a", "ab", "w", 5L));
            Assert.AreEqual("2", Format("{a[1]}", "a", new List<object> { 1L, 2L }));
            Assert.AreEqual("2021", Format("{d.year}", "d", new DateTime(2021, 1, 2)));
        }

        [TestMethod]
        public void StrFormatErrors()
        {
            try
            {
                Format("{missing}");
                Assert.Fail();
            }
            catch (PyKeyError err)
            {
                Assert.AreEqual("'missing'", err.Message);
            }
            try
            {
                Format("{a");
                Assert.Fail();
            }
            catch (PyValueError err)
            {
                Assert.AreEqual("expected '}' before end of string", err.Message);
            }
            try
            {
                Format("a}");
                Assert.Fail();
            }
            catch (PyValueError err)
            {
                Assert.AreEqual("Single '}' encountered in format string", err.Message);
            }
        }

        [TestMethod]
        public void FormatStringContainsKey()
        {
            Assert.IsTrue(PyFormatter.FormatStringContainsKey("{target}", "target"));
            Assert.IsTrue(PyFormatter.FormatStringContainsKey("x/{profile.username}", "profile"));
            Assert.IsFalse(PyFormatter.FormatStringContainsKey("{targets}", "target"));
            Assert.IsFalse(PyFormatter.FormatStringContainsKey("{{target}}", "target"));
        }

        [TestMethod]
        public void Strftime()
        {
            var dt = new PyDateTime(new DateTime(2021, 1, 1, 12, 0, 5).AddTicks(1234560));
            Assert.AreEqual("2021-01-01_12-00-05", dt.Strftime("%Y-%m-%d_%H-%M-%S"));
            Assert.AreEqual("Friday January 001 00 00 PM 12 21 %", dt.Strftime("%A %B %j %U %W %p %I %y %%"));
            Assert.AreEqual("123456", dt.Strftime("%f"));
            Assert.AreEqual("Fri Jan  1 12:00:05 2021", dt.Strftime("%c"));
            Assert.AreEqual("Friday, January 01, 2021 12:00:05", dt.Strftime("%#c"));
            Assert.AreEqual("01/01/21 Friday, January 01, 2021 12:00:05", dt.Strftime("%x %#x %X"));
            Assert.AreEqual(" 1 01/01/21 12:00:05 PM 12:00 2021-01-01 2020-53-5 20 20 Jan", dt.Strftime("%e %D %r %R %F %G-%V-%u %g %C %h"));
            Assert.AreEqual("1", dt.Strftime("%#d"));
            Assert.AreEqual("", dt.Strftime("%z"));
            var aware = new PyDateTime(new DateTime(2024, 5, 6, 7, 8, 9), TimeSpan.FromHours(-5.5));
            Assert.AreEqual("-0530 -05:30", aware.Strftime("%z %:z"));
            try
            {
                dt.Strftime("%Q");
                Assert.Fail();
            }
            catch (PyValueError err)
            {
                Assert.AreEqual("Invalid format string", err.Message);
            }
        }

        [TestMethod]
        public void PythonDateTimeStringAndComparison()
        {
            var naive = new PyDateTime(new DateTime(2021, 1, 1, 12, 0, 0));
            var aware = new PyDateTime(new DateTime(2021, 1, 1, 12, 0, 0), TimeSpan.Zero);
            Assert.AreEqual("2021-01-01 12:00:00", naive.PyStr());
            Assert.AreEqual("2021-01-01 12:00:00+00:00", aware.PyStr());
            Assert.IsFalse(naive.PyEquals(aware));
            try
            {
                naive.CompareTo(aware);
                Assert.Fail();
            }
            catch (PyTypeError err)
            {
                Assert.AreEqual("can't compare offset-naive and offset-aware datetimes", err.Message);
            }
        }

        [TestMethod]
        public void IsoTimestampsOfLatestStamps()
        {
            DateTimeOffset dto = PyDateTime.ParseIsoWithOffset("2024-05-06T07:08:09.123456+0200");
            Assert.AreEqual(new DateTimeOffset(2024, 5, 6, 7, 8, 9, TimeSpan.FromHours(2)).AddTicks(1234560), dto);
            Assert.AreEqual("2024-05-06T07:08:09.123456+0200", PyDateTime.FormatIsoWithOffset(dto));
            Assert.AreEqual(dto, PyDateTime.ParseIsoWithOffset("2024-05-06T07:08:09.123456+02:00"));
        }

        [TestMethod]
        public void PrintfStyleFormatting()
        {
            Assert.AreEqual("  42|42   |002a|3.14|'x'|100%",
                            PyRuntime.PrintfFormat("%4d|%-5s|%04x|%.2f|%r|100%%",
                                                   new PyTuple(new object[] { 42L, "42", 42L, 3.14159, "x" })));
        }
    }
}
