namespace WordGameBff.Infrastructure.Storage.Redis;

/// <summary>
/// Narrow Redis surface used by BFF shared stores (testable without a live Redis).
/// </summary>
public interface IBffRedisDatabase
{
    Task SetStringAsync(string key, string value, TimeSpan? ttl = null, CancellationToken cancellationToken = default);

    Task<string?> GetStringAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>Atomically get-and-delete. Returns null when the key is missing.</summary>
    Task<string?> GetDeleteStringAsync(string key, CancellationToken cancellationToken = default);

    Task<bool> KeyExistsAsync(string key, CancellationToken cancellationToken = default);

    Task<bool> DeleteKeyAsync(string key, CancellationToken cancellationToken = default);

    Task ExpireAsync(string key, TimeSpan ttl, CancellationToken cancellationToken = default);

    Task<long> IncrementAsync(string key, CancellationToken cancellationToken = default);

    Task SetAddAsync(string key, string member, CancellationToken cancellationToken = default);

    Task SetRemoveAsync(string key, string member, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> SetMembersAsync(string key, CancellationToken cancellationToken = default);
}
