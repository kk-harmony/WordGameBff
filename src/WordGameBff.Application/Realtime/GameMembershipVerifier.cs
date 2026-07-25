using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WordGameBff.Application.Configuration;
using WordGameBff.Application.Games;

namespace WordGameBff.Application.Realtime;

public interface IGameMembershipVerifier
{
    Task<bool> IsMemberAsync(
        string userId,
        long gameId,
        CancellationToken cancellationToken = default);
}

public sealed class UpstreamGameMembershipVerifier : IGameMembershipVerifier
{
    private readonly IGameApiClient _gameApiClient;
    private readonly TimeSpan _timeout;
    private readonly ILogger<UpstreamGameMembershipVerifier> _logger;

    public UpstreamGameMembershipVerifier(
        IGameApiClient gameApiClient,
        IOptions<RealtimeOptions> options,
        ILogger<UpstreamGameMembershipVerifier> logger)
    {
        _gameApiClient = gameApiClient;
        _timeout = TimeSpan.FromSeconds(options.Value.HubJoinUpstreamTimeoutSeconds);
        _logger = logger;
    }

    public async Task<bool> IsMemberAsync(
        string userId,
        long gameId,
        CancellationToken cancellationToken = default)
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(_timeout);

        try
        {
            var response = await _gameApiClient.GetGameAsync(userId, gameId, timeoutSource.Token);
            return response.IsSuccess;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(
                "Hub join GetGame timed out after {TimeoutSeconds}s for game {GameId}",
                _timeout.TotalSeconds,
                gameId);
            return false;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Hub join GetGame failed for game {GameId}", gameId);
            return false;
        }
    }
}
