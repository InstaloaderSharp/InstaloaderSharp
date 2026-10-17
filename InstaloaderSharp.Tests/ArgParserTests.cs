using System;
using System.Collections.Generic;
using System.IO;
using InstaloaderSharp.Cli;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace InstaloaderSharp.Tests
{
    [TestClass]
    public class ArgParserTests
    {
        private static Dictionary<string, object> Parse(params string[] args)
        {
            return Program.CreateParser().Parse(args);
        }

        private static int ExitCode(params string[] args)
        {
            TextWriter stdout = Console.Out, stderr = Console.Error;
            Console.SetOut(TextWriter.Null);
            Console.SetError(TextWriter.Null);
            try
            {
                Parse(args);
                return -1;
            }
            catch (ArgParserExit exit)
            {
                return exit.Code;
            }
            finally
            {
                Console.SetOut(stdout);
                Console.SetError(stderr);
            }
        }

        [TestMethod]
        public void Defaults()
        {
            Dictionary<string, object> a = Parse();
            Assert.AreEqual(false, a["fast_update"]);
            Assert.AreEqual(3L, a["max_connection_attempts"]);
            Assert.AreEqual(300.0, a["request_timeout"]);
            Assert.IsNull(a["login"]);
            Assert.AreEqual(0, ((List<string>)a["profile"]).Count);
        }

        [TestMethod]
        public void OptionSpellings()
        {
            Assert.AreEqual(true, Parse("--fast")["fast_update"], "unique prefix");
            Assert.AreEqual("user", Parse("-luser")["login"]);
            Assert.AreEqual("user", Parse("-l", "user")["login"]);
            Assert.AreEqual("user", Parse("--login=user")["login"]);
            Assert.AreEqual("user", Parse("--log", "user")["login"]);
            Dictionary<string, object> combined = Parse("-sF");
            Assert.AreEqual(true, combined["stories"]);
            Assert.AreEqual(true, combined["fast_update"]);
            Assert.AreEqual("viewer_has_liked", Parse("--only-if=viewer_has_liked")["post_filter"]);
        }

        [TestMethod]
        public void TypedAndRepeatedOptions()
        {
            Dictionary<string, object> a = Parse("--max-connection-attempts", "5", "--request-timeout=7.5",
                                                  "--abort-on", "302,429", "--post-metadata-txt", "a", "--post-metadata-txt=b");
            Assert.AreEqual(5L, a["max_connection_attempts"]);
            Assert.AreEqual(7.5, a["request_timeout"]);
            CollectionAssert.AreEqual(new List<int> { 302, 429 }, (List<int>)a["abort_on"]);
            CollectionAssert.AreEqual(new List<object> { "a", "b" }, (List<object>)a["post_metadata_txt"]);
        }

        [TestMethod]
        public void OptionalValueOfLatestStampsConsumesNextArgumentLikeArgparse()
        {
            Dictionary<string, object> a = Parse("--latest-stamps", "profile");
            Assert.AreEqual("profile", a["latest_stamps"]);
            Assert.AreEqual(0, ((List<string>)a["profile"]).Count);
            Dictionary<string, object> b = Parse("profile", "--latest-stamps");
            Assert.AreEqual(Instaloader.GetDefaultStampsFilename(), b["latest_stamps"]);
            CollectionAssert.AreEqual(new List<string> { "profile" }, (List<string>)b["profile"]);
        }

        [TestMethod]
        public void TargetsAfterDoubleDash()
        {
            Dictionary<string, object> a = Parse("-F", "profile1", "--", "-SHORTCODE", "--not-an-option");
            CollectionAssert.AreEqual(new List<string> { "profile1", "-SHORTCODE", "--not-an-option" }, (List<string>)a["profile"]);
        }

        [TestMethod]
        public void ArgumentsFromFile()
        {
            using (TempDirectory dir = TestData.TempDirectory())
            {
                File.WriteAllText(dir.File("args.txt"), "--login=myname\r\n--fast-update\r\nprofile one\r\n");
                Dictionary<string, object> a = Parse("+" + dir.File("args.txt"), "profile2");
                Assert.AreEqual("myname", a["login"]);
                Assert.AreEqual(true, a["fast_update"]);
                CollectionAssert.AreEqual(new List<string> { "profile one", "profile2" }, (List<string>)a["profile"]);
            }
        }

        [TestMethod]
        public void ErrorsExitWithCode2()
        {
            Assert.AreEqual(2, ExitCode("--bogus"));
            Assert.AreEqual(2, ExitCode("--no", "x"), "ambiguous prefix");
            Assert.AreEqual(2, ExitCode("--count"), "missing value");
            Assert.AreEqual(2, ExitCode("--max-connection-attempts", "x"), "invalid int");
            Assert.AreEqual(2, ExitCode("--abort-on", "99"), "invalid status code");
            Assert.AreEqual(2, ExitCode("-s=x"), "value for a flag");
        }

        [TestMethod]
        public void HelpAndVersionExitWithCode0()
        {
            Assert.AreEqual(0, ExitCode("--help"));
            Assert.AreEqual(0, ExitCode("--version"));
        }

        [TestMethod]
        public void HelpTextIsWrappedLikeArgparse()
        {
            string old = Environment.GetEnvironmentVariable("COLUMNS");
            Environment.SetEnvironmentVariable("COLUMNS", "120");
            try
            {
                string help = Program.CreateParser().FormatHelp().Replace("\r\n", "\n");
                StringAssert.Contains(help,
                    "  --no-pictures         Do not download post pictures. Cannot be used together with --fast-update. Implies --no-video-\n" +
                    "                        thumbnails, does not imply --no-videos.\n");
                StringAssert.Contains(help, "  -l, --login YOUR-USERNAME\n                        Login name (profile name) for your Instagram account.\n");
            }
            finally
            {
                Environment.SetEnvironmentVariable("COLUMNS", old);
            }
        }
    }
}
