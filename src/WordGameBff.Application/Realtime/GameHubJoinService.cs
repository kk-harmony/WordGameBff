using Microsoft.Extensions.Options;
using WordGameBff.Application.Auth;
using WordGameBff.Application.Configuration;
using WordGameBff.Application.Games;
using WordGameBff.Application.Realtime;

namespace WordGameBff.Application.Realtime;

public enum HubJoinFailureReason
{
    InvalidToken,
    GameUnavailable,
    ConnectionLimitExceeded,
    RegistrationFailed,
}

public abstract record HubJoinResult;

public sealed record HubJoinSuccess(string UserId) : HubJoinResult;

public sealed record HubJoinFailure(HubJoinFailureReason Reason) : HubJoinResult;

public interface IGameHubJoinService
{
    Task<HubJoinResult> TryJoinAsync(
        string accessToken,
        long gameId,
        string connectionId,
        CancellationToken cancellationToken = default);

    Task LeaveAsync(string connectionId, CancellationToken cancellationToken = default);
}

public sealed class GameHubJoinService : IGameHubJoinService
{
    private readonly ISessionTokenService _sessionTokenService;
    private readonly IGameApiClient _gameApiClient;
    private readonly IGameConnectionRegistry _connectionRegistry;
    private readonly RealtimeOptions _options;

    public GameHubJoinService(
        ISessionTokenService sessionTokenService,
        IGameApiClient gameApiClient,
        IGameConnectionRegistry connectionRegistry,
        IOptions<RealtimeOptions> options)
    {
        _sessionTokenService = sessionTokenService;
        _gameApiClient = gameApiClient;
        _connectionRegistry = connectionRegistry;
        _options = options.Value;
    }

    public async Task<HubJoinResult> TryJoinAsync(
        string accessToken,
        long gameId,
        string connectionId,
        CancellationToken cancellationToken = default)
    {
        var validation = await _sessionTokenService.ValidateTokenAsync(accessToken, cancellationToken);
        if (!validation.IsValid || string.IsNullOrEmpty(validation.UserId))
        {
            return new HubJoinFailure(HubJoinFailureReason.InvalidToken);
        }

        var gameResponse = await _gameApiClient.GetGameAsync(validation.UserId, gameId, cancellationToken);
        if (!gameResponse.IsSuccess)
        {
            return new HubJoinFailure(HubJoinFailureReason.GameUnavailable);
        }

        var connectionCount = await _connectionRegistry.GetConnectionCountForUserAsync(
            validation.UserId,
            cancellationToken);
        if (connectionCount >= _options.MaxConnectionsPerUser)
        {
            return new HubJoinFailure(HubJoinFailureReason.ConnectionLimitExceeded);
        }

        if (!await _connectionRegistry.TryRegisterAsync(connectionId, validation.UserId, gameId, cancellationToken))
        {
            return new HubJoinFailure(HubJoinFailureReason.RegistrationFailed);
        }

        return new HubJoinSuccess(validation.UserId);
    }

    public Task LeaveAsync(string connectionId, CancellationToken cancellationToken = default) =>
        _connectionRegistry.UnregisterAsync(connectionId, cancellationToken);
}
