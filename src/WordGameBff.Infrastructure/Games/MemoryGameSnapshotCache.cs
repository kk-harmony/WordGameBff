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
        if (_cache.TryGetValue(GetKey(gameId), out CachedGameSnapshot? cached) && cached is not null)
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

        var key = GetKey(gameId);
        lock (_writeGate)
        {
            if (_cache.TryGetValue(key, out CachedGameSnapshot? existing)
                && existing is not null
                && existing.Revision > revision)
            {
                return;
            }

            _cache.Set(
                key,
                new CachedGameSnapshot
                {
                    RawJson = rawJson,
                    Revision = revision,
                },
                _ttl);
        }
    }

    public void Invalidate(long gameId)
    {
        lock (_writeGate)
        {
            _cache.Remove(GetKey(gameId));
        }
    }

    private static string GetKey(long gameId) => $"game-snapshot:{gameId}";
}
