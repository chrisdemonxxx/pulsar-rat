using Pulsar.Client.Config;
using System;
using System.Drawing;
using System.Windows.Forms;

namespace Pulsar.Client.GUI
{
    /// <summary>
    /// Read-only status window showing the current client configuration and connection state.
    /// </summary>
    internal sealed class FrmClientStatus : Form
    {
        private readonly Font _captionFont;
        private readonly Label _connectionValue;

        public FrmClientStatus()
        {
            Text = "Pulsar Status";
            FormBorderStyle = FormBorderStyle.FixedToolWindow;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(400, 300);
            MinimizeBox = false;
            MaximizeBox = false;

            _captionFont = new Font(Font, FontStyle.Bold);

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 8,
                Padding = new Padding(10)
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120F));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

            _connectionValue = AddRow(layout, 0, "Connection:", "unknown");
            AddRow(layout, 1, "Servers:", Settings.HOSTS);
            AddRow(layout, 2, "Tag:", Settings.TAG);
            AddRow(layout, 3, "Version:", Settings.ReportedVersion);
            AddRow(layout, 4, "Keylogger active:", Settings.ENABLELOGGER ? "yes" : "no");
            AddRow(layout, 5, "Install path:", Settings.INSTALLPATH);
            AddRow(layout, 6, "Current exe:", Application.ExecutablePath);

            var closeButton = new Button
            {
                Text = "Close",
                Anchor = AnchorStyles.Right
            };
            closeButton.Click += (s, e) => Close();
            layout.Controls.Add(closeButton, 1, 7);

            Controls.Add(layout);
            AcceptButton = closeButton;
        }

        private Label AddRow(TableLayoutPanel layout, int row, string caption, string value)
        {
            var captionLabel = new Label
            {
                Text = caption,
                Font = _captionFont,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft
            };
            var valueLabel = new Label
            {
                Text = string.IsNullOrEmpty(value) ? "-" : value,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true
            };
            layout.Controls.Add(captionLabel, 0, row);
            layout.Controls.Add(valueLabel, 1, row);
            return valueLabel;
        }

        public void RefreshState(bool connected)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action<bool>(RefreshState), connected);
                return;
            }

            _connectionValue.Text = connected ? "Connected" : "Disconnected";
            _connectionValue.ForeColor = connected ? Color.DarkGreen : Color.DarkRed;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _captionFont?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
