using MessagePack;
using Pulsar.Common.Messages.Other;

namespace Pulsar.Common.Messages.Monitoring.RemoteDesktop
{
    /// <summary>
    /// Sent by clients that run with the user-facing GUI (builder flag SHOWCLIENTGUI) when the
    /// local user answered the remote-desktop consent prompt. Only transmitted on DENIAL —
    /// an approved session simply starts streaming, so old servers never see this message.
    /// Wire-additive: old peers drop it silently.
    /// </summary>
    [MessagePackObject]
    public class DoRemoteDesktopPermissionResponse : IMessage
    {
        [Key(1)]
        public bool Allowed { get; set; }

        [Key(2)]
        public string Reason { get; set; }
    }
}
