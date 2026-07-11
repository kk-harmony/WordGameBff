using Microsoft.Extensions.Logging;

namespace WordGameBff.Application.Realtime;

public sealed class GameEventPublisher : IGameEventPublisher
{
    private readonly IGameRealtimeBackplane _backplane;
    private readonly IGameRevisionStore _revisionStore;
    private readonly ILogger<GameEventPublisher> _logger;

    public GameEventPublisher(
        IGameRealtimeBackplane backplane,
        IGameRevisionStore revisionStore,
        ILogger<GameEventPublisher> logger)
    {
        _backplane = backplane;
        _revisionStore = revisionStore;
        _logger = logger;
    }

    public async Task PublishGameChangedAsync(
        long gameId,
        string triggeredByUserId,
        string action,
        CancellationToken cancellationToken = default)
    {
        var message = new GameRealtimeMessage
        {
            Type = "gameChanged",
            GameId = gameId,
            Revision = await _revisionStore.GetNextRevisionAsync(gameId, cancellationToken),
            TriggeredBy = triggeredByUserId,
            Action = action,
        };

        await _backplane.PublishAsync(gameId, message, cancellationToken);
        _logger.LogDebug(
            "Published gameChanged for game {GameId} revision {Revision} action {Action}",
            gameId,
            message.Revision,
            action);
    }
}
