namespace Pulsar.Common.Messages.Monitoring.HVNC
{
    public enum HVNCProcessStage
    {
        Queued,
        Validating,
        Cloning,
        Starting,
        Suspended,
        Injecting,
        Resuming,
        Started,
        Completed,
        Cancelled,
        Failed,
        TimedOut
    }
}
