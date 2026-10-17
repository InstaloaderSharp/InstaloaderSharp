using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using InstaloaderSharp.Compression;
using InstaloaderSharp.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace InstaloaderSharp.Tests
{
    [TestClass]
    public class NodeIteratorTests
    {
        /// <summary>The same iterator as in generate.py; the first page is given, so no request is made.</summary>
        private static NodeIterator<JObject> CreateIterator(InstaloaderContext context, bool hasNextPage = true)
        {
            var edges = new JArray();
            for (int i = 0; i < 5; i++)
                edges.Add(new JObject { { "node", new JObject { { "id", i }, { "f", 1.5 } } } });
            var firstData = new JObject
            {
                { "edges", edges },
                { "page_info", new JObject { { "has_next_page", hasNextPage }, { "end_cursor", "CUR" } } }
            };
            var variables = new JObject
            {
                { "data", new JObject { { "count", 12 }, { "include", true } } },
                { "username", ((char)0xE9) + "x" }
            };
            return new NodeIterator<JObject>(context, null, d => d, n => n, variables, "https://www.instagram.com/abc/",
                                             firstData, null, "999");
        }

        [TestMethod]
        public void MagicMatchesPython()
        {
            Assert.AreEqual("BeMcKw-D", CreateIterator(TestData.Context()).Magic);
        }

        [TestMethod]
        public void ThawsResumeFileWrittenByPython()
        {
            InstaloaderContext context = TestData.Context();
            var frozen = (FrozenNodeIterator)Structures.LoadStructureFromFile(context, TestData.Path("resume.json.xz"));
            NodeIterator<JObject> iterator = CreateIterator(context);
            iterator.Thaw(frozen);
            Assert.AreEqual(1L, iterator.TotalIndex);
            Assert.IsTrue(iterator.MoveNext());
            Assert.AreEqual(1L, (long)iterator.Current["id"]);
            Assert.AreEqual(0L, (long)iterator.FirstItem["id"]);
        }

        [TestMethod]
        public void FreezeWritesTheSameResumeInformationAsPython()
        {
            NodeIterator<JObject> iterator = CreateIterator(TestData.Context());
            iterator.MoveNext();
            iterator.MoveNext();
            JObject ours = Structures.GetJsonStructure(iterator.Freeze());
            var python = (JObject)PyJson.Loads(Encoding.ASCII.GetString(Xz.DecompressFile(TestData.Path("resume.json.xz"))));
            // best_before depends on the time of freezing
            ((JObject)ours["node"]).Remove("best_before");
            ((JObject)python["node"]).Remove("best_before");
            Assert.AreEqual(PyJson.Dumps(python), PyJson.Dumps(ours));
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidArgumentException))]
        public void ThawRejectsMismatchingIterator()
        {
            InstaloaderContext context = TestData.Context();
            var frozen = (FrozenNodeIterator)Structures.LoadStructureFromFile(context, TestData.Path("resume.json.xz"));
            frozen.DocId = "other";
            CreateIterator(context).Thaw(frozen);
        }

        [TestMethod]
        public void IteratorWithoutEdgesIsEmpty()
        {
            var iterator = new NodeIterator<JObject>(TestData.Context(), "hash", d => d, n => n,
                                                     firstData: new JObject { { "count", 0 } });
            Assert.IsFalse(iterator.MoveNext());
        }

        [TestMethod]
        public void ResumableIterationSavesOnInterruptAndResumes()
        {
            using (TempDirectory dir = TestData.TempDirectory())
            {
                InstaloaderContext context = TestData.Context();
                Func<string, string> formatPath = magic => dir.File("resume_" + magic + ".json.xz");
                Func<InstaloaderContext, string, object> load = (c, p) => Structures.LoadStructureFromFile(c, p);
                Action<FrozenNodeIterator, string> save = (f, p) => Structures.SaveStructureToFile(f, p);

                // First run: interrupted after two items
                NodeIterator<JObject> first = CreateIterator(context, hasNextPage: false);
                try
                {
                    NodeIteration.ResumableIteration(context, first, load, save, formatPath, true, true, (resuming, start) =>
                    {
                        Assert.IsFalse(resuming);
                        int n = 0;
                        foreach (JObject item in first)
                            if (++n == 2)
                                throw new KeyboardInterruptException();
                    });
                    Assert.Fail();
                }
                catch (KeyboardInterruptException)
                {
                }
                string resumeFile = formatPath(first.Magic);
                Assert.IsTrue(File.Exists(resumeFile));

                // Second run: resumes with the second item and deletes the resume file when complete
                NodeIterator<JObject> second = CreateIterator(context, hasNextPage: false);
                var ids = new List<long>();
                NodeIteration.ResumableIteration(context, second, load, save, formatPath, true, true, (resuming, start) =>
                {
                    Assert.IsTrue(resuming);
                    Assert.AreEqual(1L, start);
                    foreach (JObject item in second)
                        ids.Add((long)item["id"]);
                });
                CollectionAssert.AreEqual(new List<long> { 1, 2, 3, 4 }, ids);
                Assert.IsFalse(File.Exists(resumeFile));
            }
        }
    }
}
