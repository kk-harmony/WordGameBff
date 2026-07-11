using System.Collections.Concurrent;
using Microsoft.Extensions.Caching.Memory;
using WordGameBff.Application.Auth;
using WordGameBff.Domain.Models;

namespace WordGameBff.Infrastructure.Auth;

public sealed class MemoryChallengeStore : IChallengeStore
{
    private readonly IMemoryCache _cache;
    private readonly ConcurrentDictionary<string, byte> _consumed = new();

    public MemoryChallengeStore(IMemoryCache cache)
    {
        _cache = cache;
    }

    public Task StoreAsync(PowChallenge challenge, CancellationToken cancellationToken = default)
    {
        var ttl = challenge.ExpiresAt - DateTimeOffset.UtcNow;
        if (ttl <= TimeSpan.Zero)
        {
            return Task.CompletedTask;
        }

        _cache.Set(GetCacheKey(challenge.ChallengeId), challenge, ttl);
        return Task.CompletedTask;
    }

    public Task<PowChallenge?> GetAsync(string challengeId, CancellationToken cancellationToken = default)
    {
        _cache.TryGetValue(GetCacheKey(challengeId), out PowChallenge? challenge);
        return Task.FromResult(challenge);
    }

    public Task<bool> TryConsumeAsync(string challengeId, CancellationToken cancellationToken = default)
    {
        if (!_cache.TryGetValue(GetCacheKey(challengeId), out PowChallenge? challenge) || challenge is null)
        {
            return Task.FromResult(false);
        }

        if (!_consumed.TryAdd(challengeId, 0))
        {
            return Task.FromResult(false);
        }

        _cache.Remove(GetCacheKey(challengeId));
        return Task.FromResult(true);
    }

    private static string GetCacheKey(string challengeId) => $"pow:{challengeId}";
}
