using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using WordGameBff.Application.Configuration;
using WordGameBff.Application.Games;
using WordGameBff.Application.Realtime;

namespace WordGameBff.Tests;

public class UpstreamGameMembershipVerifierTests
{
    private const string UserId = "u1";
    private const long GameId = 7;

    [Fact]
    public async Task IsMember_WhenUpstreamAnswers_ReturnsResponseSuccess()
    {
        var reader = new Mock<IGameSnapshotReader>();
        reader.Setup(x => x.GetGameAsync(UserId, GameId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GameApiResponse
            {
                StatusCode = 200,
                Body = """{"id":7,"name":"G","adminUserId":"u1","members":[{"userId":"u1"}]}""",
            });

        var verifier = CreateVerifier(reader.Object);

        Assert.True(await verifier.IsMemberAsync(UserId, GameId));
    }

    [Fact]
    public async Task IsMember_WhenUpstreamIsSlow_FailsFast()
    {
        var reader = new Mock<IGameSnapshotReader>();
        reader.Setup(x => x.GetGameAsync(UserId, GameId, It.IsAny<CancellationToken>()))
            .Returns(async (string _, long _, CancellationToken token) =>
            {
                await Task.Delay(TimeSpan.FromSeconds(30), token);
                return new GameApiResponse { StatusCode = 200, Body = "{}" };
            });

        var verifier = CreateVerifier(reader.Object, timeoutSeconds: 1);
        var startedAt = DateTimeOffset.UtcNow;

        var isMember = await verifier.IsMemberAsync(UserId, GameId);

        Assert.False(isMember);
        Assert.True(
            DateTimeOffset.UtcNow - startedAt < TimeSpan.FromSeconds(5),
            "Membership verification should honor its timeout budget.");
    }

    [Fact]
    public async Task IsMember_WhenUpstreamThrows_ReturnsFalse()
    {
        var reader = new Mock<IGameSnapshotReader>();
        reader.Setup(x => x.GetGameAsync(UserId, GameId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("upstream failure"));

        var verifier = CreateVerifier(reader.Object);

        Assert.False(await verifier.IsMemberAsync(UserId, GameId));
    }

    [Fact]
    public async Task IsMember_WhenCallerCancels_PropagatesCancellation()
    {
        var reader = new Mock<IGameSnapshotReader>();
        reader.Setup(x => x.GetGameAsync(UserId, GameId, It.IsAny<CancellationToken>()))
            .Returns(async (string _, long _, CancellationToken token) =>
            {
                await Task.Delay(TimeSpan.FromSeconds(30), token);
                return new GameApiResponse { StatusCode = 200, Body = "{}" };
            });

        var verifier = CreateVerifier(reader.Object);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => verifier.IsMemberAsync(UserId, GameId, cancellation.Token));
    }

    private static UpstreamGameMembershipVerifier CreateVerifier(
        IGameSnapshotReader snapshotReader,
        int timeoutSeconds = 3) =>
        new(
            snapshotReader,
            Options.Create(new RealtimeOptions
            {
                HubJoinUpstreamTimeoutSeconds = timeoutSeconds,
            }),
            NullLogger<UpstreamGameMembershipVerifier>.Instance);
}
