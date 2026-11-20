using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Pulsar.Common.Enums;
using Pulsar.Common.Messages;
using Pulsar.Common.Messages.Administration.ReverseProxy;
using Pulsar.Common.Messages.Other;
using Pulsar.Common.Networking;

namespace Pulsar.Common.Tests.ReverseProxy
{
    [TestClass]
    public class ReverseProxyMessageTests
    {
        [TestMethod]
        public void RoundTrip_ReverseProxyConnect_PreservesAllFields()
        {
            var original = new ReverseProxyConnect
            {
                ConnectionId = 42,
                Target = "proxy.example.test",
                Port = 443,
                ListenerSessionId = "listener-session",
                TunnelId = "tunnel-42",
                Sequence = 1001,
                OriginClientId = "client-7"
            };

            var result = RoundTrip(original) as ReverseProxyConnect;

            Assert.IsNotNull(result);
            Assert.AreEqual(original.ConnectionId, result.ConnectionId);
            Assert.AreEqual(original.Target, result.Target);
            Assert.AreEqual(original.Port, result.Port);
            Assert.AreEqual(original.ListenerSessionId, result.ListenerSessionId);
            Assert.AreEqual(original.TunnelId, result.TunnelId);
            Assert.AreEqual(original.Sequence, result.Sequence);
            Assert.AreEqual(original.OriginClientId, result.OriginClientId);
        }

        [TestMethod]
        public void RoundTrip_ReverseProxyConnectResponse_PreservesAllFields()
        {
            var original = new ReverseProxyConnectResponse
            {
                ConnectionId = 43,
                IsConnected = true,
                LocalAddress = new byte[] { 127, 0, 0, 1 },
                LocalPort = 8080,
                HostName = "localhost",
                ListenerSessionId = "listener-session",
                TunnelId = "tunnel-43",
                Sequence = 1002,
                Status = ReverseProxyStatus.Connected,
                ErrorCode = "none",
                ErrorMessage = "fixture"
            };

            var result = RoundTrip(original) as ReverseProxyConnectResponse;

            Assert.IsNotNull(result);
            Assert.AreEqual(original.ConnectionId, result.ConnectionId);
            Assert.IsTrue(result.IsConnected);
            CollectionAssert.AreEqual(original.LocalAddress, result.LocalAddress);
            Assert.AreEqual(original.LocalPort, result.LocalPort);
            Assert.AreEqual(original.HostName, result.HostName);
            Assert.AreEqual(original.ListenerSessionId, result.ListenerSessionId);
            Assert.AreEqual(original.TunnelId, result.TunnelId);
            Assert.AreEqual(original.Sequence, result.Sequence);
            Assert.AreEqual(original.Status, result.Status);
            Assert.AreEqual(original.ErrorCode, result.ErrorCode);
            Assert.AreEqual(original.ErrorMessage, result.ErrorMessage);
        }

        [TestMethod]
        public void RoundTrip_ReverseProxyData_PreservesPayloadAndRoutingFields()
        {
            var original = new ReverseProxyData
            {
                ConnectionId = 44,
                Data = new byte[] { 0, 1, 2, 127, 128, 255 },
                ListenerSessionId = "listener-session",
                TunnelId = "tunnel-44",
                Sequence = 1003,
                OriginClientId = "client-7"
            };

            var result = RoundTrip(original) as ReverseProxyData;

            Assert.IsNotNull(result);
            Assert.AreEqual(original.ConnectionId, result.ConnectionId);
            CollectionAssert.AreEqual(original.Data, result.Data);
            Assert.AreEqual(original.ListenerSessionId, result.ListenerSessionId);
            Assert.AreEqual(original.TunnelId, result.TunnelId);
            Assert.AreEqual(original.Sequence, result.Sequence);
            Assert.AreEqual(original.OriginClientId, result.OriginClientId);
        }

        [TestMethod]
        public void RoundTrip_ReverseProxyDisconnect_PreservesStatusAndRoutingFields()
        {
            var original = new ReverseProxyDisconnect
            {
                ConnectionId = 45,
                ListenerSessionId = "listener-session",
                TunnelId = "tunnel-45",
                Sequence = 1004,
                OriginClientId = "client-7",
                Status = ReverseProxyStatus.HalfClosed,
                Reason = "peer closed"
            };

            var result = RoundTrip(original) as ReverseProxyDisconnect;

            Assert.IsNotNull(result);
            Assert.AreEqual(original.ConnectionId, result.ConnectionId);
            Assert.AreEqual(original.ListenerSessionId, result.ListenerSessionId);
            Assert.AreEqual(original.TunnelId, result.TunnelId);
            Assert.AreEqual(original.Sequence, result.Sequence);
            Assert.AreEqual(original.OriginClientId, result.OriginClientId);
            Assert.AreEqual(original.Status, result.Status);
            Assert.AreEqual(original.Reason, result.Reason);
        }

