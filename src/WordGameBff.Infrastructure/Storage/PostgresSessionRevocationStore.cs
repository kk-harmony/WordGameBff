using WordGameBff.Application.Auth;

namespace WordGameBff.Infrastructure.Storage;

public sealed class PostgresSessionRevocationStore : ISessionRevocationStore
{
    private const string Namespace = "revoked";
    private readonly PostgresKeyValueStore _store;

    public PostgresSessionRevocationStore(PostgresKeyValueStore store)
    {
        _store = store;
    }

    public Task RevokeAsync(string sessionId, DateTimeOffset expiresAt, CancellationToken cancellationToken = default)
    {
        if (expiresAt <= DateTimeOffset.UtcNow)
        {
            return Task.CompletedTask;
        }

        return _store.SetAsync(Namespace, sessionId, true, expiresAt, cancellationToken);
    }

    public Task<bool> IsRevokedAsync(string sessionId, CancellationToken cancellationToken = default) =>
        _store.ExistsAsync(Namespace, sessionId, cancellationToken);
}
