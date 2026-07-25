using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using WordGameBff.Application.Configuration;
using WordGameBff.Application.Games;
using WordGameBff.Application.Realtime;
using WordGameBff.Domain.Models;
using WordGameBff.Infrastructure.Games;
using WordGameBff.Infrastructure.Realtime;
using WordGameBff.Infrastructure.Realtime.Postgres;

namespace WordGameBff.Tests;

public class MemoryGameSnapshotCacheTests
{
    [Fact]
    public void Set_DoesNotOverwriteNewerRevision()
    {
        var cache = CreateCache();
        cache.Set(1, """{"id":1}""", revision: 5);
        cache.Set(1, """{"id":1,"stale":true}""", revision: 4);

        Assert.True(cache.TryGet(1, out var snapshot));
        Assert.Equal(5, snapshot!.Revision);
        Assert.DoesNotContain("stale", snapshot.RawJson);
    }

    [Fact]
    public void Set_OverwritesOlderOrEqualRevision()
    {
        var cache = CreateCache();
        cache.Set(1, """{"id":1,"v":1}""", revision: 2);
        cache.Set(1, """{"id":1,"v":2}""", revision: 2);

        Assert.True(cache.TryGet(1, out var snapshot));
        Assert.Contains("\"v\":2", snapshot!.RawJson);
    }

    [Fact]
    public void InvalidateOlderThan_RemovesOlderEntry()
    {
        var cache = CreateCache();
        cache.Set(9, """{"id":9}""", revision: 1);
        cache.InvalidateOlderThan(9, revision: 2);
        Assert.False(cache.TryGet(9, out _));
    }

    [Fact]
    public void InvalidateOlderThan_KeepsEntryAlreadyAtRevision()
    {
        var cache = CreateCache();
        cache.Set(9, """{"id":9,"fresh":true}""", revision: 4);
        cache.InvalidateOlderThan(9, revision: 4);

        Assert.True(cache.TryGet(9, out var snapshot));
        Assert.Contains("fresh", snapshot!.RawJson);
    }

    [Fact]
    public void Set_IgnoresBodyOlderThanLastInvalidation()
    {
        var cache = CreateCache();
        cache.InvalidateOlderThan(9, revision: 6);

        cache.Set(9, """{"id":9,"stale":true}""", revision: 5);
        Assert.False(cache.TryGet(9, out _));

        cache.Set(9, """{"id":9,"current":true}""", revision: 6);
        Assert.True(cache.TryGet(9, out var snapshot));
        Assert.Contains("current", snapshot!.RawJson);
    }

    [Fact]
    public async Task Entry_ExpiresAfterAbsoluteTtl()
    {
        var cache = new MemoryGameSnapshotCache(
            new MemoryCache(new MemoryCacheOptions()),
            Options.Create(new GameSnapshotOptions { CacheTtlSeconds = 1 }));
        cache.Set(1, """{"id":1}""", revision: 1);

        await Task.Delay(TimeSpan.FromMilliseconds(1_200));

        Assert.False(cache.TryGet(1, out _));
    }

    private static MemoryGameSnapshotCache CreateCache() =>
        new(new MemoryCache(new MemoryCacheOptions()), Options.Create(new GameSnapshotOptions
        {
            CacheTtlSeconds = 60,
        }));
}

