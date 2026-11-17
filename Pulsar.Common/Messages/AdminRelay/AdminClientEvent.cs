using MessagePack;
using Pulsar.Common.Messages.Other;

namespace Pulsar.Common.Messages.AdminRelay
{
    /// <summary>
    /// Pushed to all accepted admin clients whenever a managed client connects or
    /// disconnects. Admin clients themselves are not included in these events.
    /// </summary>
    [MessagePackObject]
    public class AdminClientEvent : IMessage
    {
        [Key(1)]
        public bool Connected { get; set; }

        [Key(2)]
        public AdminClientInfo Client { get; set; }
    }
}
