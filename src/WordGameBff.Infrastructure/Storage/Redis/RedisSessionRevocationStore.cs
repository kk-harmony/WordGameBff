using WordGameBff.Application.Auth;

namespace WordGameBff.Infrastructure.Storage.Redis;

public sealed class RedisSessionRevocationStore : ISessionRevocationStore
{
    private readonly IBffRedisDatabase _redis;

    public RedisSessionRevocationStore(IBffRedisDatabase redis)
    {
        _redis = redis;
    }

    public async Task RevokeAsync(
        string sessionId,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken = default)
    {
        var ttl = expiresAt - DateTimeOffset.UtcNow;
        if (ttl <= TimeSpan.Zero)
        {
            return;
        }

        await _redis.SetStringAsync(Key(sessionId), "1", ttl, cancellationToken);
    }

    public Task<bool> IsRevokedAsync(string sessionId, CancellationToken cancellationToken = default) =>
        _redis.KeyExistsAsync(Key(sessionId), cancellationToken);

    private static string Key(string sessionId) => $"bff:revoked:{sessionId}";
}
