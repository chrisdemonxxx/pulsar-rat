#if NET9_0_OR_GREATER
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Pulsar.Server.Forms;
using Pulsar.Server.Statistics;
using System;
using System.Collections.Generic;

namespace Pulsar.Common.Tests.Server
{
    [TestClass]
    public class TopLevelTabsCompletionTests
    {
        [TestMethod]
        public void ListeningStatusHandlesNoPortsAndPortOverflow()
        {
            Assert.AreEqual("Not listening.", FrmMain.ResolveListeningStatus(false, new ushort[] { 4782 }));
            Assert.AreEqual("Listening.", FrmMain.ResolveListeningStatus(true, Array.Empty<ushort>()));
            Assert.AreEqual(
                "Listening on ports 4782, 4783, 4784 and 2 more ports.",
                FrmMain.ResolveListeningStatus(true, new ushort[] { 4782, 4783, 4784, 4785, 4786 }));
        }

        [TestMethod]
        public void NotificationStatusTextIsDeterministic()
        {
            Assert.AreEqual("You're all caught up. No notifications yet.", FrmMain.ResolveNotificationStatusText(0, 0));
            Assert.AreEqual("You're all caught up. 3 notifications logged.", FrmMain.ResolveNotificationStatusText(0, 3));
            Assert.AreEqual("Pending notifications: 2 of 5", FrmMain.ResolveNotificationStatusText(2, 5));
            Assert.AreEqual("Pending notifications: 4", FrmMain.ResolveNotificationStatusText(4, 4));
        }

        [TestMethod]
        public void CryptoConfigurationParserNormalizesAddressKeys()
        {
            const string json = """
            {
              "Addresses": {
                "btc": "bc1-example",
                "ETH": "0x-example"
              },
              "ClipperEnabled": true
            }
            """;

            Assert.IsTrue(FrmMain.TryParseCryptoAddressConfiguration(
                json,
                out var addresses,
                out var enabled));
            Assert.IsTrue(enabled);
            Assert.AreEqual("bc1-example", addresses["BTC"]);
            Assert.AreEqual("0x-example", addresses["ETH"]);
        }

        [TestMethod]
        public void CryptoConfigurationParserRejectsInvalidJson()
        {
            Assert.IsFalse(FrmMain.TryParseCryptoAddressConfiguration(
                "{not-json",
                out _,
                out _));
        }

        [TestMethod]
        public void StatisticsServiceHandlesEmptyAndPopulatedRecords()
        {
            var now = DateTime.UtcNow;
            var records = new[]
            {
                new Pulsar.Server.Persistence.OfflineClientRecord
                {
                    ClientId = "one",
                    Country = "United States",
                    OperatingSystem = "Windows 11",
                    Tag = "lab",
                    FirstSeenUtc = now,
                    IsOnline = true
                }
            };

            var snapshot = ClientStatisticsService.CreateSnapshot(records);
            Assert.IsFalse(snapshot.HasError);
            Assert.AreEqual(1, snapshot.TotalClients);
            Assert.AreEqual(1, snapshot.OnlineClients);
            Assert.AreEqual(0, snapshot.OfflineClients);
            Assert.AreEqual(1, snapshot.NewClientsLast7Days);

            var empty = ClientStatisticsService.CreateSnapshot(Array.Empty<Pulsar.Server.Persistence.OfflineClientRecord>());
            Assert.AreEqual(0, empty.TotalClients);
            Assert.IsFalse(empty.HasError);
        }

        [TestMethod]
        public void MainShellPreviewAndSearchRemainStable()
        {
            Assert.AreEqual(
                FrmMain.MainShellPreviewState.Ready,
                FrmMain.ResolveMainShellPreviewState(1, true));
            Assert.AreEqual(
                FrmMain.MainShellPreviewState.Empty,
                FrmMain.ResolveMainShellPreviewState(1, false));
            Assert.AreEqual(
                FrmMain.MainShellPreviewState.Multiple,
                FrmMain.ResolveMainShellPreviewState(2, true));

            Assert.IsTrue(FrmMain.MainShellSearchMatches("alice", new[] { "Alice-PC", "192.0.2.10" }));
            Assert.IsFalse(FrmMain.MainShellSearchMatches("bob", new[] { "Alice-PC", "192.0.2.10" }));
        }
    }
}
#endif
