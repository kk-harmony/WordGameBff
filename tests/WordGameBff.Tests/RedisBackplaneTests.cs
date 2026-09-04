using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using WordGameBff.Application.Configuration;
using WordGameBff.Application.Games;
using WordGameBff.Application.Realtime;
using WordGameBff.Domain.Models;
using WordGameBff.Infrastructure.Games;
using WordGameBff.Infrastructure.Realtime;
using WordGameBff.Infrastructure.Realtime.Redis;

namespace WordGameBff.Tests;

public class GameRealtimeEnvelopePayloadBuilderTests
{
    [Fact]
    public void BuildWirePayload_OmitsSnapshotObject()
    {
        var builder = CreateBuilder(maxPayloadBytes: 6000);
        var envelope = new GameRealtimeEnvelope
        {
            Notification = new GameRealtimeMessage
            {
                Type = "gameChanged",
                GameId = 1,
                Revision = 2,
                Action = "join",
            },
            Snapshot = new Game { Id = 1, Name = "Room", AdminUserId = "u1", Status = "WAITING" },
            SnapshotJson = """{"id":1,"name":"Room","adminUserId":"u1","status":"WAITING"}""",
        };

        var payload = builder.BuildWirePayload(envelope);
        var parsed = GameRealtimeEnvelope.FromJson(payload)!;

        Assert.Null(parsed.Snapshot);
        Assert.NotNull(parsed.SnapshotJson);
        Assert.DoesNotContain("\"snapshot\"", payload, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildWirePayload_DropsSnapshotJsonWhenSerializedPayloadIsTooLarge()
    {
        var builder = CreateBuilder(maxPayloadBytes: 512);
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

        var payload = builder.BuildWirePayload(envelope);
        var parsed = GameRealtimeEnvelope.FromJson(payload)!;

        Assert.Null(parsed.Snapshot);
        Assert.Null(parsed.SnapshotJson);
        Assert.True(parsed.InvalidateCache);
    }

    private static GameRealtimeEnvelopePayloadBuilder CreateBuilder(int maxPayloadBytes) =>
        new(
            Options.Create(new GameSnapshotOptions { MaxPayloadBytes = maxPayloadBytes }),
            NullLogger<GameRealtimeEnvelopePayloadBuilder>.Instance);
}

public class BackplaneEnvelopeDispatcherTests
{
    [Fact]
    public async Task Dispatch_SameRevisionTwice_FansOutOnce()
    {
        var cache = new MemoryGameSnapshotCache(
            new Microsoft.Extensions.Caching.Memory.MemoryCache(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions()),
            Options.Create(new GameSnapshotOptions { CacheTtlSeconds = 60 }));
        var fanout = new Mock<IGameSnapshotFanout>();
        var dispatcher = new BackplaneEnvelopeDispatcher(
            cache,
            fanout.Object,
            NullLogger<BackplaneEnvelopeDispatcher>.Instance);

        var envelope = new GameRealtimeEnvelope
        {
            Notification = new GameRealtimeMessage
            {
                Type = "gameChanged",
                GameId = 5,
                Revision = 3,
                Action = "vote",
            },
        };
        var payload = envelope.ToJson();

        await dispatcher.DispatchAsync(payload);
        await dispatcher.DispatchAsync(payload);

        fanout.Verify(x => x.DispatchAsync(It.IsAny<GameRealtimeEnvelope>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}

public class RedisGameRealtimeBackplaneTests
{
    [Fact]
    public async Task PublishAsync_PublishesWirePayloadAndDispatchesLocally()
    {
        var messaging = new Mock<IRedisBackplaneMessaging>();
        string? publishedPayload = null;
        messaging.Setup(x => x.PublishAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, CancellationToken>((_, payload, _) => publishedPayload = payload)
            .Returns(Task.CompletedTask);

        var cache = new MemoryGameSnapshotCache(
            new Microsoft.Extensions.Caching.Memory.MemoryCache(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions()),
            Options.Create(new GameSnapshotOptions { CacheTtlSeconds = 60 }));
        var fanout = new Mock<IGameSnapshotFanout>();
        var dispatcher = new BackplaneEnvelopeDispatcher(
            cache,
            fanout.Object,
            NullLogger<BackplaneEnvelopeDispatcher>.Instance);
        var payloadBuilder = new GameRealtimeEnvelopePayloadBuilder(
            Options.Create(new GameSnapshotOptions()),
            NullLogger<GameRealtimeEnvelopePayloadBuilder>.Instance);

        var backplane = new RedisGameRealtimeBackplane(
            messaging.Object,
            payloadBuilder,
            dispatcher,
            Options.Create(new RealtimeOptions()),
            NullLogger<RedisGameRealtimeBackplane>.Instance);

        var envelope = new GameRealtimeEnvelope
        {
            Notification = new GameRealtimeMessage
            {
                Type = "gameChanged",
                GameId = 9,
                Revision = 1,
                Action = "join",
            },
            SnapshotJson = """{"id":9,"name":"G","adminUserId":"u1"}""",
        };

        await backplane.PublishAsync(9, envelope);

        Assert.NotNull(publishedPayload);
        Assert.DoesNotContain("\"snapshot\"", publishedPayload!, StringComparison.OrdinalIgnoreCase);
        messaging.Verify(
            x => x.PublishAsync(RedisGameRealtimeBackplane.DefaultChannelName, It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
