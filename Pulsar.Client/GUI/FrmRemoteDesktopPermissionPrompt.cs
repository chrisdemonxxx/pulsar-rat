using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace Pulsar.Client.GUI
{
    /// <summary>
    /// Consent prompt shown to the local user when an operator starts a remote desktop session
    /// on a build with the user-facing GUI (builder flag SHOWCLIENTGUI). Auto-denies after the
    /// countdown elapses so an unattended machine never streams without consent.
    /// </summary>
    internal sealed class FrmRemoteDesktopPermissionPrompt : Form
    {
        private readonly Label _countdownLabel;
        private readonly System.Windows.Forms.Timer _countdownTimer;
        private int _remainingSeconds;

        internal FrmRemoteDesktopPermissionPrompt(int timeoutSeconds, string title = "Remote Desktop",
            string requestText = null)
        {
            _remainingSeconds = timeoutSeconds <= 0 ? 30 : timeoutSeconds;

            // Neutral wording - no tool branding on a consent dialog.
            Text = title;
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            TopMost = true;
            ShowInTaskbar = true;
            ClientSize = new Size(400, 160);

            var messageLabel = new Label
            {
                Text = (requestText ?? "A remote operator is requesting to view your desktop.") + Environment.NewLine + Environment.NewLine +
                       "Allow this session?",
                TextAlign = ContentAlignment.MiddleCenter,
                Dock = DockStyle.Fill,
                Font = new Font(Font, FontStyle.Bold)
            };

            _countdownLabel = new Label
            {
                Text = $"Auto-deny in {_remainingSeconds}s",
                Dock = DockStyle.Bottom,
                Height = 24,
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = SystemColors.GrayText
            };

            var buttonPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 48,
                FlowDirection = FlowDirection.RightToLeft,
                Padding = new Padding(12, 8, 12, 8)
            };

            var btnAllow = new Button { Text = "Allow", DialogResult = DialogResult.Yes, Width = 90 };
            var btnDeny = new Button { Text = "Deny", DialogResult = DialogResult.No, Width = 90 };

            buttonPanel.Controls.Add(btnDeny);
            buttonPanel.Controls.Add(btnAllow);

            Controls.Add(messageLabel);
            Controls.Add(_countdownLabel);
            Controls.Add(buttonPanel);

            AcceptButton = btnAllow;
            CancelButton = btnDeny;

            _countdownTimer = new System.Windows.Forms.Timer { Interval = 1000 };
            _countdownTimer.Tick += CountdownTimer_Tick;
            _countdownTimer.Start();
        }

        private void CountdownTimer_Tick(object sender, EventArgs e)
        {
            _remainingSeconds--;
            if (_remainingSeconds <= 0)
            {
                _countdownTimer.Stop();
                DialogResult = DialogResult.No;
                Close();
                return;
            }

            _countdownLabel.Text = $"Auto-deny in {_remainingSeconds}s";
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _countdownTimer.Stop();
            _countdownTimer.Dispose();
            base.OnFormClosed(e);
        }
    }

    /// <summary>
    /// Shows the consent prompt on a dedicated STA thread and blocks the caller (a message
    /// dispatcher thread-pool thread) until the user answers or the countdown auto-denies.
    /// </summary>
    internal static class RemoteDesktopPermissionPrompt
    {
        internal static bool RequestUserConsent(int timeoutSeconds = 30, string title = "Remote Desktop",
            string requestText = null)
        {
            bool allowed = false;
            using (var done = new ManualResetEventSlim(false))
            {
                var promptThread = new Thread(() =>
                {
                    try
                    {
                        using (var prompt = new FrmRemoteDesktopPermissionPrompt(timeoutSeconds, title, requestText))
                        {
                            allowed = prompt.ShowDialog() == DialogResult.Yes;
                        }
                    }
                    catch
                    {
                        allowed = false;
                    }
                    finally
                    {
                        done.Set();
                    }
                });

                promptThread.SetApartmentState(ApartmentState.STA);
                promptThread.IsBackground = true;
                promptThread.Start();

                // hard cap beyond the form's own auto-deny so the caller can never hang forever
                if (!done.Wait(TimeSpan.FromSeconds(timeoutSeconds + 15)))
                {
                    allowed = false;
                }
            }

            return allowed;
        }
    }
}