        [TestMethod]
        public void ReverseProxyAdditiveFields_RoundTripAsExplicitValues()
        {
            var original = new ReverseProxyData
            {
                ConnectionId = 46,
                Data = new byte[] { 9 },
                ListenerSessionId = "stable-listener",
                TunnelId = "stable-tunnel",
                Sequence = 77,
                OriginClientId = "stable-origin"
            };

            var first = (ReverseProxyData)RoundTrip(original);
            var repeated = (ReverseProxyData)RoundTrip(original);

            Assert.AreEqual(first.ListenerSessionId, repeated.ListenerSessionId);
            Assert.AreEqual(first.TunnelId, repeated.TunnelId);
            Assert.AreEqual(first.Sequence, repeated.Sequence);
            Assert.AreEqual(first.OriginClientId, repeated.OriginClientId);
        }

        [TestMethod]
        public void ReverseProxyStatus_DefaultsRemainWireCompatible()
        {
            Assert.AreEqual(ReverseProxyStatus.PendingConnect, new ReverseProxyConnectResponse().Status);
            Assert.AreEqual(ReverseProxyStatus.Stopped, new ReverseProxyDisconnect().Status);
        }

        private static IMessage RoundTrip(IMessage message)
        {
            var payload = PulsarMessagePackSerializer.Serialize(message);
            Assert.IsNotNull(payload);
            Assert.AreNotEqual(0, payload.Length);
            return PulsarMessagePackSerializer.Deserialize(payload);
        }
    }

    [TestClass]
    public class ReverseProxyValidationTests
    {
        [TestMethod]
        [DataRow("localhost", true)]
        [DataRow("127.0.0.1", true)]
        [DataRow("example.test", true)]
        [DataRow("", false)]
        [DataRow("   ", false)]
        [DataRow("bad\0target", false)]
        [DataRow("toolong", false)]
        public void IsValidTarget_AcceptsOnlyBoundedNonBlankTargets(string target, bool expected)
        {
            if (target == "toolong")
                target = new string('x', ReverseProxyValidation.MaxTargetLength + 1);

            Assert.AreEqual(expected, ReverseProxyValidation.IsValidTarget(target));
        }

        [TestMethod]
        public void IsValidTarget_AcceptsMaximumLengthAndRejectsPastMaximum()
        {
            Assert.IsTrue(ReverseProxyValidation.IsValidTarget(
                new string('x', ReverseProxyValidation.MaxTargetLength)));
            Assert.IsFalse(ReverseProxyValidation.IsValidTarget(
                new string('x', ReverseProxyValidation.MaxTargetLength + 1)));
        }

        [TestMethod]
        [DataRow(0, false)]
        [DataRow(-1, false)]
        [DataRow(1, true)]
        [DataRow(443, true)]
        [DataRow(65535, true)]
        [DataRow(65536, false)]
        [DataRow(int.MaxValue, false)]
        public void IsValidPort_AcceptsOnlyTcpPortRange(int port, bool expected)
        {
            Assert.AreEqual(expected, ReverseProxyValidation.IsValidPort(port));
        }

        [TestMethod]
        [DataRow(0, false)]
        [DataRow(-1, false)]
        [DataRow(1, true)]
        [DataRow(65536, true)]
        [DataRow(16 * 1024 * 1024, true)]
        [DataRow(16 * 1024 * 1024 + 1, false)]
        public void IsValidPayloadLength_AcceptsPositiveLengthsWithinLimit(int length, bool expected)
        {
            Assert.AreEqual(expected, ReverseProxyValidation.IsValidPayloadLength(length));
        }

        [TestMethod]
        [DataRow(10, 11, false)]
        [DataRow(10, 10, true)]
        [DataRow(10, 9, true)]
        [DataRow(10, 0, false)]
        [DataRow(10, -1, false)]
        public void IsStaleSequence_DetectsDuplicateAndOutOfOrderSequences(long lastSequence, long sequence, bool expected)
        {
            Assert.AreEqual(expected, ReverseProxyValidation.IsStaleSequence(lastSequence, sequence));
        }

        [TestMethod]
        [DataRow("", false)]
        [DataRow("   ", false)]
        [DataRow("listener-session", true)]
        [DataRow("valid-id", true)]
        [DataRow("bad\0id", false)]
        public void IsValidId_AcceptsBoundedNonBlankIds(string id, bool expected)
        {
            Assert.AreEqual(expected, ReverseProxyValidation.IsValidId(id));
        }

        [TestMethod]
        public void IsValidId_RejectsIdsPastMaximumLength()
        {
            Assert.IsFalse(ReverseProxyValidation.IsValidId(new string('x', 129)));
            Assert.IsTrue(ReverseProxyValidation.IsValidId(new string('x', 128)));
        }

        [TestMethod]
        public void CreateSessionId_GeneratesUniqueRepeatableProtocolValues()
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);

            for (var i = 0; i < 100; i++)
            {
                var id = ReverseProxyValidation.CreateSessionId();
                Assert.IsTrue(ReverseProxyValidation.IsValidId(id));
                Assert.IsTrue(ids.Add(id));
            }
        }
    }
}
