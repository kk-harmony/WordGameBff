using WordGameBff.Domain.Models;

namespace WordGameBff.Application.Auth;

public interface IChallengeStore
{
    Task StoreAsync(PowChallenge challenge, CancellationToken cancellationToken = default);
    Task<PowChallenge?> GetAsync(string challengeId, CancellationToken cancellationToken = default);
    Task<bool> TryConsumeAsync(string challengeId, CancellationToken cancellationToken = default);
}
