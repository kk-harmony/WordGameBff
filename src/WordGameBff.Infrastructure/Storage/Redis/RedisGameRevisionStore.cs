using WordGameBff.Application.Realtime;

namespace WordGameBff.Infrastructure.Storage.Redis;

public sealed class RedisGameRevisionStore : IGameRevisionStore
{
    private readonly IBffRedisDatabase _redis;

    public RedisGameRevisionStore(IBffRedisDatabase redis)
    {
        _redis = redis;
    }

    public async Task<long> GetCurrentRevisionAsync(long gameId, CancellationToken cancellationToken = default)
    {
        var raw = await _redis.GetStringAsync(Key(gameId), cancellationToken);
        return long.TryParse(raw, out var revision) ? revision : 0L;
    }

    public Task<long> GetNextRevisionAsync(long gameId, CancellationToken cancellationToken = default) =>
        _redis.IncrementAsync(Key(gameId), cancellationToken);

    private static string Key(long gameId) => $"bff:rev:{gameId}";
}
