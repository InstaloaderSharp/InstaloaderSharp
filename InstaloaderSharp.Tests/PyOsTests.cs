using System.Collections.Generic;
using InstaloaderSharp.Python;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace InstaloaderSharp.Tests
{
    [TestClass]
    public class PyOsTests
    {
        [TestMethod]
        public void JoinLikeNtpath()
        {
            // Expected values from Python's ntpath.join
            Assert.AreEqual("a\\b", PyOs.Join("a", "b"));
            Assert.AreEqual("a/b", PyOs.Join("a/", "b"));
            Assert.AreEqual("\\b", PyOs.Join("a", "\\b"));
            Assert.AreEqual("C:x", PyOs.Join("C:", "x"));
            Assert.AreEqual("D:y", PyOs.Join("C:\\x", "D:y"));
            Assert.AreEqual("C:\\b", PyOs.Join("C:\\x", "\\b"));
            Assert.AreEqual("a\\b\\c", PyOs.Join("a", "b", "c"));
            Assert.AreEqual("\\\\server\\share\\x", PyOs.Join("\\\\server\\share", "x"));
        }

        [TestMethod]
        public void SplitFunctionsLikeNtpath()
        {
            Assert.AreEqual("a/b", PyOs.DirName("a/b/c.txt"));
            Assert.AreEqual("c.txt", PyOs.BaseName("a/b/c.txt"));
            Assert.AreEqual("", PyOs.DirName("c.txt"));
            Assert.AreEqual("C:\\", PyOs.DirName("C:\\x"));
            string root, ext;
            PyOs.SplitExt("a/b.tar.gz", out root, out ext);
            Assert.AreEqual("a/b.tar", root);
            Assert.AreEqual(".gz", ext);
            PyOs.SplitExt(".hidden", out root, out ext);
            Assert.AreEqual(".hidden", root);
            Assert.AreEqual("", ext);
            PyOs.SplitExt("a.b\\c", out root, out ext);
            Assert.AreEqual("a.b\\c", root);
            Assert.AreEqual("", ext);
        }

        [TestMethod]
        public void TextWrapBreaksAtHyphensLikePython()
        {
            const string text = "Do not download post pictures. Cannot be used together with --fast-update. " +
                                "Implies --no-video-thumbnails, does not imply --no-videos.";
            CollectionAssert.AreEqual(
                new List<string>
                {
                    "Do not download post pictures. Cannot be used together with --fast-update. Implies --no-video-",
                    "thumbnails, does not imply --no-videos."
                },
                TextWrap.Wrap(text, 94));
            CollectionAssert.AreEqual(new List<string> { "abcdefghij", "klm" }, TextWrap.Wrap("abcdefghijklm", 10));
        }

        [TestMethod]
        public void PythonStringHelpers()
        {
            string emoji = char.ConvertFromUtf32(0x1F600);
            Assert.AreEqual(3, PyStr.Len("a" + emoji + "b"));
            Assert.AreEqual(emoji + "b", PyStr.Slice("a" + emoji + "b", 1, null));
            CollectionAssert.AreEqual(new List<string> { "a", "b", "", "c" }, PyStr.SplitLines("a\r\nb\n\nc"));
            Assert.AreEqual("\"it's\"", PyStr.Repr("it's"));
            Assert.AreEqual("'a\\nb\\x00'", PyStr.Repr("a\nb\0"));
            Assert.AreEqual(-1, PyStr.CompareCodePoints(((char)0xFFFF).ToString(), emoji), "code points, not UTF-16 units");
        }
    }
}
