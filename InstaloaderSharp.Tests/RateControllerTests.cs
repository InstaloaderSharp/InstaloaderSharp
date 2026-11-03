using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace InstaloaderSharp.Tests
{
    [TestClass]
    public class RateControllerTests
    {
        /// <summary>Records the waits instead of sleeping.</summary>
        private sealed class RecordingRateController : RateController
        {
            public readonly List<double> Waits = new List<double>();

            public RecordingRateController(InstaloaderContext context) : base(context) { }

            public override void Sleep(double secs)
            {
                Waits.Add(secs);
            }
        }

        private static RecordingRateController Run(bool loggedIn, string queryType, int queries)
        {
            RecordingRateController controller = null;
            var context = new InstaloaderContext(sleep: false, quiet: true,
                                                 rateController: c => controller = new RecordingRateController(c));
            if (loggedIn)
                context.Username = "someuser";
            for (int i = 0; i < queries; i++)
                controller.WaitBeforeQuery(queryType);
            return controller;
        }

        [TestMethod]
        public void LoggedInQueriesOfOneTypeWaitAfterHalfOfInstaloadersLimit()
        {
            Assert.AreEqual(0, Run(true, "12345", 100).Waits.Count);
            RecordingRateController controller = Run(true, "12345", 101);
            Assert.AreEqual(1, controller.Waits.Count);
            Assert.IsTrue(controller.Waits[0] > 600);
        }

        [TestMethod]
        public void LoggedOutQueriesKeepInstaloadersLimit()
        {
            Assert.AreEqual(0, Run(false, "12345", 200).Waits.Count);
            Assert.AreEqual(1, Run(false, "12345", 201).Waits.Count);
        }

        [TestMethod]
        public void LoggedInAppQueriesWaitAfterHundred()
        {
            Assert.AreEqual(0, Run(true, "iphone", 100).Waits.Count);
            RecordingRateController controller = Run(true, "iphone", 101);
            Assert.AreEqual(1, controller.Waits.Count);
            Assert.IsTrue(controller.Waits[0] > 1800);
        }

        [TestMethod]
        public void LoggedInGraphqlQueriesOfAllTypesWaitAfterHundredFifty()
        {
            RecordingRateController controller = null;
            var context = new InstaloaderContext(sleep: false, quiet: true,
                                                 rateController: c => controller = new RecordingRateController(c));
            context.Username = "someuser";
            for (int i = 0; i < 90; i++)
                controller.WaitBeforeQuery("a");
            for (int i = 0; i < 60; i++)
                controller.WaitBeforeQuery("b");
            Assert.AreEqual(0, controller.Waits.Count);
            controller.WaitBeforeQuery("c");
            Assert.AreEqual(1, controller.Waits.Count);
            Assert.IsTrue(controller.Waits[0] > 590);
        }
    }
}
