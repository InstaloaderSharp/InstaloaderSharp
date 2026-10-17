using System;
using System.Collections.Generic;
using System.Linq;
using InstaloaderSharp.Cli;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace InstaloaderSharp.Tests
{
    [TestClass]
    public class SqliteAndCryptoTests
    {
        [TestMethod]
        public void ReadsCookieDatabaseIncludingWriteAheadLog()
        {
            List<Dictionary<string, object>> rows = new SqliteReader(TestData.Path("cookies.sqlite")).ReadTable("moz_cookies").ToList();
            Assert.AreEqual(302, rows.Count);
            Dictionary<string, object> session = rows.Single(r => (string)r["name"] == "sessionid");
            Assert.AreEqual(3003, ((string)session["value"]).Length, "value stored on overflow pages");
            StringAssert.EndsWith((string)session["value"], "END");
            Assert.AreEqual(301L, session["id"], "INTEGER PRIMARY KEY column is the rowid");
            Assert.AreEqual("from-wal", rows.Single(r => (string)r["name"] == "csrftoken")["value"], "row only in the WAL");
            Assert.AreEqual("updated", rows.Single(r => (string)r["name"] == "n5")["value"], "row updated in the WAL");
            Dictionary<string, object> n7 = rows.Single(r => (string)r["name"] == "n7");
            Assert.AreEqual(1700000007L, n7["expiry"]);
            Assert.AreEqual(".example0.com", n7["host"]);
        }

        private static byte[] Hex(string hex)
        {
            return Enumerable.Range(0, hex.Length / 2).Select(i => Convert.ToByte(hex.Substring(2 * i, 2), 16)).ToArray();
        }

        private const string Plaintext =
            "d9313225f88406e5a55909c5aff5269a86a7a9531534f7da2e4c303d8a318a721c3c0c95956809532fcf0e2449a6b525b16aedf5aa0de657ba637b391aafd255";

        [TestMethod]
        public void AesGcmDecryptsGcmSpecificationTestVectors()
        {
            // Test cases 3 (AES-128) and 15 (AES-256) of "The Galois/Counter Mode of Operation (GCM)"
            byte[] nonce = Hex("cafebabefacedbaddecaf888");
            byte[] result3 = AesGcm.Decrypt(Hex("feffe9928665731c6d6a8f9467308308"), nonce,
                Hex("42831ec2217774244b7221b784d0d49ce3aa212f2c02a4e035c17e2329aca12e21d514b25466931c7d8f6a5aac84aa051ba30b396a0aac973d58e091473f5985"),
                Hex("4d5c2af327cd64a62cf35abd2ba6fab4"));
            CollectionAssert.AreEqual(Hex(Plaintext), result3);
            byte[] result15 = AesGcm.Decrypt(Hex("feffe9928665731c6d6a8f9467308308feffe9928665731c6d6a8f9467308308"), nonce,
                Hex("522dc1f099567d07f47f37a32a84427d643a8cdcbfe5c0c97598a2bd2555d1aa8cb08e48590dbb3da7b08b1056828838c5f61e6393ba7a0abcc9f662898015ad"),
                Hex("b094dac5d93471bdec1a502270e3cc6c"));
            CollectionAssert.AreEqual(Hex(Plaintext), result15);
        }

        [TestMethod]
        public void AesGcmRejectsWrongTag()
        {
            Assert.IsNull(AesGcm.Decrypt(Hex("feffe9928665731c6d6a8f9467308308"), Hex("cafebabefacedbaddecaf888"),
                                         Hex("42831ec2"), Hex("4d5c2af327cd64a62cf35abd2ba6fab4")));
        }
    }
}
