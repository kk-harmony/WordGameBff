using Equinoctial.Redis;

namespace WordGameBff.Infrastructure.Storage.Redis;

public sealed class EquinoctialBffRedisDatabase : IBffRedisDatabase
{
    private readonly RedisClient _client;

    public EquinoctialBffRedisDatabase(RedisClient client)
    {
        _client = client;
    }

    public async Task SetStringAsync(
        string key,
        string value,
        TimeSpan? ttl = null,
        CancellationToken cancellationToken = default)
    {
        if (ttl is { } lifetime)
        {
            await _client.Strings.SetAsync(key, value, lifetime, cancellationToken);
            return;
        }

        await _client.Strings.SetAsync(key, value, cancellationToken);
    }

    public async Task<string?> GetStringAsync(string key, CancellationToken cancellationToken = default) =>
        await _client.Strings.GetAsync(key, cancellationToken);

    public async Task<string?> GetDeleteStringAsync(string key, CancellationToken cancellationToken = default) =>
        await _client.Strings.GetDelAsync(key, cancellationToken);

    public async Task<bool> KeyExistsAsync(string key, CancellationToken cancellationToken = default)
    {
        var count = await _client.Keys.ExistsAsync([key], cancellationToken);
        return count > 0;
    }

    public async Task<bool> DeleteKeyAsync(string key, CancellationToken cancellationToken = default)
    {
        var deleted = await _client.Keys.DelAsync([key], cancellationToken);
        return deleted > 0;
    }

    public async Task ExpireAsync(string key, TimeSpan ttl, CancellationToken cancellationToken = default) =>
        await _client.Keys.ExpireAsync(key, ttl, cancellationToken);

    public async Task<long> IncrementAsync(string key, CancellationToken cancellationToken = default) =>
        await _client.Strings.IncrAsync(key, cancellationToken);

    public async Task SetAddAsync(string key, string member, CancellationToken cancellationToken = default) =>
        await _client.Sets.SAddAsync(key, [member], cancellationToken);

    public async Task SetRemoveAsync(string key, string member, CancellationToken cancellationToken = default) =>
        await _client.Sets.SRemAsync(key, [member], cancellationToken);

    public async Task<IReadOnlyList<string>> SetMembersAsync(
        string key,
        CancellationToken cancellationToken = default)
    {
        var members = await _client.Sets.SMembersAsync(key, cancellationToken);
        return members ?? Array.Empty<string>();
    }
}
