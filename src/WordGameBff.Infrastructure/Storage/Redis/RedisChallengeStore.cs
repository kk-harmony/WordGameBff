using System.Text.Json;
using WordGameBff.Application.Auth;
using WordGameBff.Domain.Models;

namespace WordGameBff.Infrastructure.Storage.Redis;

public sealed class RedisChallengeStore : IChallengeStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IBffRedisDatabase _redis;

    public RedisChallengeStore(IBffRedisDatabase redis)
    {
        _redis = redis;
    }

    public async Task StoreAsync(PowChallenge challenge, CancellationToken cancellationToken = default)
    {
        var ttl = challenge.ExpiresAt - DateTimeOffset.UtcNow;
        if (ttl <= TimeSpan.Zero)
        {
            return;
        }

        var json = JsonSerializer.Serialize(challenge, JsonOptions);
        await _redis.SetStringAsync(Key(challenge.ChallengeId), json, ttl, cancellationToken);
    }

    public async Task<PowChallenge?> GetAsync(string challengeId, CancellationToken cancellationToken = default)
    {
        var json = await _redis.GetStringAsync(Key(challengeId), cancellationToken);
        return json is null ? null : JsonSerializer.Deserialize<PowChallenge>(json, JsonOptions);
    }

    public async Task<bool> TryConsumeAsync(string challengeId, CancellationToken cancellationToken = default)
    {
        var json = await _redis.GetDeleteStringAsync(Key(challengeId), cancellationToken);
        return json is not null;
    }

    private static string Key(string challengeId) => $"bff:pow:{challengeId}";
}
