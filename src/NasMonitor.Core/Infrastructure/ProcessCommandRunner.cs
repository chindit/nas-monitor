using System.Diagnostics;
using System.Text;
using NasMonitor.Core.Commands;

namespace NasMonitor.Core.Infrastructure;

public sealed class ProcessCommandRunner : ICommandRunner
{
    private readonly IReadOnlySet<string>? _testAllowlist;

    public ProcessCommandRunner()
    {
    }

    internal ProcessCommandRunner(IReadOnlySet<string> testAllowlist) => _testAllowlist = testAllowlist;


    public async Task<CommandResult> RunAsync(CommandRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!CommandAllowlist.IsAllowed(request.Executable) && _testAllowlist?.Contains(request.Executable) != true)
        {
            throw new ArgumentException("The executable is not allowlisted.", nameof(request));
        }

        if (request.Timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "Timeout must be positive.");
        }

        if (request.MaximumStandardOutputBytes <= 0 || request.MaximumStandardErrorBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "Output limits must be positive.");
        }

        var stopwatch = Stopwatch.StartNew();
        var psi = new ProcessStartInfo
        {
            FileName = request.Executable,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        psi.Environment["LC_ALL"] = "C";
        psi.Environment["LANG"] = "C";

        foreach (var arg in request.Arguments)
        {
            psi.ArgumentList.Add(arg);
        }

        using var process = new Process { StartInfo = psi };
        using var executionCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        executionCts.CancelAfter(request.Timeout);

        process.Start();

        var outputTask = ReadStreamAndCancelOnFailureAsync(
            process.StandardOutput,
            request.MaximumStandardOutputBytes,
            executionCts);
        var errorTask = ReadStreamAndCancelOnFailureAsync(
            process.StandardError,
            request.MaximumStandardErrorBytes,
            executionCts);

        try
        {
            await process.WaitForExitAsync(executionCts.Token).ConfigureAwait(false);
            var output = await outputTask.ConfigureAwait(false);
            var error = await errorTask.ConfigureAwait(false);
            stopwatch.Stop();
            return new CommandResult(process.ExitCode, output, error, stopwatch.Elapsed);
        }
        catch
        {
            await KillProcessTreeAsync(process).ConfigureAwait(false);

            if (outputTask.IsFaulted && outputTask.Exception?.GetBaseException() is ProcessOutputLimitExceededException outputException)
            {
                throw outputException;
            }

            if (errorTask.IsFaulted && errorTask.Exception?.GetBaseException() is ProcessOutputLimitExceededException errorException)
            {
                throw errorException;
            }

            cancellationToken.ThrowIfCancellationRequested();

            if (executionCts.IsCancellationRequested)
            {
                throw new TimeoutException("The command timed out.");
            }

            throw;
        }
    }

    private static async Task<string> ReadStreamAndCancelOnFailureAsync(
        StreamReader reader,
        int maximumBytes,
        CancellationTokenSource executionCts)
    {
        try
        {
            var builder = new StringBuilder();
            var buffer = new char[4096];
            var bytesRead = 0;

            while (true)
            {
                var charactersRead = await reader.ReadAsync(
                    buffer.AsMemory(),
                    executionCts.Token).ConfigureAwait(false);
                if (charactersRead == 0)
                {
                    return builder.ToString();
                }

                bytesRead = checked(bytesRead + reader.CurrentEncoding.GetByteCount(buffer, 0, charactersRead));
                if (bytesRead > maximumBytes)
                {
                    throw new ProcessOutputLimitExceededException("Process output exceeded its configured limit.");
                }

                builder.Append(buffer, 0, charactersRead);
            }
        }
        catch
        {
            await executionCts.CancelAsync().ConfigureAwait(false);
            throw;
        }
    }

    private static async Task KillProcessTreeAsync(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
        }
        catch (System.ComponentModel.Win32Exception)
        {
        }

        try
        {
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (InvalidOperationException)
        {
        }
    }
}
