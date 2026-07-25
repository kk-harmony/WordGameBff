using WordGameBff.Domain.Models;

namespace WordGameBff.Application.Auth;

public interface IPowChallengeService
{
    Task<PowChallenge> CreateChallengeAsync(CancellationToken cancellationToken = default);
    Task<SessionTokenResult> VerifyAsync(
        string challengeId,
        string nonce,
        string? userId = null,
        CancellationToken cancellationToken = default);
}
