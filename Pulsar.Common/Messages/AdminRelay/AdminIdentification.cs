using MessagePack;
using Pulsar.Common.Messages.Other;

namespace Pulsar.Common.Messages.AdminRelay
{
    /// <summary>
    /// Sent by an admin client right after the regular identification handshake to
    /// request elevated permissions. The payload is SHA-256 (hex) of the configured
    /// admin key. If no admin key is configured server-side the request is denied —
    /// the feature is effectively off until a key is set.
    /// </summary>
    [MessagePackObject]
    public class AdminIdentification : IMessage
    {
        [Key(1)]
        public string AdminKeyHash { get; set; }
    }
}
