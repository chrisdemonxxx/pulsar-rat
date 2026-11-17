using MessagePack;
using Pulsar.Common.Messages.Other;

namespace Pulsar.Common.Messages.AdminRelay
{
    /// <summary>
    /// Server response to <see cref="DoAdminRelay"/> confirming whether the command
    /// was forwarded to the target client.
    /// </summary>
    [MessagePackObject]
    public class AdminRelayResult : IMessage
    {
        [Key(1)]
        public string TargetId { get; set; }

        [Key(2)]
        public bool Accepted { get; set; }

        [Key(3)]
        public string Reason { get; set; }
    }
}
