using System;
using System.Text;
using InstaloaderSharp.Compression;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace InstaloaderSharp.Tests
{
    [TestClass]
    public class CompressionTests
    {
        [TestMethod]
        public void DecompressesFilesWrittenByPython()
        {
            string expected = TestData.Text("text.json");
            foreach (string name in new[] { "text_nocheck.json.xz", "text_crc32.json.xz", "text_crc64.json.xz",
                                            "text_sha256.json.xz", "text_concatenated.xz", "text_legacy.lzma" })
                Assert.AreEqual(expected, Encoding.UTF8.GetString(Xz.DecompressFile(TestData.Path(name))), name);
        }

        private static void AssertRoundTrip(byte[] data, XzCheck check = XzCheck.None)
        {
            byte[] compressed = Xz.Compress(data, check);
            CollectionAssert.AreEqual(data, Xz.Decompress(compressed));
        }

        [TestMethod]
        public void RoundTripsTextAndRepetitiveData()
        {
            AssertRoundTrip(Encoding.UTF8.GetBytes(TestData.Text("text.json")));
            AssertRoundTrip(Encoding.ASCII.GetBytes(new string('a', 300000)));
            AssertRoundTrip(Encoding.ASCII.GetBytes("x"));
            AssertRoundTrip(new byte[0]);
        }

        [TestMethod]
        public void RoundTripsIncompressibleDataAcrossChunks()
        {
            var data = new byte[200000];
            new Random(42).NextBytes(data);
            AssertRoundTrip(data, XzCheck.Crc64);
            AssertRoundTrip(data, XzCheck.Crc32);
        }

        [TestMethod]
        public void RoundTripsMixedData()
        {
            // Alternating compressible and random blocks exercise compressed and stored LZMA2 chunks.
            var random = new Random(7);
            var sb = new System.IO.MemoryStream();
            for (int i = 0; i < 6; i++)
            {
                var block = new byte[50000];
                if (i % 2 == 0)
                    random.NextBytes(block);
                else
                    for (int j = 0; j < block.Length; j++)
                        block[j] = (byte)"abcabcabd"[j % 9];
                sb.Write(block, 0, block.Length);
            }
            AssertRoundTrip(sb.ToArray(), XzCheck.Sha256);
        }

        [TestMethod]
        public void CompressesText()
        {
            byte[] text = Encoding.UTF8.GetBytes(TestData.Text("text.json"));
            Assert.IsTrue(Xz.Compress(text).Length < text.Length / 3);
        }

        [TestMethod]
        [ExpectedException(typeof(LzmaException))]
        public void CorruptDataFails()
        {
            byte[] data = TestData.Bytes("text_crc64.json.xz");
            data[data.Length / 2] ^= 0xFF;
            Xz.Decompress(data);
        }

        [TestMethod]
        [ExpectedException(typeof(System.IO.EndOfStreamException))]
        public void TruncatedDataFails()
        {
            byte[] data = TestData.Bytes("text_nocheck.json.xz");
            Array.Resize(ref data, data.Length / 2);
            Xz.Decompress(data);
        }

        [TestMethod]
        [ExpectedException(typeof(LzmaException))]
        public void UnknownFormatFails()
        {
            Xz.Decompress(Encoding.ASCII.GetBytes("this is not compressed data"));
        }
    }
}
