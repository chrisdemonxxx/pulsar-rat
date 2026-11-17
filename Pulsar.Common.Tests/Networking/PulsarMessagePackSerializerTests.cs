using Microsoft.VisualStudio.TestTools.UnitTesting;
using Pulsar.Common.Messages;
using Pulsar.Common.Messages.Administration.TaskManager;
using Pulsar.Common.Messages.Monitoring.RemoteDesktop;
using Pulsar.Common.Messages.Other;
using Pulsar.Common.Networking;
using System;
using Process = Pulsar.Common.Models.Process;

namespace Pulsar.Common.Tests.Networking
{
    [TestClass]
    public class PulsarMessagePackSerializerTests
    {
        [TestMethod]
        public void RoundTrip_SetStatus_PreservesTypeAndFields()
        {
            var original = new SetStatus { Message = "Kolibri serializer round-trip \u00e4\u00f6\u00fc" };

            var result = RoundTrip(original);

            Assert.IsInstanceOfType(result, typeof(SetStatus));
            var typed = (SetStatus)result;
            Assert.AreEqual(original.Message, typed.Message);
        }

        [TestMethod]
        public void RoundTrip_DoProcessStart_PreservesBinaryAndScalarFields()
        {
            var fileBytes = new byte[256];
            for (var i = 0; i < fileBytes.Length; i++)
                fileBytes[i] = (byte)i;

            var original = new DoProcessStart
            {
                DownloadUrl = "https://example.org/payload.exe",
                FilePath = "C:\\temp\\payload.exe",
                IsUpdate = true,
                ExecuteInMemoryDotNet = false,
                UseRunPE = true,
                RunPETarget = "svchost.exe",
                RunPECustomPath = "C:\\Windows\\Microsoft.NET\\runpe.exe",
                FileBytes = fileBytes,
                FileExtension = ".exe"
            };

            var result = RoundTrip(original);

            Assert.IsInstanceOfType(result, typeof(DoProcessStart));
            var typed = (DoProcessStart)result;
            Assert.AreEqual(original.DownloadUrl, typed.DownloadUrl);
            Assert.AreEqual(original.FilePath, typed.FilePath);
            Assert.AreEqual(original.IsUpdate, typed.IsUpdate);
            Assert.AreEqual(original.ExecuteInMemoryDotNet, typed.ExecuteInMemoryDotNet);
            Assert.AreEqual(original.UseRunPE, typed.UseRunPE);
            Assert.AreEqual(original.RunPETarget, typed.RunPETarget);
            Assert.AreEqual(original.RunPECustomPath, typed.RunPECustomPath);
            Assert.AreEqual(original.FileExtension, typed.FileExtension);
            CollectionAssert.AreEqual(original.FileBytes, typed.FileBytes);
        }

        [TestMethod]
        public void RoundTrip_GetProcessesResponse_PreservesCollectionAndNullableFields()
        {
            // Processes is a Process[] (not List<T>) - exercises the dynamic-generic collection path.
            var original = new GetProcessesResponse
            {
                Processes = new[]
                {
                    new Process { Name = "pulsar", Id = 1337, MainWindowTitle = "Pulsar", ParentId = 4 },
                    new Process { Name = "idle", Id = 0, MainWindowTitle = null, ParentId = null }
                },
                RatPid = 4242
            };

            var result = RoundTrip(original);

            Assert.IsInstanceOfType(result, typeof(GetProcessesResponse));
            var typed = (GetProcessesResponse)result;
            Assert.AreEqual(original.RatPid, typed.RatPid);
            Assert.IsNotNull(typed.Processes);
            Assert.AreEqual(2, typed.Processes.Length);
            Assert.AreEqual("pulsar", typed.Processes[0].Name);
            Assert.AreEqual(1337, typed.Processes[0].Id);
            Assert.AreEqual("Pulsar", typed.Processes[0].MainWindowTitle);
            Assert.AreEqual((int?)4, typed.Processes[0].ParentId);
            Assert.AreEqual("idle", typed.Processes[1].Name);
            Assert.IsNull(typed.Processes[1].MainWindowTitle);
            Assert.IsNull(typed.Processes[1].ParentId);
        }

        [TestMethod]
        public void Deserialize_GarbageInput_ThrowsInvalidOperationException()
        {
            // Deserialize wraps every failure in InvalidOperationException - it must never return null/garbage.
            Assert.ThrowsException<InvalidOperationException>(
                () => { PulsarMessagePackSerializer.Deserialize(new byte[] { 0x01, 0x02, 0x03, 0x04 }); });
        }

        [TestMethod]
        public void RoundTrip_DoRemoteDesktopPermissionResponse_PreservesFields()
        {
            // consent-prompt denial (client -> server, wire-additive)
            var original = new DoRemoteDesktopPermissionResponse
            {
                Allowed = false,
                Reason = "denied by user"
            };

            var result = RoundTrip(original);

            Assert.IsInstanceOfType(result, typeof(DoRemoteDesktopPermissionResponse));
            var typed = (DoRemoteDesktopPermissionResponse)result;
            Assert.AreEqual(original.Allowed, typed.Allowed);
            Assert.AreEqual(original.Reason, typed.Reason);
        }

        private static IMessage RoundTrip(IMessage message)
        {
            var payload = PulsarMessagePackSerializer.Serialize(message);
            Assert.IsNotNull(payload);
            Assert.AreNotEqual(0, payload.Length);
            return PulsarMessagePackSerializer.Deserialize(payload);
        }
    }
}
