#if NET9_0_OR_GREATER
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Pulsar.Server.Dialogs;
using Pulsar.Server.Forms;
using Pulsar.Server.Networking;
using Pulsar.Server.Operations;
using Pulsar.Server.Persistence;
using Pulsar.Server.Services.Wpf;
using Pulsar.Server.ViewModels;
using Pulsar.Server.Views;
using ServerClient = Pulsar.Server.Networking.Client;

namespace Pulsar.Common.Tests.Server
{
    [TestClass]
    public class PulsarMainWindowTests
    {
        private const string ValidTestClientId = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

        private sealed class FakeDialogService : IClientDialogService
        {
            public List<ActionConfirmationRequest> ConfirmationRequests { get; } = new List<ActionConfirmationRequest>();
            public Func<ActionConfirmationRequest, ActionConfirmationResult> OnConfirm { get; set; }
            public Func<string, MessageBoxInputResult> OnMessageBoxInput { get; set; }
            public Func<string, QuickCommandInputResult> OnQuickCommandInput { get; set; }
            public Func<AutoTaskInputResult> OnAutoTaskInput { get; set; }

            public ActionConfirmationResult ShowConfirmation(ActionConfirmationRequest request)
            {
                ConfirmationRequests.Add(request);
                return OnConfirm?.Invoke(request) ?? new ActionConfirmationResult { Confirmed = true, DontAskAgain = false };
            }

            public MessageBoxInputResult ShowMessageBoxInput(string clientDisplay)
            {
                return OnMessageBoxInput?.Invoke(clientDisplay) ?? new MessageBoxInputResult
                {
                    Confirmed = true,
                    Caption = "Test Caption",
                    Text = "Hello from test",
                    Button = "OK",
                    Icon = "Information"
                };
            }

            public QuickCommandInputResult ShowQuickCommandInput(string clientDisplay)
            {
                return OnQuickCommandInput?.Invoke(clientDisplay) ?? new QuickCommandInputResult
                {
                    Confirmed = true,
                    Host = "cmd.exe",
                    Command = "whoami"
                };
            }

            public AutoTaskInputResult ShowAutoTaskInput()
            {
                return OnAutoTaskInput?.Invoke() ?? new AutoTaskInputResult
                {
                    Confirmed = true,
                    Title = "Execute Command",
                    Param1 = "whoami",
                    Param2 = "cmd.exe",
                    ExecutionMode = AutoTaskExecutionMode.OncePerClient
                };
            }
        }

        private sealed class FakeToolLauncher : IClientToolLauncher
        {
            public List<(ClientToolType Tool, ServerClient Client)> Launched { get; } = new List<(ClientToolType, ServerClient)>();

            public void Launch(ClientToolType tool, ServerClient client)
            {
                Launched.Add((tool, client));
            }
        }

        private sealed class FakeServerWindowLauncher : IServerWindowLauncher
        {
            public List<ServerWindowType> Launched { get; } = new List<ServerWindowType>();

            public void Launch(ServerWindowType windowType, PulsarServer server)
            {
                Launched.Add(windowType);
            }
        }

        /// <summary>
        /// Blocking read stream that keeps Client.Connected == true while recording all outbound frames written by Client.
        /// </summary>
        private sealed class CapturingLoopStream : Stream
        {
            private readonly ManualResetEventSlim _closed = new ManualResetEventSlim(false);
            private readonly object _writeLock = new object();
            private readonly List<byte[]> _writes = new List<byte[]>();
            private readonly AutoResetEvent _writeSignal = new AutoResetEvent(false);

            public int WriteCount
            {
                get { lock (_writeLock) return _writes.Count; }
            }

            public bool WaitForWriteCount(int expectedCount, int timeoutMs = 3000)
            {
                var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
                while (DateTime.UtcNow < deadline)
                {
                    if (WriteCount >= expectedCount)
                    {
                        return true;
                    }

                    _writeSignal.WaitOne(50);
                }

                return WriteCount >= expectedCount;
            }

            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => true;
            public override long Length => 0;
            public override long Position { get => 0; set => throw new NotSupportedException(); }

            public override int Read(byte[] buffer, int offset, int count)
            {
                _closed.Wait();
                return 0;
            }

