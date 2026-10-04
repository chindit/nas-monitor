namespace NasMonitor.Contracts.Common;

public sealed record ModuleError(
    string Code,
    string Message);
