using WordGameBff.Application.Games;

namespace WordGameBff.Application.Realtime;

/// <summary>
/// Single place that applies cache side-effects so publishers, listeners, and create
/// share one policy: seed from raw JSON, invalidate only when explicitly requested.
/// </summary>
public static class GameSnapshotCacheSync
{
    public static void Apply(IGameSnapshotCache cache, GameRealtimeEnvelope envelope)
    {
        var gameId = envelope.Notification.GameId;
        if (envelope.InvalidateCache)
        {
            cache.Invalidate(gameId);
            return;
        }

        if (!string.IsNullOrWhiteSpace(envelope.SnapshotJson))
        {
            cache.Set(gameId, envelope.SnapshotJson, envelope.Notification.Revision);
        }
    }

    public static void Seed(IGameSnapshotCache cache, long gameId, long revision, string rawJson) =>
        cache.Set(gameId, rawJson, revision);
}
