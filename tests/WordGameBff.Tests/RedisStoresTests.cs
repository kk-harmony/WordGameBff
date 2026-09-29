using WordGameBff.Domain.Models;
using WordGameBff.Infrastructure.Storage.Redis;

namespace WordGameBff.Tests;

public class RedisStoresTests
{
    [Fact]
    public async Task ChallengeStore_ConsumesExactlyOnce()
    {
        var redis = new InMemoryBffRedisDatabase();
        var store = new RedisChallengeStore(redis);
        var challenge = new PowChallenge
        {
            ChallengeId = "c1",
            Prefix = "abc",
            Difficulty = 16,
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(5),
        };

        await store.StoreAsync(challenge);
        Assert.NotNull(await store.GetAsync("c1"));
        Assert.True(await store.TryConsumeAsync("c1"));
        Assert.False(await store.TryConsumeAsync("c1"));
        Assert.Null(await store.GetAsync("c1"));
    }

    [Fact]
    public async Task ChallengeStore_SkipsAlreadyExpired()
    {
        var redis = new InMemoryBffRedisDatabase();
        var store = new RedisChallengeStore(redis);
        await store.StoreAsync(new PowChallenge
        {
            ChallengeId = "expired",
            Prefix = "x",
            Difficulty = 16,
            ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(-1),
        });

        Assert.Null(await store.GetAsync("expired"));
    }

    [Fact]
    public async Task SessionRevocation_HonorsTtl()
    {
        var now = DateTimeOffset.UtcNow;
        var redis = new InMemoryBffRedisDatabase(() => now);
        var store = new RedisSessionRevocationStore(redis);

        await store.RevokeAsync("s1", now.AddMinutes(2));
        Assert.True(await store.IsRevokedAsync("s1"));

        now = now.AddMinutes(3);
        Assert.False(await store.IsRevokedAsync("s1"));
    }

    [Fact]
    public async Task RevisionStore_IncrementsFromZero()
    {
        var store = new RedisGameRevisionStore(new InMemoryBffRedisDatabase());
        Assert.Equal(0, await store.GetCurrentRevisionAsync(42));
        Assert.Equal(1, await store.GetNextRevisionAsync(42));
        Assert.Equal(2, await store.GetNextRevisionAsync(42));
        Assert.Equal(2, await store.GetCurrentRevisionAsync(42));
    }

    [Fact]
    public async Task ConnectionRegistry_TracksPresenceAndCleansExpiredIndexes()
    {
        var now = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
        var redis = new InMemoryBffRedisDatabase(() => now);
        var registry = new RedisGameConnectionRegistry(redis);

        await registry.TryRegisterAsync("c1", "u1", 9);
        await registry.TryRegisterAsync("c2", "u2", 9);
        Assert.True(await registry.IsUserConnectedToGameAsync("u1", 9));
        Assert.Equal(2, (await registry.GetConnectedUserIdsForGameAsync(9)).Count);

        now = now.AddMinutes(6);
        Assert.False(await registry.IsUserConnectedToGameAsync("u1", 9));
        Assert.False(await registry.HasConnectionsForGameAsync(9));
        Assert.Empty(await registry.GetConnectedUserIdsForGameAsync(9));
    }

    [Fact]
    public async Task ConnectionRegistry_RefreshExtendsTtl()
    {
        var now = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
        var redis = new InMemoryBffRedisDatabase(() => now);
        var registry = new RedisGameConnectionRegistry(redis);

        await registry.TryRegisterAsync("c1", "u1", 9);
        now = now.AddMinutes(4);
        await registry.RefreshAsync("c1");
        now = now.AddMinutes(4);

        Assert.True(await registry.IsUserConnectedToGameAsync("u1", 9));
    }

    [Fact]
    public async Task ConnectionRegistry_UnregisterRemovesPresence()
    {
        var registry = new RedisGameConnectionRegistry(new InMemoryBffRedisDatabase());
        await registry.TryRegisterAsync("c1", "u1", 9);
        await registry.UnregisterAsync("c1");

        Assert.Equal(0, await registry.GetConnectionCountForUserAsync("u1"));
        Assert.False(await registry.HasConnectionsForGameAsync(9));
    }

    [Fact]
    public async Task SelfVoteStore_AppliesViewerVoteAndClearsOnStatusLeave()
    {
        var store = new RedisGameSelfVoteStore(new InMemoryBffRedisDatabase());
        await store.RecordSelfVoteAsync(7, "viewer", "target");

        var voting = new Game
        {
            Id = 7,
            Name = "g",
            AdminUserId = "admin",
            Status = "VOTING",
            Members =
            [
                new GameMember { UserId = "viewer" },
                new GameMember { UserId = "target" },
            ],
        };

        var applied = await store.ApplyViewerSelfVoteAsync(voting, "viewer");
        Assert.Equal("target", applied.Members!.Single(m => m.UserId == "viewer").VotedForUserId);

        await store.SyncFromUpstreamAsync(new Game
        {
            Id = 7,
            Name = "g",
            AdminUserId = "admin",
            Status = "IN_PROGRESS",
            Members = voting.Members,
        });

        var after = await store.ApplyViewerSelfVoteAsync(voting, "viewer");
        Assert.Null(after.Members!.Single(m => m.UserId == "viewer").VotedForUserId);
    }
}
