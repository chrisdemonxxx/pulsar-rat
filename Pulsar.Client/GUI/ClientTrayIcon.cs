using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace Pulsar.Client.GUI
{
    /// <summary>
    /// Tray icon with a context menu that opens the client status window.
    /// Only created when the client was built with the "Client GUI" flag.
    /// </summary>
    internal sealed class ClientTrayIcon : IDisposable
    {
        private readonly NotifyIcon _notifyIcon;
        private readonly SynchronizationContext _syncContext;
        private FrmClientStatus _statusForm;
        private bool _hasState;
        private bool _connected;
        private bool _everConnected;
        private bool _disposed;

        public ClientTrayIcon(PulsarApplication owner)
        {
            _syncContext = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();

            var menu = new ContextMenuStrip();
            menu.Items.Add("Status", null, (s, e) => ShowStatusForm());

            _notifyIcon = new NotifyIcon
            {
                Icon = SystemIcons.Application,
                Text = "Pulsar",
                ContextMenuStrip = menu,
                Visible = true
            };
            _notifyIcon.DoubleClick += (s, e) => ShowStatusForm();
        }

        public void ShowBalloon(string title, string text, ToolTipIcon icon, int timeoutMs)
        {
            Post(() => _notifyIcon.ShowBalloonTip(timeoutMs, title, text, icon));
        }

        public void SetConnectionState(bool connected)
        {
            Post(() =>
            {
                bool hadState = _hasState;
                if (_hasState && connected == _connected)
                    return;

                bool firstConnect = connected && !_everConnected;
                _hasState = true;
                _connected = connected;
                if (connected)
                    _everConnected = true;

                _notifyIcon.Text = connected ? "Pulsar - connected" : "Pulsar - disconnected";
                _statusForm?.RefreshState(connected);

                if (firstConnect)
                {
                    _notifyIcon.ShowBalloonTip(5000, "Pulsar", "Pulsar connected - right-click the tray icon for status", ToolTipIcon.Info);
                }
                else if (hadState)
                {
                    _notifyIcon.ShowBalloonTip(3000, "Pulsar", connected ? "Pulsar connected" : "Pulsar disconnected", ToolTipIcon.Info);
                }
            });
        }

        private void ShowStatusForm()
        {
            Post(() =>
            {
                if (_statusForm == null || _statusForm.IsDisposed)
                {
                    _statusForm = new FrmClientStatus();
                    _statusForm.RefreshState(_connected);
                }

                _statusForm.Show();
                _statusForm.Activate();
            });
        }

        private void Post(Action action)
        {
            if (_disposed)
                return;

            _syncContext.Post(_ =>
            {
                if (!_disposed)
                    action();
            }, null);
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _statusForm?.Dispose();
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
        }
    }
}
