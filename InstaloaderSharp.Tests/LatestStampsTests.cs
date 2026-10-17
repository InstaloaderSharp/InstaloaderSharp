using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace InstaloaderSharp.Tests
{
    [TestClass]
    public class LatestStampsTests
    {
        private static readonly DateTimeOffset PostTimestamp =
            new DateTimeOffset(2024, 5, 6, 7, 8, 9, TimeSpan.FromHours(2)).AddTicks(1234560);

        [TestMethod]
        public void ReadsFileWrittenByPython()
        {
            var stamps = new LatestStamps(TestData.Path("latest-stamps.ini"));
            Assert.AreEqual(25025320L, stamps.GetProfileId("someone"));
            Assert.AreEqual("pic_n.jpg", stamps.GetProfilePic("someone"));
            Assert.AreEqual(PostTimestamp, stamps.GetLastPostTimestamp("someone"));
        }

        [TestMethod]
        public void WritesTheSameFileAsPython()
        {
            using (TempDirectory dir = TestData.TempDirectory())
            {
                string file = dir.File("sub\\latest-stamps.ini");
                var stamps = new LatestStamps(file);
                stamps.SaveProfileId("someone", 25025320);
                stamps.SetProfilePic("someone", "pic_n.jpg");
                stamps.SetLastPostTimestamp("someone", PostTimestamp);
                TestData.AssertSameBytes(TestData.Bytes("latest-stamps.ini"), File.ReadAllBytes(file));
            }
        }

        [TestMethod]
        public void MissingValuesHaveDefaults()
        {
            using (TempDirectory dir = TestData.TempDirectory())
            {
                var stamps = new LatestStamps(dir.File("none.ini"));
                Assert.IsNull(stamps.GetProfileId("nobody"));
                Assert.AreEqual("", stamps.GetProfilePic("nobody"));
                Assert.AreEqual(new DateTimeOffset(1970, 1, 1, 0, 0, 0, TimeSpan.Zero), stamps.GetLastStoryTimestamp("nobody"));
                Assert.IsFalse(File.Exists(dir.File("none.ini")));
            }
        }

        [TestMethod]
        public void RenameProfileMovesValues()
        {
            using (TempDirectory dir = TestData.TempDirectory())
            {
                var stamps = new LatestStamps(dir.File("s.ini"));
                stamps.SaveProfileId("old", 5);
                stamps.SetLastTaggedTimestamp("old", PostTimestamp);
                stamps.RenameProfile("old", "new");
                var reloaded = new LatestStamps(dir.File("s.ini"));
                Assert.IsNull(reloaded.GetProfileId("old"));
                Assert.AreEqual(5L, reloaded.GetProfileId("new"));
                Assert.AreEqual(PostTimestamp, reloaded.GetLastTaggedTimestamp("new"));
            }
        }

        [TestMethod]
        public void ConfigParserDialect()
        {
            var parser = new ConfigParser();
            parser.ReadString("# comment\n[Section]\nKey : value\n  continued\n; other comment\npercent = 100%%\nref = %(key)s!\n");
            Assert.AreEqual("value\ncontinued", parser.Get("Section", "key"));
            Assert.AreEqual("100%", parser.Get("Section", "percent"));
            Assert.AreEqual("value\ncontinued!", parser.Get("Section", "ref"));
            Assert.AreEqual("[Section]\nkey = value\n\tcontinued\npercent = 100%%\nref = %(key)s!\n\n", parser.Write());
        }
    }
}
