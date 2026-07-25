using WordGameBff.Application.Games;

namespace WordGameBff.Application.Realtime;

/// <summary>
/// Single place that applies cache side-effects so publishers, listeners, and create
/// share one policy: seed from raw JSON when the event carries a body, otherwise evict
/// anything older than the event's revision.
/// </summary>
public static class GameSnapshotCacheSync
{
    public static void Apply(IGameSnapshotCache cache, GameRealtimeEnvelope envelope)
    {
        var gameId = envelope.Notification.GameId;
        var revision = envelope.Notification.Revision;

        if (!string.IsNullOrWhiteSpace(envelope.SnapshotJson))
        {
            cache.Set(gameId, envelope.SnapshotJson, revision);
            return;
        }

        if (envelope.InvalidateCache)
        {
            cache.InvalidateOlderThan(gameId, revision);
        }
    }

    public static void Seed(IGameSnapshotCache cache, long gameId, long revision, string rawJson) =>
        cache.Set(gameId, rawJson, revision);
}
