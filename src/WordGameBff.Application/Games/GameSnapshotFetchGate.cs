namespace WordGameBff.Application.Games;

/// <summary>
/// Process-wide singleflight gate so concurrent cache misses for one game collapse
/// into a single upstream fetch across request scopes.
/// </summary>
public interface IGameSnapshotFetchGate
{
    Task<T> RunAsync<T>(long gameId, Func<CancellationToken, Task<T>> work, CancellationToken cancellationToken);
}

public sealed class GameSnapshotFetchGate : IGameSnapshotFetchGate
{
    private readonly Dictionary<long, SemaphoreSlim> _locks = new();
    private readonly object _gate = new();

    public async Task<T> RunAsync<T>(
        long gameId,
        Func<CancellationToken, Task<T>> work,
        CancellationToken cancellationToken)
    {
        var semaphore = GetLock(gameId);
        await semaphore.WaitAsync(cancellationToken);
        try
        {
            return await work(cancellationToken);
        }
        finally
        {
            semaphore.Release();
        }
    }

    private SemaphoreSlim GetLock(long gameId)
    {
        lock (_gate)
        {
            if (!_locks.TryGetValue(gameId, out var semaphore))
            {
                semaphore = new SemaphoreSlim(1, 1);
                _locks[gameId] = semaphore;
            }

            return semaphore;
        }
    }
}
