using System.Globalization;
using NasMonitor.Contracts.Media;
using NasMonitor.Core.Commands;
using NasMonitor.Core.Monitoring;
using NasMonitor.Modules.Commands;

namespace NasMonitor.Modules.Media;

public interface IServiceStateReader
{
    Task<ServiceStateSnapshot> ReadAsync(string unit, TimeSpan timeout, CancellationToken cancellationToken);
}

public sealed class ServiceStateReader : IServiceStateReader
{
    private readonly ICommandRunner _commandRunner;

    public ServiceStateReader(ICommandRunner commandRunner) => _commandRunner = commandRunner;

    public async Task<ServiceStateSnapshot> ReadAsync(string unit, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var result = await CommandExecution.RunAsync(
            _commandRunner,
            new CommandRequest(
                "/usr/bin/systemctl",
                ["show", "--no-pager", "--property=LoadState,ActiveState,SubState,Result,ExecMainStatus", "--", unit],
                timeout),
            cancellationToken,
            allowNonZeroExitCode: true).ConfigureAwait(false);
        if (result.ExitCode != 0)
        {
            var code = result.StandardError.Contains("not found", StringComparison.OrdinalIgnoreCase)
                ? "service_not_found"
                : "command_failed";
            throw new ModuleCollectionException(code, "The media service state could not be read.");
        }

        var values = result.StandardOutput
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Split('=', 2))
            .Where(parts => parts.Length == 2)
            .ToDictionary(parts => parts[0], parts => parts[1], StringComparer.Ordinal);
        if (!values.TryGetValue("LoadState", out var loadState) ||
            !values.TryGetValue("ActiveState", out var activeState) ||
            !values.TryGetValue("SubState", out var subState))
        {
            throw new ModuleCollectionException("parse_failed", "The media service state could not be parsed.");
        }

        return new ServiceStateSnapshot(
            unit,
            loadState,
            activeState,
            subState,
            values.GetValueOrDefault("Result"),
            values.TryGetValue("ExecMainStatus", out var status) && int.TryParse(status, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedStatus)
                ? parsedStatus
                : null);
    }
}