public class GameSnapshotReaderTests
{
    [Fact]
    public async Task GetGame_ServesMemberFromCacheWithoutUpstream()
    {
        var gameApi = new Mock<IGameApiClient>(MockBehavior.Strict);
        var cache = new MemoryGameSnapshotCache(
            new MemoryCache(new MemoryCacheOptions()),
            Options.Create(new GameSnapshotOptions { CacheTtlSeconds = 60 }));
        cache.Set(
            1,
            """{"id":1,"name":"G","adminUserId":"u1","status":"WAITING","members":[{"userId":"u1","role":"ADMIN"}]}""",
            revision: 3);

        var reader = new GameSnapshotReader(
            gameApi.Object,
            cache,
            new InMemoryGameRevisionStore(),
            new GameSnapshotFetchGate());

        var response = await reader.GetGameAsync("u1", 1);

        Assert.True(response.IsSuccess);
        Assert.Contains("\"id\":1", response.Body);
        gameApi.Verify(x => x.GetGameAsync(It.IsAny<string>(), It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetGame_FallsThroughForNonMemberEvenWhenCached()
    {
        var gameApi = new Mock<IGameApiClient>();
        gameApi.Setup(x => x.GetGameAsync("outsider", 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GameApiResponse { StatusCode = 403, Body = """{"error":"FORBIDDEN"}""" });

        var cache = new MemoryGameSnapshotCache(
            new MemoryCache(new MemoryCacheOptions()),
            Options.Create(new GameSnapshotOptions { CacheTtlSeconds = 60 }));
        cache.Set(
            1,
            """{"id":1,"name":"G","adminUserId":"u1","status":"WAITING","members":[{"userId":"u1","role":"ADMIN"}]}""",
            revision: 3);

        var reader = new GameSnapshotReader(
            gameApi.Object,
            cache,
            new InMemoryGameRevisionStore(),
            new GameSnapshotFetchGate());

        var response = await reader.GetGameAsync("outsider", 1);

        Assert.Equal(403, response.StatusCode);
        gameApi.Verify(x => x.GetGameAsync("outsider", 1, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetGame_CollapsesConcurrentMissesIntoOneUpstreamCall()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var gameApi = new Mock<IGameApiClient>();
        gameApi.Setup(x => x.GetGameAsync("u1", 2, It.IsAny<CancellationToken>()))
            .Returns(async (string _, long _, CancellationToken _) =>
            {
                started.TrySetResult();
                await release.Task;
                return new GameApiResponse
                {
                    StatusCode = 200,
                    Body = """{"id":2,"name":"G","adminUserId":"u1","status":"WAITING","members":[{"userId":"u1","role":"ADMIN"}]}""",
                };
            });

        var reader = new GameSnapshotReader(
            gameApi.Object,
            new MemoryGameSnapshotCache(
                new MemoryCache(new MemoryCacheOptions()),
                Options.Create(new GameSnapshotOptions { CacheTtlSeconds = 60 })),
            new InMemoryGameRevisionStore(),
            new GameSnapshotFetchGate());

        var first = reader.GetGameAsync("u1", 2);
        await started.Task;
        var second = reader.GetGameAsync("u1", 2);
        release.TrySetResult();

        var results = await Task.WhenAll(first, second);
        Assert.All(results, r => Assert.True(r.IsSuccess));
        gameApi.Verify(x => x.GetGameAsync("u1", 2, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetGame_DoesNotOverwriteSnapshotCachedDuringFetch()
    {
        var revisions = new InMemoryGameRevisionStore();
        await revisions.GetNextRevisionAsync(3); // revision = 1

        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var gameApi = new Mock<IGameApiClient>();
        gameApi.Setup(x => x.GetGameAsync("u1", 3, It.IsAny<CancellationToken>()))
            .Returns(async (string _, long _, CancellationToken _) =>
            {
                started.TrySetResult();
                await release.Task;
                return new GameApiResponse
                {
                    StatusCode = 200,
                    Body = """{"id":3,"name":"stale","adminUserId":"u1","status":"WAITING","members":[{"userId":"u1","role":"ADMIN"}]}""",
                };
            });

        var cache = new MemoryGameSnapshotCache(
            new MemoryCache(new MemoryCacheOptions()),
            Options.Create(new GameSnapshotOptions { CacheTtlSeconds = 60 }));
        var reader = new GameSnapshotReader(gameApi.Object, cache, revisions, new GameSnapshotFetchGate());

        var fetch = reader.GetGameAsync("u1", 3);
        await started.Task;
        await revisions.GetNextRevisionAsync(3); // mutation bumps to 2 mid-fetch
        cache.Set(
            3,
            """{"id":3,"name":"fresh","adminUserId":"u1","status":"WAITING","members":[{"userId":"u1","role":"ADMIN"}]}""",
            revision: 2);
        release.TrySetResult();

        var response = await fetch;
        Assert.True(response.IsSuccess);
        Assert.Contains("stale", response.Body);
        Assert.True(cache.TryGet(3, out var snapshot));
        Assert.Contains("fresh", snapshot!.RawJson);
        Assert.Equal(2, snapshot.Revision);
    }
}

public class GameSnapshotFanoutTests
{
    [Fact]
    public async Task Dispatch_SendsPerViewerSanitizedPayloadsDuringVoting()
    {
        var transport = new Mock<IGameRealtimeTransport>();
        var published = new List<(string UserId, GameRealtimeMessage Message)>();
        transport.Setup(x => x.PublishToUserInGameAsync(
                It.IsAny<long>(),
                It.IsAny<string>(),
                It.IsAny<GameRealtimeMessage>(),
                It.IsAny<CancellationToken>()))
            .Callback<long, string, GameRealtimeMessage, CancellationToken>((_, userId, message, _) =>
                published.Add((userId, message)))
            .Returns(Task.CompletedTask);

        var selfVotes = new Mock<IGameSelfVoteStore>();
        selfVotes.Setup(x => x.SyncFromUpstreamAsync(It.IsAny<Game>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        selfVotes.Setup(x => x.ApplyViewerSelfVoteAsync(It.IsAny<Game>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Game game, string _, CancellationToken _) => game);

        var presence = new Mock<IGamePresenceEnricher>();
        presence.Setup(x => x.EnrichAsync(It.IsAny<Game>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Game game, CancellationToken _) => game);

        var fanout = new GameSnapshotFanout(
            transport.Object,
            new GameSanitizer(),
            presence.Object,
            selfVotes.Object,
            NullLogger<GameSnapshotFanout>.Instance);

        var snapshot = new Game
        {
            Id = 10,
            Name = "Vote",
            AdminUserId = "admin",
            Status = "VOTING",
            ImpostorUserId = "impostor",
            Members =
            [
                new GameMember { UserId = "admin", Role = "ADMIN", VotedForUserId = "p2" },
                new GameMember { UserId = "p2", Role = "PLAYER", VotedForUserId = "admin" },
            ],
        };

        await fanout.DispatchAsync(new GameRealtimeEnvelope
        {
            Notification = new GameRealtimeMessage
            {
                Type = "gameChanged",
                GameId = 10,
                Revision = 4,
                Action = "vote",
            },
            Snapshot = snapshot,
        });

        Assert.Equal(2, published.Count);
        var adminView = published.Single(p => p.UserId == "admin").Message.Game!;
        var p2View = published.Single(p => p.UserId == "p2").Message.Game!;

        Assert.Null(adminView.ImpostorUserId);
        Assert.Equal("p2", adminView.Members!.Single(m => m.UserId == "admin").VotedForUserId);
        Assert.Null(adminView.Members!.Single(m => m.UserId == "p2").VotedForUserId);

        Assert.Null(p2View.ImpostorUserId);
        Assert.Equal("admin", p2View.Members!.Single(m => m.UserId == "p2").VotedForUserId);
        Assert.Null(p2View.Members!.Single(m => m.UserId == "admin").VotedForUserId);
        transport.Verify(
            x => x.PublishToGameAsync(It.IsAny<long>(), It.IsAny<GameRealtimeMessage>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Dispatch_WithoutSnapshot_PublishesLightweightGroupMessage()
    {
        var transport = new Mock<IGameRealtimeTransport>();
        transport.Setup(x => x.PublishToGameAsync(It.IsAny<long>(), It.IsAny<GameRealtimeMessage>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var fanout = new GameSnapshotFanout(
            transport.Object,
            new GameSanitizer(),
            Mock.Of<IGamePresenceEnricher>(),
            Mock.Of<IGameSelfVoteStore>(),
            NullLogger<GameSnapshotFanout>.Instance);

        await fanout.DispatchAsync(new GameRealtimeEnvelope
        {
            Notification = new GameRealtimeMessage
            {
                Type = "gameChanged",
                GameId = 3,
                Revision = 1,
                Action = "leave",
            },
        });

        transport.Verify(
            x => x.PublishToGameAsync(
                3,
                It.Is<GameRealtimeMessage>(m => m.Game == null && m.Action == "leave"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }
}

public class GameEventPublisherSnapshotTests
{
    [Fact]
    public async Task PublishGameChanged_WithSnapshot_SeedsCacheAndPublishesEnvelope()
    {
        GameRealtimeEnvelope? published = null;
        var backplane = new Mock<IGameRealtimeBackplane>();
        backplane.Setup(x => x.PublishAsync(42, It.IsAny<GameRealtimeEnvelope>(), It.IsAny<CancellationToken>()))
            .Callback<long, GameRealtimeEnvelope, CancellationToken>((_, envelope, _) => published = envelope)
            .Returns(Task.CompletedTask);

        var cache = new MemoryGameSnapshotCache(
            new MemoryCache(new MemoryCacheOptions()),
            Options.Create(new GameSnapshotOptions { CacheTtlSeconds = 60, PushEnabled = true }));

        var publisher = new GameEventPublisher(
            backplane.Object,
            new InMemoryGameRevisionStore(),
            cache,
            Options.Create(new GameSnapshotOptions { PushEnabled = true, CacheTtlSeconds = 60 }),
            NullLogger<GameEventPublisher>.Instance);

        await publisher.PublishGameChangedAsync(42, "u1", GameChangeActions.Join, """{"id":42,"name":"G","adminUserId":"u1","status":"WAITING","members":[{"userId":"u1","role":"ADMIN"}]}""");

        Assert.NotNull(published);
        Assert.NotNull(published!.Snapshot);
        Assert.False(published.InvalidateCache);
        Assert.Equal("join", published.Notification.Action);
        Assert.True(cache.TryGet(42, out var cached));
        Assert.Contains("\"id\":42", cached!.RawJson);
    }

    [Fact]
    public async Task PublishGameChanged_WhenPushDisabled_OmitsSnapshotButKeepsCacheSeed()
    {
        GameRealtimeEnvelope? published = null;
        var backplane = new Mock<IGameRealtimeBackplane>();
        backplane.Setup(x => x.PublishAsync(It.IsAny<long>(), It.IsAny<GameRealtimeEnvelope>(), It.IsAny<CancellationToken>()))
            .Callback<long, GameRealtimeEnvelope, CancellationToken>((_, envelope, _) => published = envelope)
            .Returns(Task.CompletedTask);

        var cache = new MemoryGameSnapshotCache(
            new MemoryCache(new MemoryCacheOptions()),
            Options.Create(new GameSnapshotOptions { PushEnabled = false }));
        var publisher = new GameEventPublisher(
            backplane.Object,
            new InMemoryGameRevisionStore(),
            cache,
            Options.Create(new GameSnapshotOptions { PushEnabled = false }),
            NullLogger<GameEventPublisher>.Instance);

        await publisher.PublishGameChangedAsync(
            1,
            "u1",
            GameChangeActions.Vote,
            """{"id":1,"name":"G","adminUserId":"u1"}""");

        Assert.NotNull(published);
        Assert.Null(published!.Snapshot);
        Assert.False(published.InvalidateCache);
        Assert.NotNull(published.SnapshotJson);
        Assert.True(cache.TryGet(1, out _));
    }

    [Fact]
    public async Task PublishGameChanged_WithoutSnapshot_InvalidatesCachedGame()
    {
        var backplane = new Mock<IGameRealtimeBackplane>();
        backplane.Setup(x => x.PublishAsync(
                It.IsAny<long>(),
                It.IsAny<GameRealtimeEnvelope>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var cache = new MemoryGameSnapshotCache(
            new MemoryCache(new MemoryCacheOptions()),
            Options.Create(new GameSnapshotOptions()));
        cache.Set(1, """{"id":1}""", revision: 1);

        // The leave mutation mints revision 2, so the cached revision-1 body is stale.
        var revisions = new InMemoryGameRevisionStore();
        await revisions.GetNextRevisionAsync(1);

        var publisher = new GameEventPublisher(
            backplane.Object,
            revisions,
            cache,
            Options.Create(new GameSnapshotOptions()),
            NullLogger<GameEventPublisher>.Instance);

        await publisher.PublishGameChangedAsync(1, "u1", GameChangeActions.Leave);

        Assert.False(cache.TryGet(1, out _));
    }
}

public class PostgresBackplanePayloadTests
{
    [Fact]
    public void Envelope_DropsSnapshotWhenSerializedPayloadIsTooLarge()
    {
        var backplane = new PostgresGameRealtimeBackplane(
            Options.Create(new RealtimeOptions()),
            Options.Create(new GameSnapshotOptions { MaxPayloadBytes = 512 }),
            NullLogger<PostgresGameRealtimeBackplane>.Instance);
        var hugeName = new string('x', 500);
        var envelope = new GameRealtimeEnvelope
        {
            Notification = new GameRealtimeMessage
            {
                Type = "gameChanged",
                GameId = 1,
                Revision = 1,
                Action = "join",
            },
            Snapshot = new Game
            {
                Id = 1,
                Name = hugeName,
                AdminUserId = "u1",
                Status = "WAITING",
            },
            SnapshotJson = $$"""{"id":1,"name":"{{hugeName}}","adminUserId":"u1","status":"WAITING"}""",
            InvalidateCache = false,
        };

        var payload = backplane.BuildPayload(envelope);
        var parsed = GameRealtimeEnvelope.FromJson(payload)!;

        Assert.Null(parsed.Snapshot);
        Assert.Null(parsed.SnapshotJson);
        Assert.True(parsed.InvalidateCache);
    }
}

public class GameSnapshotCacheSyncTests
{
    [Fact]
    public void Apply_DoesNotInvalidateWhenSnapshotMissingWithoutFlag()
    {
        var cache = new MemoryGameSnapshotCache(
            new MemoryCache(new MemoryCacheOptions()),
            Options.Create(new GameSnapshotOptions { CacheTtlSeconds = 60 }));
        cache.Set(1, """{"id":1}""", revision: 1);

        GameSnapshotCacheSync.Apply(cache, new GameRealtimeEnvelope
        {
            Notification = new GameRealtimeMessage
            {
                Type = "gameChanged",
                GameId = 1,
                Revision = 2,
                Action = "vote",
            },
            Snapshot = null,
            SnapshotJson = null,
            InvalidateCache = false,
        });

        Assert.True(cache.TryGet(1, out _));
    }

    [Fact]
    public void Apply_InvalidatesStaleCacheWhenFlagSet()
    {
        var cache = new MemoryGameSnapshotCache(
            new MemoryCache(new MemoryCacheOptions()),
            Options.Create(new GameSnapshotOptions { CacheTtlSeconds = 60 }));
        cache.Set(1, """{"id":1,"stale":true}""", revision: 1);

        GameSnapshotCacheSync.Apply(cache, new GameRealtimeEnvelope
        {
            Notification = new GameRealtimeMessage
            {
                Type = "gameChanged",
                GameId = 1,
                Revision = 2,
                Action = "vote",
            },
            InvalidateCache = true,
        });

        Assert.False(cache.TryGet(1, out _));
    }

    [Fact]
    public void Apply_KeepsSeededCacheWhenInvalidateEchoesSameRevision()
    {
        var cache = new MemoryGameSnapshotCache(
            new MemoryCache(new MemoryCacheOptions()),
            Options.Create(new GameSnapshotOptions { CacheTtlSeconds = 60 }));
        cache.Set(1, """{"id":1,"fresh":true}""", revision: 5);

        GameSnapshotCacheSync.Apply(cache, new GameRealtimeEnvelope
        {
            Notification = new GameRealtimeMessage
            {
                Type = "gameChanged",
                GameId = 1,
                Revision = 5,
                Action = "vote",
            },
            InvalidateCache = true,
        });

        Assert.True(cache.TryGet(1, out var snapshot));
        Assert.Contains("fresh", snapshot!.RawJson);
        Assert.Equal(5, snapshot.Revision);
    }
}
