using System.ComponentModel;
using NasMonitor.Core.Commands;
using NasMonitor.Core.Monitoring;

namespace NasMonitor.Modules.Commands;

internal static class CommandExecution
{
    public static async Task<CommandResult> RunAsync(
        ICommandRunner commandRunner,
        CommandRequest request,
        CancellationToken cancellationToken,
        bool allowNonZeroExitCode = false)
    {
        try
        {
            var result = await commandRunner.RunAsync(request, cancellationToken).ConfigureAwait(false);
            if (!allowNonZeroExitCode && result.ExitCode != 0)
            {
                var code = result.StandardError.Contains("permission denied", StringComparison.OrdinalIgnoreCase)
                    ? "permission_denied"
                    : "command_failed";
                throw new ModuleCollectionException(code, "A required monitoring command failed.");
            }

            return result;
        }
        catch (ModuleCollectionException)
        {
            throw;
        }
        catch (FileNotFoundException exception)
        {
            throw new ModuleCollectionException("command_not_found", "A required monitoring command is not installed.", exception);
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode is 2 or 3)
        {
            throw new ModuleCollectionException("command_not_found", "A required monitoring command is not installed.", exception);
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode is 5 or 13)
        {
            throw new ModuleCollectionException("permission_denied", "Permission was denied while starting a monitoring command.", exception);
        }
    }
}
