namespace NasMonitor.Core.Services;

public sealed class DashboardCollectionGate : IDisposable
{
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    public async Task<bool> TryEnterAsync(TimeSpan timeout, CancellationToken cancellationToken) =>
        await _semaphore.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);

    public void Exit() => _semaphore.Release();

    public void Dispose() => _semaphore.Dispose();
}
