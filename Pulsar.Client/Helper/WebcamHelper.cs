using AForge.Video;
using AForge.Video.DirectShow;
using System;
using System.Diagnostics;
using System.Drawing;
using System.Threading;

namespace Pulsar.Client.Helper
{
    public class WebcamHelper
    {
        private readonly object _lock = new object();
        private Bitmap _currentFrame;
        private bool _isRunning = false;
        private VideoCaptureDevice _videoDevice;
        private int _width;
        private int _height;
        private long _captureVersion;
        private long _deliveredCaptureVersion;

        public bool StartWebcam(int webcamIndex)
        {
            VideoCaptureDevice newDevice = null;
            try
            {
                if (_isRunning)
                {
                    StopWebcam();
                }

                FilterInfoCollection captureDevices = new FilterInfoCollection(FilterCategory.VideoInputDevice);
                if (captureDevices.Count == 0)
                {
                    throw new InvalidOperationException("No webcam detected.");
                }

                if (webcamIndex < 0 || webcamIndex >= captureDevices.Count)
                {
                    throw new ArgumentOutOfRangeException(nameof(webcamIndex), "Invalid webcam device index.");
                }

                newDevice = new VideoCaptureDevice(captureDevices[webcamIndex].MonikerString);
                _videoDevice = newDevice;

                var videoCapabilities = _videoDevice.VideoCapabilities;
                if (videoCapabilities != null && videoCapabilities.Length > 0)
                {
                    bool foundMatchingResolution = false;

                    foreach (var capability in videoCapabilities)
                    {
                        if (capability.AverageFrameRate >= 25 && capability.AverageFrameRate <= 35)
                        {
                            _videoDevice.VideoResolution = capability;
                            foundMatchingResolution = true;
                            Debug.WriteLine($"Selected video mode: {capability.FrameSize.Width}x{capability.FrameSize.Height} @ {capability.AverageFrameRate}fps");
                            break;
                        }
                    }

                    if (!foundMatchingResolution && videoCapabilities.Length > 0)
                    {
                        _videoDevice.VideoResolution = videoCapabilities[0];
                        Debug.WriteLine($"Selected default video mode: {videoCapabilities[0].FrameSize.Width}x{videoCapabilities[0].FrameSize.Height} @ {videoCapabilities[0].AverageFrameRate}fps");
                    }
                }

                _videoDevice.NewFrame += new NewFrameEventHandler(FinalFrame_NewFrame);
                _videoDevice.VideoSourceError += VideoDevice_VideoSourceError;
                _videoDevice.Start();
                lock (_lock)
                {
                    _captureVersion = 0;
                    _deliveredCaptureVersion = 0;
                    _currentFrame?.Dispose();
                    _currentFrame = null;
                    _width = 0;
                    _height = 0;
                    _isRunning = true;
                }
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error starting webcam: {ex.Message}");
                if (newDevice != null)
                {
                    try
                    {
                        newDevice.NewFrame -= FinalFrame_NewFrame;
                        newDevice.VideoSourceError -= VideoDevice_VideoSourceError;
                        if (newDevice.IsRunning)
                            newDevice.SignalToStop();
                    }
                    catch { }
                    newDevice = null;
                }
                _isRunning = false;
                _videoDevice = null;
                throw;
            }
        }

        public static string[] GetWebcams()
        {
            try
            {
                FilterInfoCollection captureDevices = new FilterInfoCollection(FilterCategory.VideoInputDevice);
                string[] webcams = new string[captureDevices.Count];
                for (int i = 0; i < captureDevices.Count; i++)
                {
                    webcams[i] = captureDevices[i].Name;
                }
                return webcams;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error getting webcams: {ex.Message}");
                return new string[0];
            }
        }

        public void StopWebcam()
        {
            VideoCaptureDevice device;
            lock (_lock)
            {
                device = _videoDevice;
                _isRunning = false;
                _currentFrame?.Dispose();
                _currentFrame = null;
                _width = 0;
                _height = 0;
                _captureVersion = 0;
                _deliveredCaptureVersion = 0;
                _videoDevice = null;
            }

            if (device == null)
                return;

            try
            {
                device.NewFrame -= FinalFrame_NewFrame;
                device.VideoSourceError -= VideoDevice_VideoSourceError;
                if (device.IsRunning)
                    device.SignalToStop();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error stopping webcam: {ex.Message}");
            }
        }

        public Bitmap GetLatestFrame()
        {
            lock (_lock)
            {
                try
                {
                    if (_currentFrame == null)
                        return null;
                    if (_captureVersion == _deliveredCaptureVersion)
                        return null;

                    _deliveredCaptureVersion = _captureVersion;
                    return (Bitmap)_currentFrame.Clone();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error getting latest frame: {ex.Message}");
                    return null;
                }
            }
        }

        public Bounds GetBounds()
        {
            lock (_lock)
            {
                try
                {
                    return new Bounds { Width = _width, Height = _height };
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error getting bounds: {ex.Message}");
                    return new Bounds { Width = 0, Height = 0 };
                }
            }
        }

        private void FinalFrame_NewFrame(object sender, NewFrameEventArgs eventArgs)
        {
            lock (_lock)
            {
                try
                {
                    _currentFrame?.Dispose();
                    Bitmap frame = (Bitmap)eventArgs.Frame.Clone();

                    frame.RotateFlip(RotateFlipType.RotateNoneFlipX);

                    _currentFrame = frame;
                    _width = _currentFrame.Width;
                    _height = _currentFrame.Height;
                    _captureVersion++;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error processing new frame: {ex.Message}");
                }
            }
        }

        private void VideoDevice_VideoSourceError(object sender, VideoSourceErrorEventArgs eventArgs)
        {
            var desc = eventArgs.Description ?? string.Empty;
            if (desc.IndexOf("0x80004002", StringComparison.OrdinalIgnoreCase) >= 0 ||
                desc.IndexOf("Interface not supported", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                Debug.WriteLine("Webcam does not support required video control interface; ignoring.");
            }
            else
            {
                Debug.WriteLine($"Video source error: {desc}");
            }
        }
    }

    public struct Bounds
    {
        public int Width { get; set; }
        public int Height { get; set; }
    }
}
