using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using WordGameBff.Application.Games;
using WordGameBff.Application.Realtime;

namespace WordGameBff.Infrastructure.Realtime;

/// <summary>
/// Parses backplane payloads, deduplicates by (gameId, revision), syncs cache, and fans out.
/// </summary>
public sealed class BackplaneEnvelopeDispatcher
{
    private const int MaxDedupEntries = 256;

    private readonly IGameSnapshotCache _snapshotCache;
    private readonly IGameSnapshotFanout _fanout;
    private readonly ILogger<BackplaneEnvelopeDispatcher> _logger;
    private readonly ConcurrentDictionary<(long GameId, long Revision), byte> _processed = new();
    private readonly ConcurrentQueue<(long GameId, long Revision)> _processedOrder = new();

    public BackplaneEnvelopeDispatcher(
        IGameSnapshotCache snapshotCache,
        IGameSnapshotFanout fanout,
        ILogger<BackplaneEnvelopeDispatcher> logger)
    {
        _snapshotCache = snapshotCache;
        _fanout = fanout;
        _logger = logger;
    }

    public async Task DispatchAsync(string payload, CancellationToken cancellationToken = default)
    {
        var envelope = ParseEnvelope(payload);
        if (envelope is null)
        {
            _logger.LogWarning(
                "Ignored unparseable backplane payload ({PayloadBytes} bytes)",
                Encoding.UTF8.GetByteCount(payload));
            return;
        }

        await DispatchAsync(envelope, payloadBytes: Encoding.UTF8.GetByteCount(payload), cancellationToken);
    }

    public async Task DispatchAsync(GameRealtimeEnvelope envelope, CancellationToken cancellationToken = default)
    {
        await DispatchAsync(envelope, payloadBytes: null, cancellationToken);
    }

    internal bool ShouldProcess(long gameId, long revision) => TryMarkProcessed(gameId, revision);

    private async Task DispatchAsync(
        GameRealtimeEnvelope envelope,
        int? payloadBytes,
        CancellationToken cancellationToken)
    {
        var notification = envelope.Notification;
        if (!TryMarkProcessed(notification.GameId, notification.Revision))
        {
            _logger.LogDebug(
                "Skipped duplicate backplane envelope for game {GameId} revision {Revision}",
                notification.GameId,
                notification.Revision);
            return;
        }

        var started = Stopwatch.GetTimestamp();
        try
        {
            GameSnapshotCacheSync.Apply(_snapshotCache, envelope);
            await _fanout.DispatchAsync(envelope, cancellationToken);

            _logger.LogInformation(
                "Backplane dispatched game {GameId} revision {Revision} action {Action} push={HasPush} bytes {PayloadBytes} in {ElapsedMs}ms",
                notification.GameId,
                notification.Revision,
                notification.Action,
                envelope.Snapshot is not null,
                payloadBytes,
                (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to dispatch backplane envelope for game {GameId} revision {Revision}",
                notification.GameId,
                notification.Revision);
        }
    }

    private bool TryMarkProcessed(long gameId, long revision)
    {
        if (!_processed.TryAdd((gameId, revision), 0))
        {
            return false;
        }

        _processedOrder.Enqueue((gameId, revision));
        while (_processedOrder.Count > MaxDedupEntries && _processedOrder.TryDequeue(out var oldest))
        {
            _processed.TryRemove(oldest, out _);
        }

        return true;
    }

    internal static GameRealtimeEnvelope? ParseEnvelope(string payload)
    {
        using var document = JsonDocument.Parse(payload);
        if (document.RootElement.TryGetProperty("notification", out _))
        {
            return GameRealtimeEnvelope.FromJson(payload);
        }

        var legacy = GameRealtimeMessage.FromJson(payload);
        if (legacy is null)
        {
            return null;
        }

        return new GameRealtimeEnvelope
        {
            Notification = legacy,
            Snapshot = null,
        };
    }
}
