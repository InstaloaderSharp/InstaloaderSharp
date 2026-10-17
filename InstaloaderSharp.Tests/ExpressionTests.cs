using System;
using System.Collections.Generic;
using InstaloaderSharp.Python;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace InstaloaderSharp.Tests
{
    [TestClass]
    public class ExpressionTests
    {
        /// <summary>An item with properties like a Post's; the expressions refer to them by their Python names.</summary>
        public class Item
        {
            public long Likes { get { return 150; } }
            public string Caption { get { return "Hello World #cat"; } }
            public List<string> CaptionHashtags { get { return new List<string> { "cat", "dogs_2" }; } }
            public DateTime DateUtc { get { return new DateTime(2021, 1, 1, 12, 0, 0, DateTimeKind.Utc); } }
            public DateTimeOffset DateLocal { get { return new DateTimeOffset(2021, 1, 1, 13, 0, 0, TimeSpan.FromHours(1)); } }
            public bool IsVideo { get { return false; } }
            public string Title { get { return null; } }
            public double? VideoDuration { get { return 12.5; } }
        }

        private static object Eval(string expression)
        {
            return PyExpression.Parse(expression).Evaluate(new Item());
        }

        private static string Repr(string expression)
        {
            return PyStr.Repr(Eval(expression));
        }

        [TestMethod]
        public void ArithmeticFollowsPythonSemantics()
        {
            // Expected values from Python's eval
            Assert.AreEqual("-4", Repr("-7 // 2"));
            Assert.AreEqual("2", Repr("-7 % 3"));
            Assert.AreEqual("3.5", Repr("7 / 2"));
            Assert.AreEqual("-22500", Repr("-likes ** 2"));
            Assert.AreEqual("1024", Repr("2 ** 10"));
            Assert.AreEqual("0.5", Repr("2 ** -1"));
            Assert.AreEqual("100000000000000000000", Repr("10 ** 20"));
            Assert.AreEqual("37.5", Repr("likes / 4"));
            Assert.AreEqual("0", Repr("likes // 7 % 3"));
            Assert.AreEqual("'abab'", Repr("'ab' * 2"));
            Assert.AreEqual("[1, 2, 3]", Repr("[1, 2] + [3]"));
        }

        [TestMethod]
        public void ComparisonsAndBooleanOperators()
        {
            Assert.AreEqual("True", Repr("likes > 100"));
            Assert.AreEqual("True", Repr("100 < likes <= 150 != 151"));
            Assert.AreEqual("False", Repr("1 < 2 > 3"));
            Assert.AreEqual("'x'", Repr("0 or '' or 'x'"));
            Assert.AreEqual("0", Repr("1 and 0 and 2"));
            Assert.AreEqual("True", Repr("not is_video and 'cat' in caption_hashtags"));
            Assert.AreEqual("True", Repr("title is None and title is not 0"));
            Assert.AreEqual("'yes'", Repr("'yes' if likes > 5 else 'no'"));
            Assert.AreEqual("True", Repr("'World' in caption"));
        }

        [TestMethod]
        public void DatetimeComparisons()
        {
            Assert.AreEqual("True", Repr("date_utc > datetime(2020, 1, 1)"));
            Assert.AreEqual("True", Repr("date_utc.year == 2021 and date_utc.month in (1, 2, 3)"));
            Assert.AreEqual("1", Repr("(date_utc - datetime(2020, 12, 31)).days"));
            Assert.AreEqual("'01.01.2021'", Repr("date_utc.strftime('%d.%m.%Y')"));
            try
            {
                Eval("date_local > datetime(2020, 1, 1)");
                Assert.Fail();
            }
            catch (PyTypeError err)
            {
                Assert.AreEqual("can't compare offset-naive and offset-aware datetimes", err.Message);
            }
        }

        [TestMethod]
        public void StringMethodsSubscriptsAndFormatting()
        {
            Assert.AreEqual("'hello'", Repr("caption.split()[0].lower()"));
            Assert.AreEqual("True", Repr("caption.startswith(('x', 'Hello'))"));
            Assert.AreEqual("['dogs_2', 'cat']", Repr("caption_hashtags[::-1]"));
            Assert.AreEqual("'llo'", Repr("caption[2:5]"));
            Assert.AreEqual("'150-12.5'", Repr("f'{likes}-{video_duration}'"));
            Assert.AreEqual("'00150'", Repr("f'{likes:05d}'"));
            Assert.AreEqual("'150 likes'", Repr("'{0} likes'.format(likes)"));
            Assert.AreEqual("'150'", Repr("'%d' % likes"));
            Assert.AreEqual("{'a': 1}", Repr("{'a': 1}"));
        }

        [TestMethod]
        public void RuntimeErrorsAreTypedLikePython()
        {
            try
            {
                Eval("1 / 0");
                Assert.Fail();
            }
            catch (PyZeroDivisionError err)
            {
                Assert.AreEqual("division by zero", err.Message);
            }
            try
            {
                Eval("caption + 1");
                Assert.Fail();
            }
            catch (PyTypeError err)
            {
                Assert.AreEqual("can only concatenate str (not \"int\") to str", err.Message);
            }
            try
            {
                Eval("caption_hashtags[5]");
                Assert.Fail();
            }
            catch (PyIndexError)
            {
            }
            try
            {
                Eval("caption.nonexistent");
                Assert.Fail();
            }
            catch (PyAttributeError err)
            {
                Assert.AreEqual("'str' object has no attribute 'nonexistent'", err.Message);
            }
        }

        [TestMethod]
        public void ParserCollectsItemAttributeNames()
        {
            PyExpression e = PyExpression.Parse("likes > 1 and datetime(2020, 1, 1) < date_utc or f'{title}'");
            CollectionAssert.AreEqual(new List<string> { "likes", "date_utc", "title" }, e.Names);
        }

        [TestMethod]
        public void SyntaxErrorsAreReported()
        {
            foreach (string bad in new[] { "likes >", "x = 5", "(1, 2", "likes likes", "lambda: 1" })
            {
                try
                {
                    PyExpression.Parse(bad);
                    Assert.Fail(bad);
                }
                catch (PySyntaxError)
                {
                }
                catch (InvalidArgumentException)
                {
                }
            }
        }

        [TestMethod]
        public void AssignmentsInFiltersAreRejected()
        {
            foreach (string bad in new[] { "[x for x in caption_hashtags]", "(y := 5)" })
            {
                try
                {
                    PyExpression.Parse(bad);
                    Assert.Fail(bad);
                }
                catch (InvalidArgumentException err)
                {
                    StringAssert.Contains(err.Message, "Modifying variables");
                }
            }
        }

        [TestMethod]
        public void PostFilterChecksAttributeNames()
        {
            Func<Post, bool> filter = Program.FilterstrToFilterfunc<Post>("likes > 100 and not is_video");
            Assert.IsNotNull(filter);
            try
            {
                Program.FilterstrToFilterfunc<Post>("len(caption) > 3");
                Assert.Fail();
            }
            catch (InvalidArgumentException err)
            {
                Assert.AreEqual("Invalid filter: len not a Post attribute.", err.Message);
            }
        }
    }
}
