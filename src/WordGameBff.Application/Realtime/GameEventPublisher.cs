using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WordGameBff.Application.Configuration;
using WordGameBff.Application.Games;
using WordGameBff.Domain.Models;

namespace WordGameBff.Application.Realtime;

public sealed class GameEventPublisher : IGameEventPublisher
{
    private readonly IGameRealtimeBackplane _backplane;
    private readonly IGameRevisionStore _revisionStore;
    private readonly IGameSnapshotCache _snapshotCache;
    private readonly GameSnapshotOptions _snapshotOptions;
    private readonly ILogger<GameEventPublisher> _logger;

    public GameEventPublisher(
        IGameRealtimeBackplane backplane,
        IGameRevisionStore revisionStore,
        IGameSnapshotCache snapshotCache,
        IOptions<GameSnapshotOptions> snapshotOptions,
        ILogger<GameEventPublisher> logger)
    {
        _backplane = backplane;
        _revisionStore = revisionStore;
        _snapshotCache = snapshotCache;
        _snapshotOptions = snapshotOptions.Value;
        _logger = logger;
    }

    public async Task PublishGameChangedAsync(
        long gameId,
        string triggeredByUserId,
        string action,
        string? snapshotJson = null,
        CancellationToken cancellationToken = default)
    {
        var revision = await _revisionStore.GetNextRevisionAsync(gameId, cancellationToken);
        var notification = new GameRealtimeMessage
        {
            Type = "gameChanged",
            GameId = gameId,
            Revision = revision,
            TriggeredBy = triggeredByUserId,
            Action = action,
        };

        Game? snapshot = null;
        string? rawJson = null;
        if (!string.IsNullOrWhiteSpace(snapshotJson))
        {
            rawJson = snapshotJson;
            snapshot = JsonSerializer.Deserialize<Game>(snapshotJson, RealtimeJson.Options);
        }

        var envelope = new GameRealtimeEnvelope
        {
            Notification = notification,
            Snapshot = _snapshotOptions.PushEnabled ? snapshot : null,
            SnapshotJson = rawJson,
            InvalidateCache = rawJson is null,
        };

        GameSnapshotCacheSync.Apply(_snapshotCache, envelope);
        await _backplane.PublishAsync(gameId, envelope, cancellationToken);
        _logger.LogDebug(
            "Published gameChanged for game {GameId} revision {Revision} action {Action} push={HasPush} invalidate={Invalidate}",
            gameId,
            revision,
            action,
            envelope.Snapshot is not null,
            envelope.InvalidateCache);
    }
}
