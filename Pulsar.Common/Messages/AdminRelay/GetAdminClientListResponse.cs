using MessagePack;
using Pulsar.Common.Messages.Other;

namespace Pulsar.Common.Messages.AdminRelay
{
    /// <summary>
    /// Server response to <see cref="GetAdminClientList"/>.
    /// </summary>
    [MessagePackObject]
    public class GetAdminClientListResponse : IMessage
    {
        [Key(1)]
        public AdminClientInfo[] Clients { get; set; }
    }
}
