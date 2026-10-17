using System.Numerics;
using InstaloaderSharp.Json;
using InstaloaderSharp.Python;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace InstaloaderSharp.Tests
{
    [TestClass]
    public class JsonTests
    {
        private static string E(int codePoint)
        {
            return char.ConvertFromUtf32(codePoint);
        }

        [TestMethod]
        public void DumpsMatchesPythonDefaultSeparators()
        {
            JToken obj = PyJson.Loads("{\"b\": [1, 1.0, true, null], \"a\": {\"x\": \"y\"}}");
            Assert.AreEqual("{\"b\": [1, 1.0, true, null], \"a\": {\"x\": \"y\"}}", PyJson.Dumps(obj));
        }

        [TestMethod]
        public void DumpsCompactAsInstaloaderXzFiles()
        {
            JToken obj = PyJson.Loads("{\"b\": [1, 2], \"a\": {}}");
            Assert.AreEqual("{\"b\":[1,2],\"a\":{}}", PyJson.Dumps(obj, null, ",", ":"));
        }

        [TestMethod]
        public void DumpsIndentedAndSortedAsInstaloaderJsonFiles()
        {
            JToken obj = PyJson.Loads("{\"b\": [1, {\"z\": 1, \"y\": []}], \"a\": {}}");
            const string expected = "{\n    \"a\": {},\n    \"b\": [\n        1,\n        {\n            \"y\": [],\n            \"z\": 1\n        }\n    ]\n}";
            Assert.AreEqual(expected, PyJson.Dumps(obj, 4, null, null, true));
        }

        [TestMethod]
        public void SortKeysUsesCodePointOrder()
        {
            var obj = new JObject { { "b", 1 }, { "B", 2 }, { "a", 3 }, { E(0xE9), 4 } };
            Assert.AreEqual("{\"B\": 2, \"a\": 3, \"b\": 1, \"\\u00e9\": 4}", PyJson.Dumps(obj, null, null, null, true));
        }

        [TestMethod]
        public void StringsAreEscapedWithEnsureAscii()
        {
            string s = "\"\\\n\r\t\b\f" + (char)1 + (char)0x7F + E(0xE9) + E(0x1F600) + "/";
            Assert.AreEqual("\"\\\"\\\\\\n\\r\\t\\b\\f\\u0001\\u007f\\u00e9\\ud83d\\ude00/\"", PyJson.Dumps(s));
        }

        [TestMethod]
        public void FloatsAreFormattedLikePythonRepr()
        {
            string[] inputs = { "1.0", "1e16", "1e15", "1e-5", "0.0001", "123.456", "-0.0", "1.7976931348623157e308",
                                "5e-324", "0.30000000000000004", "2.5", "100.0", "1e22", "-1.5e-07" };
            string[] expected = { "1.0", "1e+16", "1000000000000000.0", "1e-05", "0.0001", "123.456", "-0.0",
                                  "1.7976931348623157e+308", "5e-324", "0.30000000000000004", "2.5", "100.0", "1e+22",
                                  "-1.5e-07" };
            for (int i = 0; i < inputs.Length; i++)
                Assert.AreEqual(expected[i], PyJson.Dumps(PyJson.Loads(inputs[i])), inputs[i]);
        }

        [TestMethod]
        public void SpecialFloatsUsePythonsNames()
        {
            Assert.AreEqual("[NaN, Infinity, -Infinity]", PyJson.Dumps(PyJson.Loads("[NaN, Infinity, -Infinity]")));
        }

        [TestMethod]
        public void LargeIntegersArePreserved()
        {
            JToken big = PyJson.Loads("12345678901234567890");
            Assert.AreEqual(JTokenType.Integer, big.Type);
            Assert.AreEqual(BigInteger.Parse("12345678901234567890"), big.ToBigInteger());
            Assert.AreEqual("12345678901234567890", PyJson.Dumps(big));
        }

        [TestMethod]
        public void ObjectsKeepInsertionOrderAndDuplicateKeysKeepFirstPosition()
        {
            JObject obj = (JObject)PyJson.Loads("{\"z\": 1, \"a\": 2, \"z\": 3}");
            CollectionAssert.AreEqual(new[] { "z", "a" }, new System.Collections.Generic.List<string>(obj.Keys));
            Assert.AreEqual(3L, (long)obj["z"]);
        }

        [TestMethod]
        public void DecodeErrorsHavePythonMessages()
        {
            try
            {
                PyJson.Loads("");
                Assert.Fail();
            }
            catch (JsonDecodeError err)
            {
                Assert.AreEqual("Expecting value: line 1 column 1 (char 0)", err.Message);
            }
            try
            {
                PyJson.Loads("{\"a\": 1}\n x");
                Assert.Fail();
            }
            catch (JsonDecodeError err)
            {
                Assert.AreEqual("Extra data: line 2 column 2 (char 10)", err.Message);
            }
        }

        [TestMethod]
        public void IndexingBehavesLikePython()
        {
            JToken obj = PyJson.Loads("{\"list\": [1, 2, 3], \"none\": null}");
            Assert.AreEqual(3L, (long)obj["list"][-1]);
            Assert.IsNull(obj.Get("missing"));
            Assert.IsTrue(obj.Get("none").IsNull);
            try
            {
                JToken unused = obj["missing"];
                Assert.Fail();
            }
            catch (PyKeyError err)
            {
                Assert.AreEqual("'missing'", err.Message);
            }
            try
            {
                JToken unused = obj["none"]["x"];
                Assert.Fail();
            }
            catch (PyTypeError err)
            {
                Assert.AreEqual("'NoneType' object is not subscriptable", err.Message);
            }
            try
            {
                JToken unused = obj["list"][3];
                Assert.Fail();
            }
            catch (PyIndexError err)
            {
                Assert.AreEqual("list index out of range", err.Message);
            }
        }

        [TestMethod]
        public void DeepEqualsUsesPythonEquality()
        {
            Assert.IsTrue(JToken.DeepEquals(PyJson.Loads("{\"a\": 1, \"b\": [true]}"), PyJson.Loads("{\"b\": [1], \"a\": 1.0}")));
            Assert.IsFalse(JToken.DeepEquals(PyJson.Loads("[1, 2]"), PyJson.Loads("[2, 1]")));
        }

        [TestMethod]
        public void PythonIntConversionAcceptsStrings()
        {
            Assert.AreEqual(25025320L, PyJson.Loads("\"25025320\"").ToInt64());
            Assert.AreEqual(3L, PyJson.Loads("3.9").ToInt64());
            try
            {
                PyJson.Loads("\"abc\"").ToInt64();
                Assert.Fail();
            }
            catch (PyValueError err)
            {
                Assert.AreEqual("invalid literal for int() with base 10: 'abc'", err.Message);
            }
        }

        [TestMethod]
        public void PythonFloatRepr()
        {
            Assert.AreEqual("0.1", PyFloat.Repr(0.1));
            Assert.AreEqual("52.5", PyFloat.Repr(52.5));
            Assert.AreEqual("1e+100", PyFloat.Repr(1e100));
            Assert.AreEqual("inf", PyFloat.Repr(double.PositiveInfinity));
        }
    }
}
