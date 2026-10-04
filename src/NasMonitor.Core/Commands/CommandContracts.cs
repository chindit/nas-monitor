namespace NasMonitor.Core.Commands;

public sealed record CommandRequest(
    string Executable,
    IReadOnlyList<string> Arguments,
    TimeSpan Timeout,
    int MaximumStandardOutputBytes = 1_048_576,
    int MaximumStandardErrorBytes = 65_536);

public sealed record CommandResult(
    int ExitCode,
    string StandardOutput,
    string StandardError,
    TimeSpan Duration);

public interface ICommandRunner
{
    Task<CommandResult> RunAsync(
        CommandRequest request,
        CancellationToken cancellationToken);
}
