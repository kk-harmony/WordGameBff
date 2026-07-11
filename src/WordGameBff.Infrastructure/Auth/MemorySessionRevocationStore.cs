using Microsoft.Extensions.Caching.Memory;
using WordGameBff.Application.Auth;

namespace WordGameBff.Infrastructure.Auth;

public sealed class MemorySessionRevocationStore : ISessionRevocationStore
{
    private readonly IMemoryCache _cache;

    public MemorySessionRevocationStore(IMemoryCache cache)
    {
        _cache = cache;
    }

    public Task RevokeAsync(string sessionId, DateTimeOffset expiresAt, CancellationToken cancellationToken = default)
    {
        var ttl = expiresAt - DateTimeOffset.UtcNow;
        if (ttl > TimeSpan.Zero)
        {
            _cache.Set(GetCacheKey(sessionId), true, ttl);
        }

        return Task.CompletedTask;
    }

    public Task<bool> IsRevokedAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_cache.TryGetValue(GetCacheKey(sessionId), out _));
    }

    private static string GetCacheKey(string sessionId) => $"revoked:{sessionId}";
}
