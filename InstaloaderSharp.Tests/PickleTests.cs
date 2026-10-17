using System.Collections.Generic;
using System.IO;
using InstaloaderSharp.Python;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace InstaloaderSharp.Tests
{
    [TestClass]
    public class PickleTests
    {
        private static Dictionary<string, string> Expected()
        {
            return new Dictionary<string, string>
            {
                { "sessionid", "12345%3AabcDEF%3A9" },
                { "mid", "" },
                { "ig_pr", "1" },
                { "csrftoken", "tokenTOKEN" },
                { "ds_user_id", "12345" },
                { "rur", "\"NHA\\05412345\"" },
                { "unicode", "caf" + (char)0xE9 + " " + (char)0x2615 }
            };
        }

        private static void AssertSession(List<KeyValuePair<string, string>> actual)
        {
            Dictionary<string, string> expected = Expected();
            Assert.AreEqual(expected.Count, actual.Count);
            var keys = new List<string>();
            foreach (var kv in actual)
            {
                Assert.AreEqual(expected[kv.Key], kv.Value, kv.Key);
                keys.Add(kv.Key);
            }
            // Python dicts keep insertion order
            CollectionAssert.AreEqual(new List<string>(expected.Keys), keys);
        }

        [TestMethod]
        public void LoadsSessionFilesOfAllPickleProtocols()
        {
            foreach (int protocol in new[] { 0, 2, 4, 5 })
            {
                using (var f = File.OpenRead(TestData.Path("session_protocol" + protocol + ".pickle")))
                    AssertSession(Pickle.LoadStringDict(f));
            }
        }

        [TestMethod]
        public void DumpThenLoadRoundTrips()
        {
            var ms = new MemoryStream();
            Pickle.DumpStringDict(Expected(), ms);
            ms.Position = 0;
            AssertSession(Pickle.LoadStringDict(ms));
        }

        [TestMethod]
        public void DumpWritesProtocol4LikePython()
        {
            var ms = new MemoryStream();
            Pickle.DumpStringDict(Expected(), ms);
            byte[] ours = ms.ToArray();
            byte[] python = TestData.Bytes("session_protocol4.pickle");
            CollectionAssert.AreEqual(python, ours);
        }

        [TestMethod]
        [ExpectedException(typeof(EndOfStreamException))]
        public void TruncatedPickleFails()
        {
            byte[] data = TestData.Bytes("session_protocol4.pickle");
            Pickle.Load(new MemoryStream(data, 0, data.Length / 2));
        }
    }
}
