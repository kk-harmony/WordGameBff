using System.Diagnostics;
using Microsoft.Extensions.Logging;
using WordGameBff.Application.Games;
using WordGameBff.Domain.Models;

namespace WordGameBff.Application.Realtime;

public interface IGameSnapshotFanout
{
    Task DispatchAsync(GameRealtimeEnvelope envelope, CancellationToken cancellationToken = default);
}

/// <summary>
/// Enrich presence once, then sanitize per viewer so vote/impostor privacy is preserved
/// without N-squared registry lookups.
/// </summary>
public sealed class GameSnapshotFanout : IGameSnapshotFanout
{
    private readonly IGameRealtimeTransport _transport;
    private readonly IGameSanitizer _sanitizer;
    private readonly IGamePresenceEnricher _presenceEnricher;
    private readonly IGameSelfVoteStore _selfVoteStore;
    private readonly ILogger<GameSnapshotFanout> _logger;

    public GameSnapshotFanout(
        IGameRealtimeTransport transport,
        IGameSanitizer sanitizer,
        IGamePresenceEnricher presenceEnricher,
        IGameSelfVoteStore selfVoteStore,
        ILogger<GameSnapshotFanout> logger)
    {
        _transport = transport;
        _sanitizer = sanitizer;
        _presenceEnricher = presenceEnricher;
        _selfVoteStore = selfVoteStore;
        _logger = logger;
    }

    public async Task DispatchAsync(GameRealtimeEnvelope envelope, CancellationToken cancellationToken = default)
    {
        var started = Stopwatch.GetTimestamp();
        var notification = envelope.Notification;
        var snapshot = envelope.Snapshot;
        if (snapshot is null)
        {
            await _transport.PublishToGameAsync(notification.GameId, notification, cancellationToken);
            LogCompleted(notification, "notify", viewerCount: 0, started);
            return;
        }

        try
        {
            await _selfVoteStore.SyncFromUpstreamAsync(snapshot, cancellationToken);
            var enriched = await _presenceEnricher.EnrichAsync(snapshot, cancellationToken);
            var viewerCount = 0;

            foreach (var viewerUserId in GameMembership.ViewerUserIds(enriched))
            {
                var sanitized = _sanitizer.Sanitize(enriched, viewerUserId);
                var withSelfVote = await _selfVoteStore.ApplyViewerSelfVoteAsync(
                    sanitized,
                    viewerUserId,
                    cancellationToken);

                var viewerMessage = new GameRealtimeMessage
                {
                    Type = notification.Type,
                    GameId = notification.GameId,
                    Revision = notification.Revision,
                    TriggeredBy = notification.TriggeredBy,
                    Action = notification.Action,
                    Game = withSelfVote,
                };

                await _transport.PublishToUserInGameAsync(
                    notification.GameId,
                    viewerUserId,
                    viewerMessage,
                    cancellationToken);
                viewerCount++;
            }

            LogCompleted(notification, "push", viewerCount, started);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Snapshot fanout failed for game {GameId}; falling back to lightweight notification",
                notification.GameId);
            await _transport.PublishToGameAsync(
                notification.GameId,
                new GameRealtimeMessage
                {
                    Type = notification.Type,
                    GameId = notification.GameId,
                    Revision = notification.Revision,
                    TriggeredBy = notification.TriggeredBy,
                    Action = notification.Action,
                },
                cancellationToken);
            LogCompleted(notification, "fallback-notify", viewerCount: 0, started);
        }
    }

    private void LogCompleted(
        GameRealtimeMessage notification,
        string mode,
        int viewerCount,
        long started)
    {
        _logger.LogInformation(
            "Realtime fanout for game {GameId} revision {Revision} action {Action} mode {Mode} viewers {ViewerCount} in {ElapsedMs}ms",
            notification.GameId,
            notification.Revision,
            notification.Action,
            mode,
            viewerCount,
            (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds);
    }
}
