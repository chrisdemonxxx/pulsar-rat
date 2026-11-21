using Microsoft.VisualStudio.TestTools.UnitTesting;
using Pulsar.Common.Messages;
using Pulsar.Common.Models;
using Pulsar.Common.Networking;
using System.Collections.Generic;

namespace Pulsar.Common.Tests.Messages
{
    [TestClass]
    public class PasswordRecoveryTests
    {
        [TestMethod]
        public void RoundTrip_GetPasswordsResponse_PreservesSourcesAndBounds()
        {
            var original = new GetPasswordsResponse
            {
                RecoveredAccounts = new List<RecoveredAccount>
                {
                    new RecoveredAccount
                    {
                        Application = "FixtureBrowser",
                        Url = "https://example.test/",
                        Username = "fixture-user",
                        Password = "fixture-only"
                    }
                },
                Sources = new List<PasswordRecoverySource>
                {
                    new PasswordRecoverySource
                    {
                        Application = "FixtureBrowser",
                        Profile = "Default",
                        Status = PasswordRecoveryStatus.Locked,
                        Detail = "fixture lock state",
                        AccountCount = 0
                    }
                },
                Truncated = true,
                Error = null
            };

            var payload = PulsarMessagePackSerializer.Serialize(original);
            var result = PulsarMessagePackSerializer.Deserialize(payload) as GetPasswordsResponse;

            Assert.IsNotNull(result);
            Assert.IsTrue(result.Truncated);
            Assert.AreEqual(1, result.RecoveredAccounts.Count);
            Assert.AreEqual("FixtureBrowser", result.RecoveredAccounts[0].Application);
            Assert.AreEqual(1, result.Sources.Count);
            Assert.AreEqual(PasswordRecoveryStatus.Locked, result.Sources[0].Status);
            Assert.AreEqual("Default", result.Sources[0].Profile);
        }

        [TestMethod]
        public void PasswordRecoveryStatus_CoversExplicitStates()
        {
            Assert.AreEqual(PasswordRecoveryStatus.Supported, PasswordRecoveryStatus.Supported);
            Assert.AreEqual(PasswordRecoveryStatus.Unsupported, PasswordRecoveryStatus.Unsupported);
            Assert.AreEqual(PasswordRecoveryStatus.Locked, PasswordRecoveryStatus.Locked);
            Assert.AreEqual(PasswordRecoveryStatus.Empty, PasswordRecoveryStatus.Empty);
            Assert.AreEqual(PasswordRecoveryStatus.Failed, PasswordRecoveryStatus.Failed);
        }
    }
}
