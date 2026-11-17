using MessagePack;
using Pulsar.Common.Messages.Other;

namespace Pulsar.Common.Messages.AdminRelay
{
    /// <summary>
    /// Requests the current list of managed clients from the server.
    /// Only processed for clients that successfully completed
    /// <see cref="AdminIdentification"/>.
    /// </summary>
    [MessagePackObject]
    public class GetAdminClientList : IMessage
    {
    }
}
