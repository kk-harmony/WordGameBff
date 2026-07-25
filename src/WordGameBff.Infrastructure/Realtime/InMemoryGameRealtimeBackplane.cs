using WordGameBff.Application.Games;
using WordGameBff.Application.Realtime;

namespace WordGameBff.Infrastructure.Realtime;

/// <summary>
/// Development backplane: apply cache side-effects and fan out directly (no Postgres listener).
/// </summary>
public sealed class InMemoryGameRealtimeBackplane : IGameRealtimeBackplane
{
    private readonly IGameSnapshotCache _snapshotCache;
    private readonly IGameSnapshotFanout _fanout;

    public InMemoryGameRealtimeBackplane(IGameSnapshotCache snapshotCache, IGameSnapshotFanout fanout)
    {
        _snapshotCache = snapshotCache;
        _fanout = fanout;
    }

    public Task PublishAsync(long gameId, GameRealtimeEnvelope envelope, CancellationToken cancellationToken = default)
    {
        GameSnapshotCacheSync.Apply(_snapshotCache, envelope);
        return _fanout.DispatchAsync(envelope, cancellationToken);
    }
}
