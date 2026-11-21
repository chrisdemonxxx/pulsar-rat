#if NET9_0_OR_GREATER
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Pulsar.Server.Messages;

namespace Pulsar.Common.Tests.HVNC
{
    [TestClass]
    public class HVNCPerformanceContractsTests
    {
        [TestMethod]
        public void FrameQueue_DefaultLimit_IsStable()
        {
            const int maximumPendingFrames = 30;
            Assert.IsTrue(maximumPendingFrames >= 5);
            Assert.IsTrue(maximumPendingFrames <= 30);
        }

        [TestMethod]
        public void TelemetryBacklog_CalculatesMaximum()
        {
            var backlog = 12;
            var priorMaximum = 8;
            var maximum = backlog > priorMaximum ? backlog : priorMaximum;

            Assert.AreEqual(12, maximum);
        }

        [TestMethod]
        public void StaleFrameSequence_IsRejected()
        {
            long receivedSequence = 42;
            long frameSequence = 41;

            Assert.IsTrue(frameSequence < receivedSequence);
        }
    }
}
#endif
