using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using WordGameBff.Application.Configuration;
using WordGameBff.Application.Games;

namespace WordGameBff.Infrastructure.Games;

public sealed class MemoryGameSnapshotCache : IGameSnapshotCache
{
    private readonly IMemoryCache _cache;
    private readonly TimeSpan _ttl;
    private readonly object _writeGate = new();

    public MemoryGameSnapshotCache(IMemoryCache cache, IOptions<GameSnapshotOptions> options)
    {
        _cache = cache;
        var seconds = Math.Max(1, options.Value.CacheTtlSeconds);
        _ttl = TimeSpan.FromSeconds(seconds);
    }

    public bool TryGet(long gameId, out CachedGameSnapshot? snapshot)
    {
        if (_cache.TryGetValue(SnapshotKey(gameId), out CachedGameSnapshot? cached) && cached is not null)
        {
            snapshot = cached;
            return true;
        }

        snapshot = null;
        return false;
    }

    public void Set(long gameId, string rawJson, long revision)
    {
        if (string.IsNullOrWhiteSpace(rawJson))
        {
            return;
        }

        lock (_writeGate)
        {
            if (TryGet(gameId, out var existing) && existing!.Revision > revision)
            {
                return;
            }

            // A body read before the last eviction predates it even when nothing is cached
            // yet, so accepting it would resurrect the state that eviction removed.
            if (revision < EvictedRevision(gameId))
            {
                return;
            }

            _cache.Set(
                SnapshotKey(gameId),
                new CachedGameSnapshot
                {
                    RawJson = rawJson,
                    Revision = revision,
                },
                _ttl);
        }
    }

    public void InvalidateOlderThan(long gameId, long revision)
    {
        lock (_writeGate)
        {
            if (TryGet(gameId, out var existing) && existing!.Revision >= revision)
            {
                return;
            }

            _cache.Remove(SnapshotKey(gameId));
            if (revision > EvictedRevision(gameId))
            {
                _cache.Set(EvictedKey(gameId), revision, _ttl);
            }
        }
    }

    private long EvictedRevision(long gameId) =>
        _cache.TryGetValue(EvictedKey(gameId), out long revision) ? revision : 0;

    private static string SnapshotKey(long gameId) => $"game-snapshot:{gameId}";

    private static string EvictedKey(long gameId) => $"game-snapshot-evicted:{gameId}";
}
