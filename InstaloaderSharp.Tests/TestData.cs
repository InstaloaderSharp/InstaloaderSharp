using System;
using System.IO;
using System.Text;
using InstaloaderSharp.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace InstaloaderSharp.Tests
{
    /// <summary>Access to the reference files in TestData (created with Instaloader 4.15.4).</summary>
    internal static class TestData
    {
        public static string Path(string name)
        {
            string dir = System.IO.Path.GetDirectoryName(typeof(TestData).Assembly.Location);
            return System.IO.Path.Combine(dir, "TestData", name);
        }

        public static byte[] Bytes(string name)
        {
            return File.ReadAllBytes(Path(name));
        }

        public static string Text(string name)
        {
            return Encoding.UTF8.GetString(Bytes(name));
        }

        /// <summary>Asserts that two files have the same bytes; on failure, shows where they first differ.</summary>
        public static void AssertSameBytes(byte[] expected, byte[] actual)
        {
            int length = Math.Min(expected.Length, actual.Length);
            int i = 0;
            while (i < length && expected[i] == actual[i])
                i++;
            if (i == length && expected.Length == actual.Length)
                return;
            int start = Math.Max(0, i - 20);
            Assert.Fail("Files differ at byte {0} (lengths {1} and {2}). Expected ...{3}..., actual ...{4}...", i,
                        expected.Length, actual.Length, Excerpt(expected, start), Excerpt(actual, start));
        }

        private static string Excerpt(byte[] data, int start)
        {
            int count = Math.Max(0, Math.Min(60, data.Length - start));
            return Encoding.UTF8.GetString(data, start, count).Replace("\r", "\\r").Replace("\n", "\\n");
        }

        /// <summary>A new, empty temporary directory, deleted with the returned object.</summary>
        public static TempDirectory TempDirectory()
        {
            return new TempDirectory();
        }

        /// <summary>A context that never sleeps between requests and makes no requests in the tests.</summary>
        public static InstaloaderContext Context()
        {
            return new InstaloaderContext(sleep: false, quiet: true);
        }

        /// <summary>The post node saved in post.json.</summary>
        public static JObject PostNode()
        {
            var structure = (JObject)PyJson.Loads(Text("post.json"));
            return (JObject)structure["node"];
        }
    }

    internal sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "InstaloaderSharp.Tests." + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; private set; }

        public string File(string name)
        {
            return System.IO.Path.Combine(Path, name);
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, true);
            }
            catch (IOException)
            {
            }
        }
    }
}
