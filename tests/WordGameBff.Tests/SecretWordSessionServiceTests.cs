using Moq;
using WordGameBff.Application.Games;
using WordGameBff.Application.Realtime;
using WordGameBff.Domain.Models;

namespace WordGameBff.Tests;

public class SecretWordSessionServiceTests
{
    [Fact]
    public async Task GetRandomForSessionAsync_AllowsAdminBetweenGames()
    {
        var api = new Mock<IGameApiClient>();
        api.Setup(x => x.GetSessionAsync("admin", "K7M2Q", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GameApiResponse
            {
                StatusCode = 200,
                Body = """{"id":"K7M2Q","name":"Lobby","adminUserId":"admin","status":"OPEN","currentGameId":null,"currentGameStatus":null}"""
            });
        api.Setup(x => x.GetRandomSecretWordAsync("admin", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GameApiResponse
            {
                StatusCode = 200,
                Body = """{"id":7,"authentic":"apple","imposed":"orange"}"""
            });

        var service = CreateService(api.Object, new SecretWordResponseBuilder());
        var outcome = await service.GetRandomForSessionAsync("admin", "K7M2Q");

        Assert.IsType<AppSuccess>(outcome);
        api.Verify(x => x.GetRandomSecretWordAsync("admin", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetRandomForSessionAsync_DeniesWhileGameInProgress()
    {
        var api = new Mock<IGameApiClient>();
        api.Setup(x => x.GetSessionAsync("admin", "K7M2Q", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GameApiResponse
            {
                StatusCode = 200,
                Body = """{"id":"K7M2Q","name":"Lobby","adminUserId":"admin","status":"OPEN","currentGameId":42,"currentGameStatus":"IN_PROGRESS"}"""
            });

        var service = CreateService(api.Object, Mock.Of<ISecretWordResponseBuilder>());
        var outcome = await service.GetRandomForSessionAsync("admin", "K7M2Q");

        var failure = Assert.IsType<AppFailure>(outcome);
        Assert.Equal(AppFailureKind.Forbidden, failure.Kind);
        api.Verify(x => x.GetRandomSecretWordAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetRandomForSessionAsync_DeniesNonAdmin()
    {
        var api = new Mock<IGameApiClient>();
        api.Setup(x => x.GetSessionAsync("player", "K7M2Q", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GameApiResponse
            {
                StatusCode = 200,
                Body = """{"id":"K7M2Q","name":"Lobby","adminUserId":"admin","status":"OPEN","currentGameId":null}"""
            });

        var service = CreateService(api.Object, Mock.Of<ISecretWordResponseBuilder>());
        var outcome = await service.GetRandomForSessionAsync("player", "K7M2Q");

        var failure = Assert.IsType<AppFailure>(outcome);
        Assert.Equal(AppFailureKind.Forbidden, failure.Kind);
        api.Verify(x => x.GetRandomSecretWordAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private static SecretWordService CreateService(IGameApiClient api, ISecretWordResponseBuilder builder) =>
        new(
            api,
            Mock.Of<IGameSnapshotReader>(),
            Mock.Of<ISecretWordAccessPolicy>(),
            builder,
            Mock.Of<IUpstreamErrorNormalizer>());
}
