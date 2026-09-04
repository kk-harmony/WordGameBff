using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WordGameBff.Application.Configuration;
using WordGameBff.Application.Realtime;

namespace WordGameBff.Infrastructure.Realtime;

/// <summary>
/// Builds lightweight backplane wire payloads: notification metadata plus optional
/// <see cref="GameRealtimeEnvelope.SnapshotJson"/> for peer cache seeding — never a
/// deserialized <see cref="GameRealtimeEnvelope.Snapshot"/> object on the wire.
/// </summary>
public sealed class GameRealtimeEnvelopePayloadBuilder
{
    private readonly GameSnapshotOptions _snapshotOptions;
    private readonly ILogger<GameRealtimeEnvelopePayloadBuilder> _logger;

    public GameRealtimeEnvelopePayloadBuilder(
        IOptions<GameSnapshotOptions> snapshotOptions,
        ILogger<GameRealtimeEnvelopePayloadBuilder> logger)
    {
        _snapshotOptions = snapshotOptions.Value;
        _logger = logger;
    }

    public string BuildWirePayload(GameRealtimeEnvelope envelope)
    {
        var wire = ToWireEnvelope(envelope);
        var payload = wire.ToJson();
        var maxBytes = Math.Max(512, _snapshotOptions.MaxPayloadBytes);
        var hasBody = !string.IsNullOrWhiteSpace(wire.SnapshotJson);
        if (!hasBody || Encoding.UTF8.GetByteCount(payload) <= maxBytes)
        {
            return payload;
        }

        _logger.LogWarning(
            "Dropping snapshotJson from backplane payload for game {GameId}; serialized size exceeded {MaxBytes} bytes",
            envelope.Notification.GameId,
            maxBytes);

        return NotificationOnly(envelope, invalidateCache: true).ToJson();
    }

    internal static GameRealtimeEnvelope ToWireEnvelope(GameRealtimeEnvelope envelope)
    {
        var json = envelope.SnapshotJson;
        if (string.IsNullOrWhiteSpace(json) && envelope.Snapshot is not null)
        {
            json = JsonSerializer.Serialize(envelope.Snapshot, RealtimeJson.Options);
        }

        var invalidate = envelope.InvalidateCache;
        if (string.IsNullOrWhiteSpace(json) && !invalidate)
        {
            invalidate = envelope.Snapshot is null;
        }

        return NotificationOnly(envelope, json, invalidate);
    }

    private static GameRealtimeEnvelope NotificationOnly(
        GameRealtimeEnvelope envelope,
        string? snapshotJson = null,
        bool invalidateCache = false)
    {
        return new GameRealtimeEnvelope
        {
            Notification = new GameRealtimeMessage
            {
                Type = envelope.Notification.Type,
                GameId = envelope.Notification.GameId,
                Revision = envelope.Notification.Revision,
                TriggeredBy = envelope.Notification.TriggeredBy,
                Action = envelope.Notification.Action,
            },
            Snapshot = null,
            SnapshotJson = snapshotJson,
            InvalidateCache = invalidateCache,
        };
    }
}
