using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using WordGameBff.Application.Configuration;
using WordGameBff.Domain.Models;

namespace WordGameBff.Application.Auth;

public sealed class PowChallengeService : IPowChallengeService
{
    private readonly IChallengeStore _challengeStore;
    private readonly ISessionTokenService _sessionTokenService;
    private readonly PowOptions _options;

    public PowChallengeService(
        IChallengeStore challengeStore,
        ISessionTokenService sessionTokenService,
        IOptions<PowOptions> options)
    {
        _challengeStore = challengeStore;
        _sessionTokenService = sessionTokenService;
        _options = options.Value;
    }

    public async Task<PowChallenge> CreateChallengeAsync(CancellationToken cancellationToken = default)
    {
        var challenge = new PowChallenge
        {
            ChallengeId = Guid.NewGuid().ToString("N"),
            Prefix = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant(),
            Difficulty = _options.DifficultyBits,
            ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(_options.ChallengeExpirySeconds)
        };

        await _challengeStore.StoreAsync(challenge, cancellationToken);
        return challenge;
    }

    public async Task<SessionTokenResult> VerifyAsync(
        string challengeId,
        string nonce,
        string? userId = null,
        CancellationToken cancellationToken = default)
    {
        var challenge = await _challengeStore.GetAsync(challengeId, cancellationToken);
        if (challenge is null)
        {
            throw new PowVerificationException("CHALLENGE_NOT_FOUND", "Challenge not found.");
        }

        if (challenge.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            throw new PowVerificationException("CHALLENGE_EXPIRED", "Challenge has expired.");
        }

        if (!await _challengeStore.TryConsumeAsync(challengeId, cancellationToken))
        {
            throw new PowVerificationException("CHALLENGE_REUSED", "Challenge has already been used.");
        }

        if (!VerifyProof(challenge.Prefix, nonce, challenge.Difficulty))
        {
            throw new PowVerificationException("INVALID_NONCE", "Proof of work verification failed.");
        }

        return _sessionTokenService.CreateToken(ResolveSessionUserId(userId));
    }

    /// <summary>Reuse a browser-stored GUID identity when valid; otherwise mint a new one.</summary>
    public static string ResolveSessionUserId(string? requestedUserId) =>
        Guid.TryParse(requestedUserId, out var parsed)
            ? parsed.ToString()
            : Guid.NewGuid().ToString();

    public static bool VerifyProof(string prefix, string nonce, int difficultyBits)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(prefix + nonce));
        return CountLeadingZeroBits(hash) >= difficultyBits;
    }

    internal static int CountLeadingZeroBits(byte[] hash)
    {
        var count = 0;
        foreach (var b in hash)
        {
            if (b == 0)
            {
                count += 8;
                continue;
            }

            for (var bit = 7; bit >= 0; bit--)
            {
                if ((b & (1 << bit)) == 0)
                {
                    count++;
                }
                else
                {
                    return count;
                }
            }
        }

        return count;
    }
}

public sealed class PowVerificationException : Exception
{
    public string ErrorCode { get; }

    public PowVerificationException(string errorCode, string message) : base(message)
    {
        ErrorCode = errorCode;
    }
}
