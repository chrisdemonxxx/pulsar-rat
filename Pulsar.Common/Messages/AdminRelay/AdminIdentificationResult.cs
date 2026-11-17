using MessagePack;
using Pulsar.Common.Messages.Other;

namespace Pulsar.Common.Messages.AdminRelay
{
    /// <summary>
    /// Server response to <see cref="AdminIdentification"/>. On acceptance the
    /// current list of managed clients is included so the admin can start working
    /// without a second round-trip.
    /// </summary>
    [MessagePackObject]
    public class AdminIdentificationResult : IMessage
    {
        [Key(1)]
        public bool Accepted { get; set; }

        [Key(2)]
        public string DeniedReason { get; set; }

        [Key(3)]
        public AdminClientInfo[] Clients { get; set; }
    }
}
