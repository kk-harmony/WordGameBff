using Moq;
using WordGameBff.Application.Games;
using WordGameBff.Application.Realtime;
using WordGameBff.Domain.Models;

namespace WordGameBff.Tests;

public class SessionServiceTests
{
    [Fact]
    public async Task StartGameAsync_SeedsCacheAndPublishesStart()
    {
        var api = new Mock<IGameApiClient>();
        api.Setup(x => x.StartSessionGameAsync("admin", "K7M2Q", It.IsAny<StartGameRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GameApiResponse
            {
                StatusCode = 201,
                Body = """{"id":42,"name":"Lobby","adminUserId":"admin","status":"IN_PROGRESS","members":[]}"""
            });

        var cache = new Mock<IGameSnapshotCache>();
        var revisions = new Mock<IGameRevisionStore>();
        revisions.Setup(x => x.GetCurrentRevisionAsync(42, It.IsAny<CancellationToken>())).ReturnsAsync(1L);

        var responseBuilder = new Mock<IGameResponseBuilder>();
        responseBuilder
            .Setup(x => x.BuildAsync(It.IsAny<Game>(), "admin", It.IsAny<CancellationToken>()))
            .ReturnsAsync((Game game, string _, CancellationToken _) => game);

        var publisher = new Mock<IGameEventPublisher>();

        var service = new SessionService(
            api.Object,
            cache.Object,
            revisions.Object,
            responseBuilder.Object,
            publisher.Object,
            Mock.Of<IUpstreamErrorNormalizer>());

        var outcome = await service.StartGameAsync("admin", "K7M2Q", new StartGameRequest { SecretWordId = 1 });

        var success = Assert.IsType<AppSuccess>(outcome);
        Assert.Equal(AppSuccessKind.Created, success.Kind);
        Assert.Equal("42", success.ResourceId);
        cache.Verify(x => x.Set(42, It.IsAny<string>(), 1), Times.Once);
        publisher.Verify(
            x => x.PublishGameChangedAsync(42, "admin", GameChangeActions.Start, It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task CreateSessionAsync_ReturnsCreatedWithSessionLocation()
    {
        var api = new Mock<IGameApiClient>();
        api.Setup(x => x.CreateSessionAsync("admin", It.IsAny<CreateSessionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GameApiResponse
            {
                StatusCode = 201,
                Body = """{"id":"K7M2Q","name":"Lobby","adminUserId":"admin","status":"OPEN","gamesStartedCount":0,"maxGames":20,"members":[]}"""
            });

        var service = new SessionService(
            api.Object,
            Mock.Of<IGameSnapshotCache>(),
            Mock.Of<IGameRevisionStore>(),
            Mock.Of<IGameResponseBuilder>(),
            Mock.Of<IGameEventPublisher>(),
            Mock.Of<IUpstreamErrorNormalizer>());

        var outcome = await service.CreateSessionAsync("admin", new CreateSessionRequest { Name = "Lobby" });
        var success = Assert.IsType<AppSuccess>(outcome);
        Assert.Equal("sessions/K7M2Q", success.ResourceId);
    }

    [Fact]
    public async Task GetSessionAsync_PassthroughSuccess()
    {
        var api = new Mock<IGameApiClient>();
        api.Setup(x => x.GetSessionAsync("admin", "K7M2Q", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GameApiResponse
            {
                StatusCode = 200,
                Body = """{"id":"K7M2Q","name":"Lobby","adminUserId":"admin"}"""
            });
        var normalizer = new Mock<IUpstreamErrorNormalizer>();

        var service = new SessionService(
            api.Object,
            Mock.Of<IGameSnapshotCache>(),
            Mock.Of<IGameRevisionStore>(),
            Mock.Of<IGameResponseBuilder>(),
            Mock.Of<IGameEventPublisher>(),
            normalizer.Object);

        var outcome = await service.GetSessionAsync("admin", "K7M2Q");
        Assert.IsType<AppRawJson>(outcome);
    }

    [Fact]
    public void GameSession_DeserializesMemberScore()
    {
        const string json = """
            {"id":"K7M2Q","name":"Lobby","adminUserId":"admin","status":"OPEN","gamesStartedCount":1,"maxGames":20,
             "members":[{"userId":"admin","displayName":"Host","role":"ADMIN","score":3},{"userId":"p2","role":"MEMBER","score":2}]}
            """;

        var session = System.Text.Json.JsonSerializer.Deserialize<GameSession>(json, RealtimeJson.Options);
        Assert.NotNull(session);
        Assert.NotNull(session!.Members);
        Assert.Equal(2, session.Members!.Count);
        Assert.Equal(3, session.Members[0].Score);
        Assert.Equal(2, session.Members[1].Score);
    }

    [Fact]
    public async Task JoinSessionAsync_PassthroughSuccess()
    {
        var api = new Mock<IGameApiClient>();
        api.Setup(x => x.JoinSessionAsync("player", "K7M2Q", It.IsAny<JoinSessionRequest?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GameApiResponse
            {
                StatusCode = 201,
                Body = """{"id":"K7M2Q","name":"Lobby","adminUserId":"admin","members":[]}"""
            });

        var service = new SessionService(
            api.Object,
            Mock.Of<IGameSnapshotCache>(),
            Mock.Of<IGameRevisionStore>(),
            Mock.Of<IGameResponseBuilder>(),
            Mock.Of<IGameEventPublisher>(),
            Mock.Of<IUpstreamErrorNormalizer>());

        var outcome = await service.JoinSessionAsync("player", "K7M2Q", new JoinSessionRequest { DisplayName = "Alex" });
        Assert.IsType<AppRawJson>(outcome);
    }

    [Fact]
    public async Task RemoveMemberAsync_ReturnsNoContentOnUpstream204()
    {
        var api = new Mock<IGameApiClient>();
        api.Setup(x => x.RemoveSessionMemberAsync("player", "K7M2Q", "player", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GameApiResponse { StatusCode = 204, Body = "" });

        var service = new SessionService(
            api.Object,
            Mock.Of<IGameSnapshotCache>(),
            Mock.Of<IGameRevisionStore>(),
            Mock.Of<IGameResponseBuilder>(),
            Mock.Of<IGameEventPublisher>(),
            Mock.Of<IUpstreamErrorNormalizer>());

        var outcome = await service.RemoveMemberAsync("player", "K7M2Q", "player");
        Assert.IsType<AppNoContent>(outcome);
    }
}
