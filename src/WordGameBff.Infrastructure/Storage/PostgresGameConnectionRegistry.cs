using WordGameBff.Application.Realtime;

namespace WordGameBff.Infrastructure.Storage;

public sealed class PostgresGameConnectionRegistry : IGameConnectionRegistry
{
    private const string Namespace = "conn";
    private static readonly TimeSpan PresenceTtl = TimeSpan.FromMinutes(5);

    private readonly PostgresKeyValueStore _store;

    public PostgresGameConnectionRegistry(PostgresKeyValueStore store)
    {
        _store = store;
    }

    private sealed record ConnectionEntry(
        [property: System.Text.Json.Serialization.JsonPropertyName("userId")] string UserId,
        [property: System.Text.Json.Serialization.JsonPropertyName("gameId")] string GameId);

    public async Task<bool> TryRegisterAsync(
        string connectionId,
        string userId,
        long gameId,
        CancellationToken cancellationToken = default)
    {
        await _store.SetAsync(
            Namespace,
            connectionId,
            new ConnectionEntry(userId, gameId.ToString()),
            DateTimeOffset.UtcNow.Add(PresenceTtl),
            cancellationToken);
        return true;
    }

    public Task UnregisterAsync(string connectionId, CancellationToken cancellationToken = default) =>
        _store.TryDeleteAsync(Namespace, connectionId, cancellationToken);

    public async Task RefreshAsync(string connectionId, CancellationToken cancellationToken = default)
    {
        var entry = await _store.GetAsync<ConnectionEntry>(Namespace, connectionId, cancellationToken);
        if (entry is null)
        {
            return;
        }

        await _store.SetAsync(
            Namespace,
            connectionId,
            entry,
            DateTimeOffset.UtcNow.Add(PresenceTtl),
            cancellationToken);
    }

    public Task<int> GetConnectionCountForUserAsync(string userId, CancellationToken cancellationToken = default) =>
        _store.CountByNamespaceAndJsonFieldAsync(Namespace, "userId", userId, cancellationToken);

    public Task<bool> IsUserConnectedToGameAsync(string userId, long gameId, CancellationToken cancellationToken = default) =>
        _store.ExistsByNamespaceAndJsonFieldsAsync(
            Namespace,
            new Dictionary<string, string>
            {
                ["userId"] = userId,
                ["gameId"] = gameId.ToString(),
            },
            cancellationToken);
}
