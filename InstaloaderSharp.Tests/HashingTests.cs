using System;
using System.Text;
using InstaloaderSharp.Util;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace InstaloaderSharp.Tests
{
    [TestClass]
    public class HashingTests
    {
        private static string Hex(byte[] bytes)
        {
            return BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
        }

        private static string UrlsafeBase64(byte[] bytes)
        {
            return Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_');
        }

        [TestMethod]
        public void Blake2bMatchesRfc7693()
        {
            Assert.AreEqual("ba80a53f981c4d0d6a2797b69f12f6e94c212f14685ac4b74b12bb6fdbffa2d1" +
                            "7d87c5392aab792dc252d5de4533cc9518d38aa8dbf1925ab92386edd4009923",
                            Hex(Blake2b.Hash(Encoding.ASCII.GetBytes("abc"), 64)));
        }

        [TestMethod]
        public void Blake2bWithSmallDigestMatchesPython()
        {
            // Values of base64.urlsafe_b64encode(hashlib.blake2b(data, digest_size=6).digest())
            Assert.AreEqual("3dnEB2f5", UrlsafeBase64(Blake2b.Hash(new byte[0], 6)));
            Assert.AreEqual("E8lJfCQl", UrlsafeBase64(Blake2b.Hash(Encoding.ASCII.GetBytes("abc"), 6)));
            Assert.AreEqual("iGJ4XAIO", UrlsafeBase64(Blake2b.Hash(Encoding.ASCII.GetBytes(new string('x', 128)), 6)));
            Assert.AreEqual("LvX6YUK6", UrlsafeBase64(Blake2b.Hash(Encoding.ASCII.GetBytes(new string('y', 129)), 6)));
            Assert.AreEqual("4L20n736", UrlsafeBase64(Blake2b.Hash(Encoding.ASCII.GetBytes(new string('z', 256)), 6)));
        }

        [TestMethod]
        public void Blake2bIncrementalUpdateEqualsOneShot()
        {
            byte[] data = Encoding.ASCII.GetBytes(new string('q', 1000));
            var b = new Blake2b(32);
            b.Update(data, 0, 1);
            b.Update(data, 1, 127);
            b.Update(data, 128, 872);
            Assert.AreEqual(Hex(Blake2b.Hash(data, 32)), Hex(b.Digest()));
        }

        [TestMethod]
        public void CrcCheckValues()
        {
            byte[] check = Encoding.ASCII.GetBytes("123456789");
            Assert.AreEqual(0xCBF43926u, Crc32.Compute(check));
            Assert.AreEqual(0x995DC9BBDF1939FAUL, Crc64.Update(0, check, 0, check.Length));
        }
    }
}
