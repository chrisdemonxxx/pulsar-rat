using MessagePack;

namespace Pulsar.Common.Messages.AdminRelay
{
    /// <summary>
    /// Compact view of a connected client as presented to admin clients.
    /// </summary>
    [MessagePackObject]
    public class AdminClientInfo
    {
        [Key(1)]
        public string Id { get; set; }

        [Key(2)]
        public string UserAtPc { get; set; }

        [Key(3)]
        public string Tag { get; set; }

        [Key(4)]
        public string PublicIp { get; set; }

        [Key(5)]
        public string Country { get; set; }

        [Key(6)]
        public string OperatingSystem { get; set; }

        [Key(7)]
        public string AccountType { get; set; }

        [Key(8)]
        public string Version { get; set; }
    }
}
