using NasMonitor.Contracts.Common;
using NasMonitor.Contracts.Media;

namespace NasMonitor.Modules.Media;

internal static class MediaStateEvaluator
{
    public static ModuleState Evaluate(ServiceStateSnapshot service)
    {
        if (string.Equals(service.ActiveState, "failed", StringComparison.Ordinal) ||
            string.Equals(service.ActiveState, "inactive", StringComparison.Ordinal))
        {
            return ModuleState.Critical;
        }

        return string.Equals(service.LoadState, "loaded", StringComparison.Ordinal) &&
               string.Equals(service.ActiveState, "active", StringComparison.Ordinal)
            ? ModuleState.Healthy
            : ModuleState.Warning;
    }
}
