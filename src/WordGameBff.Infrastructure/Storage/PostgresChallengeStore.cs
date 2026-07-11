using WordGameBff.Application.Auth;
using WordGameBff.Domain.Models;

namespace WordGameBff.Infrastructure.Storage;

public sealed class PostgresChallengeStore : IChallengeStore
{
    private const string Namespace = "pow";
    private readonly PostgresKeyValueStore _store;

    public PostgresChallengeStore(PostgresKeyValueStore store)
    {
        _store = store;
    }

    public Task StoreAsync(PowChallenge challenge, CancellationToken cancellationToken = default) =>
        _store.SetAsync(Namespace, challenge.ChallengeId, challenge, challenge.ExpiresAt, cancellationToken);

    public Task<PowChallenge?> GetAsync(string challengeId, CancellationToken cancellationToken = default) =>
        _store.GetAsync<PowChallenge>(Namespace, challengeId, cancellationToken);

    public Task<bool> TryConsumeAsync(string challengeId, CancellationToken cancellationToken = default) =>
        _store.TryDeleteAsync(Namespace, challengeId, cancellationToken);
}
