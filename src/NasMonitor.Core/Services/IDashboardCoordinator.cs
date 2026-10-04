using NasMonitor.Contracts.Dashboard;

namespace NasMonitor.Core.Services;

public interface IDashboardCoordinator
{
    Task<DashboardResponse> CollectAsync(CancellationToken cancellationToken);
}