            public override IAsyncResult BeginRead(byte[] buffer, int offset, int count, AsyncCallback callback, object state)
            {
                var tcs = new TaskCompletionSource<int>(state);
                Task.Run(() =>
                {
                    _closed.Wait();
                    tcs.TrySetResult(0);
                    callback?.Invoke(tcs.Task);
                });
                return tcs.Task;
            }

            public override int EndRead(IAsyncResult asyncResult)
            {
                return ((Task<int>)asyncResult).GetAwaiter().GetResult();
            }

            public override void Write(byte[] buffer, int offset, int count)
            {
                var copy = new byte[count];
                Buffer.BlockCopy(buffer, offset, copy, 0, count);
                lock (_writeLock)
                {
                    _writes.Add(copy);
                }
                _writeSignal.Set();
            }

            public override void Flush() { }
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    _closed.Set();
                    _writeSignal.Set();
                }
                base.Dispose(disposing);
            }
        }

        private static X509Certificate2 CreateTestCertificate()
        {
            using (var rsa = RSA.Create(2048))
            {
                var req = new CertificateRequest("CN=PulsarUiTest", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                var cert = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
                var pfxBytes = cert.Export(X509ContentType.Pfx);
                cert.Dispose();
                return X509CertificateLoader.LoadPkcs12(pfxBytes, null, X509KeyStorageFlags.Exportable);
            }
        }

        private static (ServerClient Client, CapturingLoopStream Stream, X509Certificate2 Cert) CreateConnectedTestClient(
            bool identified = true,
            string username = "operator_user",
            string pcName = "WORKSTATION-01",
            string country = "United States",
            string countryCode = "US",
            string clientId = ValidTestClientId,
            int port = 47820)
        {
            var cert = CreateTestCertificate();
            var stream = new CapturingLoopStream();
            var client = new ServerClient(stream, new IPEndPoint(IPAddress.Loopback, port), cert);
            client.Value.Id = clientId;
            client.Value.Username = username;
            client.Value.PcName = pcName;
            client.Value.PublicIP = "203.0.113.42";
            client.Value.Country = country;
            client.Value.CountryCode = countryCode;
            client.Value.AccountType = "Admin";
            client.Value.OperatingSystem = "Windows 11 Pro";
            client.Value.Version = "2.4.0";
            client.Value.Tag = "Lab-Alpha";
            client.Identified = identified;
            return (client, stream, cert);
        }

        private static string CreateTempDataDirectory()
        {
            var dir = Path.Combine(Path.GetTempPath(), "PulsarUiTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return dir;
        }

        [TestMethod]
        public void SystemControlCommands_CanExecuteReflectsSelectionConnectionAndIdentification()
        {
            var tempDir = CreateTempDataDirectory();
            try
            {
                var dialogs = new FakeDialogService();
                var launcher = new FakeToolLauncher();
                using var vm = new MainViewModel(null, null, dialogs, launcher, a => a(), new FakeServerWindowLauncher(), tempDir);

                Assert.IsFalse(vm.DisconnectCommand.CanExecute(null));
                Assert.IsFalse(vm.ReconnectCommand.CanExecute(null));
                Assert.IsFalse(vm.RestartCommand.CanExecute(null));
                Assert.IsFalse(vm.ShutdownCommand.CanExecute(null));
                Assert.IsFalse(vm.StandbyCommand.CanExecute(null));
                Assert.IsFalse(vm.LockscreenCommand.CanExecute(null));
                Assert.IsFalse(vm.ShowMessageBoxCommand.CanExecute(null));
                Assert.IsFalse(vm.ExecuteCommandCommand.CanExecute(null));
                Assert.IsFalse(vm.UninstallCommand.CanExecute(null));
                Assert.IsFalse(vm.QuickMicrophoneCommand.CanExecute(null));

                var (unidentifiedClient, stream1, cert1) = CreateConnectedTestClient(identified: false);
                try
                {
                    vm.OnlineClients.Add(unidentifiedClient);
                    vm.SelectedClient = unidentifiedClient;

                    Assert.IsFalse(vm.DisconnectCommand.CanExecute(null));
                    Assert.IsFalse(vm.UninstallCommand.CanExecute(null));
                    Assert.IsTrue(vm.QuickMicrophoneCommand.CanExecute(null));

                    unidentifiedClient.Identified = true;
                    Assert.IsTrue(vm.DisconnectCommand.CanExecute(null));
                    Assert.IsTrue(vm.ReconnectCommand.CanExecute(null));
                    Assert.IsTrue(vm.RestartCommand.CanExecute(null));
                    Assert.IsTrue(vm.ShutdownCommand.CanExecute(null));
                    Assert.IsTrue(vm.StandbyCommand.CanExecute(null));
                    Assert.IsTrue(vm.LockscreenCommand.CanExecute(null));
                    Assert.IsTrue(vm.ShowMessageBoxCommand.CanExecute(null));
                    Assert.IsTrue(vm.ExecuteCommandCommand.CanExecute(null));
                    Assert.IsTrue(vm.UninstallCommand.CanExecute(null));
                }
                finally
                {
                    unidentifiedClient.Disconnect();
                    stream1.Dispose();
                    cert1.Dispose();
                }
            }
            finally
            {
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            }
        }

        [TestMethod]
        public void HighRiskConfirmation_SupportsSessionSuppressAndReset()
        {
            var tempDir = CreateTempDataDirectory();
            var (client, stream, cert) = CreateConnectedTestClient();
            try
            {
                var dialogs = new FakeDialogService
                {
                    OnConfirm = req => new ActionConfirmationResult { Confirmed = true, DontAskAgain = true }
                };
                using var vm = new MainViewModel(null, null, dialogs, new FakeToolLauncher(), a => a(), new FakeServerWindowLauncher(), tempDir);
                vm.OnlineClients.Add(client);
                vm.SelectedClient = client;

                vm.LockscreenCommand.Execute(null);
                Assert.AreEqual(1, dialogs.ConfirmationRequests.Count);
                Assert.AreEqual(ActionRiskLevel.High, dialogs.ConfirmationRequests[0].RiskLevel);
                Assert.IsTrue(dialogs.ConfirmationRequests[0].AllowDontAskAgain);

                vm.LockscreenCommand.Execute(null);
                Assert.AreEqual(1, dialogs.ConfirmationRequests.Count);

                vm.ResetConfirmationsCommand.Execute(null);
                vm.LockscreenCommand.Execute(null);
                Assert.AreEqual(2, dialogs.ConfirmationRequests.Count);
                Assert.IsTrue(stream.WaitForWriteCount(3));
            }
            finally
            {
                client.Disconnect();
                stream.Dispose();
                cert.Dispose();
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            }
        }

        [TestMethod]
        public void CriticalRiskOperations_AlwaysConfirmEvenIfDontAskAgainReturned()
        {
            var tempDir = CreateTempDataDirectory();
            var (client, stream, cert) = CreateConnectedTestClient();
            try
            {
                var dialogs = new FakeDialogService
                {
                    OnConfirm = req => new ActionConfirmationResult { Confirmed = true, DontAskAgain = true }
                };
                using var vm = new MainViewModel(null, null, dialogs, new FakeToolLauncher(), a => a(), new FakeServerWindowLauncher(), tempDir);
                vm.OnlineClients.Add(client);
                vm.SelectedClient = client;

                vm.UninstallCommand.Execute(null);
                vm.UninstallCommand.Execute(null);
                vm.ShutdownCommand.Execute(null);
                vm.RestartCommand.Execute(null);

                Assert.AreEqual(4, dialogs.ConfirmationRequests.Count);
                Assert.IsTrue(dialogs.ConfirmationRequests.All(r => r.RiskLevel == ActionRiskLevel.Critical));
                Assert.IsTrue(dialogs.ConfirmationRequests.All(r => !r.AllowDontAskAgain));
                Assert.IsTrue(stream.WaitForWriteCount(4));
            }
            finally
            {
                client.Disconnect();
                stream.Dispose();
                cert.Dispose();
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            }
        }

        [TestMethod]
        public void CancelledConfirmation_DoesNotSendOperation()
        {
            var tempDir = CreateTempDataDirectory();
            var (client, stream, cert) = CreateConnectedTestClient();
            try
            {
                var dialogs = new FakeDialogService
                {
                    OnConfirm = _ => new ActionConfirmationResult { Confirmed = false }
                };
                using var vm = new MainViewModel(null, null, dialogs, new FakeToolLauncher(), a => a(), new FakeServerWindowLauncher(), tempDir);
                vm.OnlineClients.Add(client);
                vm.SelectedClient = client;

                vm.DisconnectCommand.Execute(null);
                Assert.AreEqual(1, dialogs.ConfirmationRequests.Count);
                Assert.AreEqual(0, stream.WriteCount);
                Assert.AreEqual(ActionExecutionState.Ready, vm.DisconnectFeedback.State);
            }
            finally
            {
                client.Disconnect();
                stream.Dispose();
                cert.Dispose();
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            }
        }

        [TestMethod]
        public void AllNineSystemControlOperations_DispatchThroughClientOperationServiceAndAudit()
        {
            var tempDir = CreateTempDataDirectory();
            var (client, stream, cert) = CreateConnectedTestClient();
            try
            {
                var dialogs = new FakeDialogService();
                using var vm = new MainViewModel(null, null, dialogs, new FakeToolLauncher(), a => a(), new FakeServerWindowLauncher(), tempDir);
                vm.OnlineClients.Add(client);
                vm.SelectedClient = client;

                vm.DisconnectCommand.Execute(null);
                vm.ReconnectCommand.Execute(null);
                vm.RestartCommand.Execute(null);
                vm.ShutdownCommand.Execute(null);
                vm.StandbyCommand.Execute(null);
                vm.LockscreenCommand.Execute(null);
                vm.ShowMessageBoxCommand.Execute(null);
                vm.ExecuteCommandCommand.Execute(null);
                vm.UninstallCommand.Execute(null);

                Assert.IsTrue(stream.WaitForWriteCount(9), $"Expected 9 outbound frames, saw {stream.WriteCount}.");
                Assert.AreEqual(ActionExecutionState.Success, vm.UninstallFeedback.State);
                Assert.AreEqual(ActionExecutionState.Success, vm.MessageBoxFeedback.State);
                Assert.AreEqual(ActionExecutionState.Success, vm.ExecuteCommandFeedback.State);
                Assert.IsTrue(vm.HasFeedbackBanner);
                Assert.IsTrue(vm.DebugLogs.Count >= 9, "Expected ClientOperationService audit entries in DebugLogs.");
            }
            finally
            {
                client.Disconnect();
                stream.Dispose();
                cert.Dispose();
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            }
        }

        [TestMethod]
        public void ToolLaunchAndMenuModuleAndWindowCommands_DispatchCleanly()
        {
            var tempDir = CreateTempDataDirectory();
            var (client, stream, cert) = CreateConnectedTestClient();
            try
            {
                var launcher = new FakeToolLauncher();
                var windowLauncher = new FakeServerWindowLauncher();
                using var vm = new MainViewModel(null, null, new FakeDialogService(), launcher, a => a(), windowLauncher, tempDir);
                vm.OnlineClients.Add(client);
                vm.SelectedClient = client;

                // Quick Tool Launch 8
                vm.QuickMicrophoneCommand.Execute(null);
                vm.QuickRemoteDesktopCommand.Execute(null);
                vm.QuickWebcamCommand.Execute(null);
                vm.QuickKeyloggerCommand.Execute(null);
                vm.QuickFileManagerCommand.Execute(null);
                vm.QuickRemoteShellCommand.Execute(null);
                vm.QuickRemoteExecutionCommand.Execute(null);
                vm.QuickHvncCommand.Execute(null);

                // Additional Menu / Context Menu Admin & Monitoring modules
                vm.OpenSystemInfoCommand.Execute(null);
                vm.OpenTaskManagerCommand.Execute(null);
                vm.OpenStartupManagerCommand.Execute(null);
                vm.OpenRegistryEditorCommand.Execute(null);
                vm.OpenTcpConnectionsCommand.Execute(null);
                vm.OpenReverseProxyCommand.Execute(null);
                vm.OpenPasswordRecoveryCommand.Execute(null);
                vm.OpenSystemAudioCommand.Execute(null);
                vm.OpenRemoteChatCommand.Execute(null);

                Assert.AreEqual(17, launcher.Launched.Count);
                Assert.AreEqual(ActionExecutionState.Success, vm.QuickHvncFeedback.State);

                // Server Utility Windows
                vm.OpenSettingsCommand.Execute(null);
                vm.OpenBuilderCommand.Execute(null);
                vm.OpenKeywordsCommand.Execute(null);
                vm.OpenAboutCommand.Execute(null);

                CollectionAssert.AreEqual(
                    new[] { ServerWindowType.Settings, ServerWindowType.Builder, ServerWindowType.Keywords, ServerWindowType.About },
                    windowLauncher.Launched);
            }
            finally
            {
                client.Disconnect();
                stream.Dispose();
                cert.Dispose();
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            }
        }

        [TestMethod]
        public void ElevationTempCleanupAndClipboardCommands_WorkEndToEnd()
        {
            var tempDir = CreateTempDataDirectory();
            var (client, stream, cert) = CreateConnectedTestClient();
            try
            {
                var copied = new List<string>();
                var dialogs = new FakeDialogService();
                using var vm = new MainViewModel(null, null, dialogs, new FakeToolLauncher(), a => a(), new FakeServerWindowLauncher(), tempDir, copied.Add);
                vm.OnlineClients.Add(client);
                vm.SelectedClient = client;

                vm.DeleteTempDirectoryCommand.Execute(null);
                vm.AskElevateCommand.Execute(null);
                vm.UacBypassCommand.Execute(null);
                vm.ElevateSystemCommand.Execute(null);
                vm.DeElevateCommand.Execute(null);

                Assert.AreEqual(5, dialogs.ConfirmationRequests.Count);
                Assert.IsTrue(stream.WaitForWriteCount(5), $"Expected 5 frames, saw {stream.WriteCount}.");

                vm.CopyClientIpCommand.Execute(null);
                vm.CopyClientEndpointCommand.Execute(null);
                vm.CopyClientIdCommand.Execute(null);
                vm.CopyClientUserAtPcCommand.Execute(null);

                CollectionAssert.AreEqual(
                    new[] { "203.0.113.42", "127.0.0.1:47820", ValidTestClientId, "operator_user@WORKSTATION-01" },
                    copied);
            }
            finally
            {
                client.Disconnect();
                stream.Dispose();
                cert.Dispose();
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            }
        }

        [TestMethod]
        public void ClientSearchFilterAndAnalyticsNavigation_WorkCorrectly()
        {
            var tempDir = CreateTempDataDirectory();
            var (c1, s1, cert1) = CreateConnectedTestClient(username: "alice", pcName: "FINANCE-01", country: "United States", countryCode: "US", clientId: ValidTestClientId, port: 47821);
            var (c2, s2, cert2) = CreateConnectedTestClient(username: "bob", pcName: "DEV-BOX", country: "Germany", countryCode: "DE", clientId: "fedcba9876543210fedcba9876543210fedcba9876543210fedcba9876543210", port: 47822);
            try
            {
                using var vm = new MainViewModel(null, null, new FakeDialogService(), new FakeToolLauncher(), a => a(), new FakeServerWindowLauncher(), tempDir);
                vm.OnlineClients.Add(c1);
                vm.OnlineClients.Add(c2);

                Assert.AreEqual(2, vm.ConnectedClientCount);
                Assert.AreEqual(2, vm.FilteredClientCount);

                vm.SearchQuery = "FINANCE";
                Assert.IsTrue(vm.HasSearchQuery);
                Assert.AreEqual(1, vm.FilteredClientCount);
                Assert.AreEqual("Connected Clients (1 of 2 matching)", vm.ClientsHeaderText);

                vm.ClearSearchCommand.Execute(null);
                Assert.IsFalse(vm.HasSearchQuery);
                Assert.AreEqual(2, vm.FilteredClientCount);

                AnalyticsSnapshotsEventArgs captured = null;
                vm.AnalyticsSnapshotsUpdated += (_, e) => captured = e;
                vm.NavigateTabCommand.Execute("1");
                Assert.AreEqual(1, vm.SelectedTabIndex);
                Assert.IsNotNull(captured);
                Assert.AreEqual(2, captured.Statistics.TotalClients);
                Assert.AreEqual(2, captured.Geo.MappedClients);
            }
            finally
            {
                c1.Disconnect();
                c2.Disconnect();
                s1.Dispose();
                s2.Dispose();
                cert1.Dispose();
                cert2.Dispose();
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            }
        }

        [TestMethod]
        public void CryptoAutoTasksAndNotifications_PersistAndReloadCleanly()
        {
            var tempDir = CreateTempDataDirectory();
            try
            {
                using (var vm = new MainViewModel(null, null, new FakeDialogService(), new FakeToolLauncher(), a => a(), new FakeServerWindowLauncher(), tempDir))
                {
                    vm.BitcoinAddress = "bc1qtestbitcoin";
                    vm.EthereumAddress = "0xTestEthereum";
                    vm.TronAddress = "TTestTronAddress";
                    vm.CryptoClipperEnabled = true;
                    vm.SaveCryptoConfigurationCommand.Execute(null);

                    vm.AddAutoTaskCommand.Execute(null);
                    Assert.AreEqual(1, vm.AutoTasks.Count);
                    Assert.AreEqual("Execute Command", vm.AutoTasks[0].Title);

                    vm.Notifications.Add(new NotificationEntry
                    {
                        Client = "alice@FINANCE-01",
                        Timestamp = DateTime.UtcNow,
                        Event = "Keyword Match",
                        Parameter = "vault",
                        IsRead = false
                    });
                    Assert.AreEqual("Pending notifications: 1", vm.NotificationStatusText);
                    vm.MarkAllNotificationsReadCommand.Execute(null);
                    Assert.AreEqual("You're all caught up. 1 notifications logged.", vm.NotificationStatusText);
                }

                // Reload in a fresh MainViewModel pointing to the same temp directory
                using (var reloaded = new MainViewModel(null, null, new FakeDialogService(), new FakeToolLauncher(), a => a(), new FakeServerWindowLauncher(), tempDir))
                {
                    Assert.AreEqual("bc1qtestbitcoin", reloaded.BitcoinAddress);
                    Assert.AreEqual("0xTestEthereum", reloaded.EthereumAddress);
                    Assert.AreEqual("TTestTronAddress", reloaded.TronAddress);
                    Assert.IsTrue(reloaded.CryptoClipperEnabled);

                    Assert.AreEqual(1, reloaded.AutoTasks.Count);
                    Assert.AreEqual("whoami", reloaded.AutoTasks[0].Param1);
                    reloaded.SelectedAutoTask = reloaded.AutoTasks[0];
                    reloaded.DeleteSelectedAutoTaskCommand.Execute(null);
                    Assert.AreEqual(0, reloaded.AutoTasks.Count);

                    Assert.AreEqual(1, reloaded.Notifications.Count);
                    Assert.IsTrue(reloaded.Notifications[0].IsRead);
                    reloaded.ClearAllNotificationsCommand.Execute(null);
                    Assert.AreEqual(0, reloaded.Notifications.Count);
                }
            }
            finally
            {
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            }
        }

        [TestMethod]
        public void SelectedClientProjection_ExposesRealClientFieldsAndClearsOnEscape()
        {
            var tempDir = CreateTempDataDirectory();
            var (client, stream, cert) = CreateConnectedTestClient();
            try
            {
                using var vm = new MainViewModel(null, null, new FakeDialogService(), new FakeToolLauncher(), a => a(), new FakeServerWindowLauncher(), tempDir);
                Assert.IsFalse(vm.HasSelectedClient);
                Assert.AreEqual("No Selection", vm.SelectedClientStatusText);
                Assert.AreEqual("—", vm.SelectedClientUsername);

                vm.OnlineClients.Add(client);
                vm.SelectedClient = client;

                Assert.IsTrue(vm.HasSelectedClient);
                Assert.IsTrue(vm.SelectedClientIsOnline);
                Assert.IsTrue(vm.SelectedClientIsIdentified);
                Assert.AreEqual("Online", vm.SelectedClientStatusText);
                Assert.AreEqual("Verified", vm.SelectedClientIdentifiedText);
                Assert.AreEqual("operator_user", vm.SelectedClientUsername);
                Assert.AreEqual("WORKSTATION-01", vm.SelectedClientHostname);
                Assert.AreEqual("203.0.113.42", vm.SelectedClientPublicIp);
                Assert.AreEqual("United States [US]", vm.SelectedClientCountry);
                Assert.AreEqual("Admin", vm.SelectedClientAccountType);
                Assert.AreEqual("Windows 11 Pro", vm.SelectedClientOperatingSystem);
                Assert.AreEqual("2.4.0", vm.SelectedClientVersion);
                Assert.AreEqual("Lab-Alpha", vm.SelectedClientTag);
                Assert.AreEqual(ValidTestClientId, vm.SelectedClientId);
                Assert.AreEqual("0123456789ab…", vm.SelectedClientIdShort);
                Assert.AreNotEqual("—", vm.SelectedClientConnectedSince);

                vm.ClearSelectionCommand.Execute(null);
                Assert.IsNull(vm.SelectedClient);
                Assert.IsFalse(vm.HasSelectedClient);
                Assert.AreEqual("No Selection", vm.SelectedClientStatusText);
            }
            finally
            {
                client.Disconnect();
                stream.Dispose();
                cert.Dispose();
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            }
        }

        [TestMethod]
        public void WpfMainWindowAndDialogs_InstantiateCleanlyOnStaThreadWithThemesMenusAndAnalytics()
        {
            Exception staException = null;
            var thread = new Thread(() =>
            {
                try
                {
                    var window = new PulsarMainWindow();
                    Assert.IsNotNull(window.ViewModel);
                    Assert.IsTrue(window.InputBindings.Count >= 27, $"Expected >=27 keyboard shortcuts, found {window.InputBindings.Count}.");
                    var grid = window.FindName("OnlineClientsDataGrid") as System.Windows.Controls.DataGrid;
                    Assert.IsNotNull(grid);
                    Assert.IsNotNull(grid.ContextMenu);
                    Assert.IsTrue(grid.ContextMenu.Items.Count >= 6);
                    Assert.IsNotNull(window.FindName("StatsPanelView"));
                    Assert.IsNotNull(window.FindName("HeatMapPanelView"));

                    // Verify required theme brushes & styles resolved without missing keys
                    Assert.IsNotNull(window.TryFindResource("SystemControlGlassBackground"));
                    Assert.IsNotNull(window.TryFindResource("ToolLaunchGlassBackground"));
                    Assert.IsNotNull(window.TryFindResource("SystemControlActionButtonStyle"));
                    Assert.IsNotNull(window.TryFindResource("ToolLaunchActionButtonStyle"));
                    Assert.IsNotNull(window.TryFindResource("WindowBackgroundBrush"));

                    // Toggle Dark -> Light -> Dark across MainWindow + StatsView + HeatMapView
                    window.ViewModel.ToggleThemeCommand.Execute(null);
                    Assert.AreEqual(ThemeService.Theme.Light, ThemeService.Instance.CurrentTheme);
                    window.ViewModel.ToggleThemeCommand.Execute(null);
                    Assert.AreEqual(ThemeService.Theme.Dark, ThemeService.Instance.CurrentTheme);

                    // Instantiate all 4 WPF dialogs and verify defaults
                    var confirmDialog = new ConfirmationDialog(new ActionConfirmationRequest
                    {
                        Title = "Confirm Uninstall",
                        ActionName = "Uninstall",
                        ClientDisplay = "user@HOST",
                        ClientEndpoint = "127.0.0.1:4782",
                        Consequence = "Permanent removal",
                        Recoverability = "Irreversible",
                        RiskLevel = ActionRiskLevel.Critical,
                        AllowDontAskAgain = false
                    });
                    Assert.IsFalse(confirmDialog.DontAskAgain);
                    confirmDialog.Close();

                    var msgDialog = new MessageBoxInputDialog("user@HOST");
                    Assert.AreEqual("OK", msgDialog.SelectedButton);
                    Assert.AreEqual("Information", msgDialog.SelectedIcon);
                    msgDialog.Close();

                    var cmdDialog = new QuickCommandInputDialog("user@HOST");
                    Assert.AreEqual("cmd.exe", cmdDialog.SelectedHost);
                    cmdDialog.Close();

                    var autoTaskDialog = new AutoTaskInputDialog();
                    Assert.AreEqual("Execute Command", autoTaskDialog.TaskTitle);
                    Assert.AreEqual(AutoTaskExecutionMode.OncePerClient, autoTaskDialog.ExecutionMode);
                    autoTaskDialog.Close();

                    window.Close();
                }
                catch (Exception ex)
                {
                    staException = ex;
                }
            });

            if (OperatingSystem.IsWindows())
            {
                thread.SetApartmentState(ApartmentState.STA);
            }
            thread.Start();
            Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(20)), "STA UI test timed out.");

            if (staException != null)
            {
                Assert.Fail($"WPF instantiation failed on STA thread: {staException}");
            }
        }
    }
}
#endif
