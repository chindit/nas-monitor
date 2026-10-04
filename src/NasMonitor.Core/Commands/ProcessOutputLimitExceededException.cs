namespace NasMonitor.Core.Commands;

public sealed class ProcessOutputLimitExceededException : Exception
{
    public ProcessOutputLimitExceededException(string message) : base(message) { }
}
