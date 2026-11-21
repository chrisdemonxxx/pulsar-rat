using Microsoft.VisualStudio.TestTools.UnitTesting;
using Pulsar.Common.Messages;
using Pulsar.Common.Networking;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Pulsar.Common.Tests.Networking
{
    [TestClass]
    public class MessageWaiterRegistryTests
    {
        [TestMethod]
        public async Task TimedOutWaiter_DoesNotConsumeNextResponse()
        {
            using (var registry = new MessageWaiterRegistry())
            {
                var expired = registry.WaitForAsync<SetStatus>(TimeSpan.FromMilliseconds(20));
                Assert.IsNull(await expired);

                var current = registry.WaitForAsync<SetStatus>(TimeSpan.FromSeconds(2));
                var response = new SetStatus { Message = "fresh" };

                Assert.IsTrue(registry.Dispatch(response));
                Assert.AreSame(response, await current);
            }
        }

        [TestMethod]
        public async Task Dispatch_UsesCorrelationPredicate()
        {
            using (var registry = new MessageWaiterRegistry())
            {
                var pending = registry.WaitForAsync<SetStatus>(
                    TimeSpan.FromSeconds(2),
                    message => message.Message == "expected");

                Assert.IsFalse(registry.Dispatch(new SetStatus { Message = "other" }));

                var expected = new SetStatus { Message = "expected" };
                Assert.IsTrue(registry.Dispatch(expected));
                Assert.AreSame(expected, await pending);
            }
        }

        [TestMethod]
        public async Task CancelAll_CompletesOutstandingWaiters()
        {
            using (var registry = new MessageWaiterRegistry())
            {
                var pending = registry.WaitForAsync<SetStatus>(Timeout.InfiniteTimeSpan);
                registry.CancelAll();

                Assert.IsNull(await pending);
            }
        }
    }
}
