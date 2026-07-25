using Microsoft.Extensions.Options;
using Moq;
using WordGameBff.Application.Auth;
using WordGameBff.Application.Configuration;
using WordGameBff.Application.Realtime;

namespace WordGameBff.Tests;

public class GameHubJoinServiceTests
{
    private const string AccessToken = "token";
    private const string UserId = "u1";
    private const long GameId = 7;

    [Fact]
    public async Task TryJoin_WhenMembershipIsVerified_RegistersConnection()
    {
        var membership = new Mock<IGameMembershipVerifier>();
        membership.Setup(x => x.IsMemberAsync(UserId, GameId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var registry = new RecordingConnectionRegistry();
        var service = CreateService(membership.Object, registry);

        var result = await service.TryJoinAsync(AccessToken, GameId, "conn-1");

        var success = Assert.IsType<HubJoinSuccess>(result);
        Assert.Equal(UserId, success.UserId);
        Assert.True(await registry.IsUserConnectedToGameAsync(UserId, GameId));
    }

    [Fact]
    public async Task TryJoin_WhenMembershipCannotBeVerified_ReportsGameUnavailable()
    {
        var membership = new Mock<IGameMembershipVerifier>();
        membership.Setup(x => x.IsMemberAsync(UserId, GameId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var registry = new RecordingConnectionRegistry();
        var service = CreateService(membership.Object, registry);

        var result = await service.TryJoinAsync(AccessToken, GameId, "conn-1");

        var failure = Assert.IsType<HubJoinFailure>(result);
        Assert.Equal(HubJoinFailureReason.GameUnavailable, failure.Reason);
        Assert.False(await registry.IsUserConnectedToGameAsync(UserId, GameId));
    }

    private static GameHubJoinService CreateService(
        IGameMembershipVerifier membershipVerifier,
        IGameConnectionRegistry registry)
    {
        var sessionTokens = new Mock<ISessionTokenService>();
        sessionTokens.Setup(x => x.ValidateTokenAsync(AccessToken, It.IsAny<CancellationToken>()))
            .ReturnsAsync((true, UserId, "sid"));

        var options = Options.Create(new RealtimeOptions
        {
            MaxConnectionsPerUser = 3,
        });

        return new GameHubJoinService(
            sessionTokens.Object,
            membershipVerifier,
            registry,
            options);
    }

    private sealed class RecordingConnectionRegistry : IGameConnectionRegistry
    {
        private readonly Dictionary<string, (string UserId, long GameId)> _entries = new();

        public Task<bool> TryRegisterAsync(
            string connectionId,
            string userId,
            long gameId,
            CancellationToken cancellationToken = default)
        {
            _entries[connectionId] = (userId, gameId);
            return Task.FromResult(true);
        }

        public Task UnregisterAsync(string connectionId, CancellationToken cancellationToken = default)
        {
            _entries.Remove(connectionId);
            return Task.CompletedTask;
        }

        public Task RefreshAsync(string connectionId, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<int> GetConnectionCountForUserAsync(string userId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_entries.Values.Count(e => e.UserId == userId));

        public Task<bool> IsUserConnectedToGameAsync(string userId, long gameId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_entries.Values.Any(e => e.UserId == userId && e.GameId == gameId));
    }
}
