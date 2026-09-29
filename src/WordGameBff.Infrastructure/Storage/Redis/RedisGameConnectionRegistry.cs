using System.Text.Json;
using System.Text.Json.Serialization;
using WordGameBff.Application.Realtime;

namespace WordGameBff.Infrastructure.Storage.Redis;

public sealed class RedisGameConnectionRegistry : IGameConnectionRegistry
{
    private static readonly TimeSpan PresenceTtl = TimeSpan.FromMinutes(5);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IBffRedisDatabase _redis;

    public RedisGameConnectionRegistry(IBffRedisDatabase redis)
    {
        _redis = redis;
    }

    private sealed record ConnectionEntry(
        [property: JsonPropertyName("userId")] string UserId,
        [property: JsonPropertyName("gameId")] string GameId);

    public async Task<bool> TryRegisterAsync(
        string connectionId,
        string userId,
        long gameId,
        CancellationToken cancellationToken = default)
    {
        var entry = new ConnectionEntry(userId, gameId.ToString());
        var json = JsonSerializer.Serialize(entry, JsonOptions);
        await _redis.SetStringAsync(ConnKey(connectionId), json, PresenceTtl, cancellationToken);
        await _redis.SetAddAsync(UserSetKey(userId), connectionId, cancellationToken);
        await _redis.SetAddAsync(GameSetKey(gameId), connectionId, cancellationToken);
        return true;
    }

    public async Task UnregisterAsync(string connectionId, CancellationToken cancellationToken = default)
    {
        var entry = await ReadEntryAsync(connectionId, cancellationToken);
        await _redis.DeleteKeyAsync(ConnKey(connectionId), cancellationToken);
        if (entry is null)
        {
            return;
        }

        await _redis.SetRemoveAsync(UserSetKey(entry.UserId), connectionId, cancellationToken);
        if (long.TryParse(entry.GameId, out var gameId))
        {
            await _redis.SetRemoveAsync(GameSetKey(gameId), connectionId, cancellationToken);
        }
    }

    public async Task RefreshAsync(string connectionId, CancellationToken cancellationToken = default)
    {
        var entry = await ReadEntryAsync(connectionId, cancellationToken);
        if (entry is null)
        {
            return;
        }

        await _redis.ExpireAsync(ConnKey(connectionId), PresenceTtl, cancellationToken);
    }

    public async Task<int> GetConnectionCountForUserAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        var live = await LiveConnectionIdsAsync(UserSetKey(userId), cancellationToken);
        return live.Count;
    }

    public async Task<bool> IsUserConnectedToGameAsync(
        string userId,
        long gameId,
        CancellationToken cancellationToken = default)
    {
        var connectionIds = await LiveConnectionIdsAsync(GameSetKey(gameId), cancellationToken);
        foreach (var connectionId in connectionIds)
        {
            var entry = await ReadEntryAsync(connectionId, cancellationToken);
            if (entry is not null && string.Equals(entry.UserId, userId, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    public async Task<IReadOnlyList<string>> GetConnectedUserIdsForGameAsync(
        long gameId,
        CancellationToken cancellationToken = default)
    {
        var connectionIds = await LiveConnectionIdsAsync(GameSetKey(gameId), cancellationToken);
        var users = new HashSet<string>(StringComparer.Ordinal);
        foreach (var connectionId in connectionIds)
        {
            var entry = await ReadEntryAsync(connectionId, cancellationToken);
            if (entry is not null)
            {
                users.Add(entry.UserId);
            }
        }

        return users.ToList();
    }

    public async Task<bool> HasConnectionsForGameAsync(long gameId, CancellationToken cancellationToken = default)
    {
        var live = await LiveConnectionIdsAsync(GameSetKey(gameId), cancellationToken);
        return live.Count > 0;
    }

    private async Task<ConnectionEntry?> ReadEntryAsync(string connectionId, CancellationToken cancellationToken)
    {
        var json = await _redis.GetStringAsync(ConnKey(connectionId), cancellationToken);
        return json is null ? null : JsonSerializer.Deserialize<ConnectionEntry>(json, JsonOptions);
    }

    private async Task<List<string>> LiveConnectionIdsAsync(string setKey, CancellationToken cancellationToken)
    {
        var members = await _redis.SetMembersAsync(setKey, cancellationToken);
        var live = new List<string>(members.Count);
        foreach (var connectionId in members)
        {
            if (await _redis.KeyExistsAsync(ConnKey(connectionId), cancellationToken))
            {
                live.Add(connectionId);
            }
            else
            {
                await _redis.SetRemoveAsync(setKey, connectionId, cancellationToken);
            }
        }

        return live;
    }

    private static string ConnKey(string connectionId) => $"bff:conn:{connectionId}";

    private static string UserSetKey(string userId) => $"bff:conn:user:{userId}";

    private static string GameSetKey(long gameId) => $"bff:conn:game:{gameId}";
}
