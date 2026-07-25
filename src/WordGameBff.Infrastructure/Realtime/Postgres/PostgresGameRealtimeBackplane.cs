using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using WordGameBff.Application.Configuration;
using WordGameBff.Application.Realtime;

namespace WordGameBff.Infrastructure.Realtime.Postgres;

public sealed class PostgresGameRealtimeBackplane : IGameRealtimeBackplane
{
    internal const string ChannelName = "wordgamebff_backplane";

    private readonly RealtimeBackplaneOptions _options;
    private readonly GameSnapshotOptions _snapshotOptions;
    private readonly ILogger<PostgresGameRealtimeBackplane> _logger;

    public PostgresGameRealtimeBackplane(
        IOptions<RealtimeOptions> options,
        IOptions<GameSnapshotOptions> snapshotOptions,
        ILogger<PostgresGameRealtimeBackplane> logger)
    {
        _options = options.Value.Backplane;
        _snapshotOptions = snapshotOptions.Value;
        _logger = logger;
    }

    public async Task PublishAsync(long gameId, GameRealtimeEnvelope envelope, CancellationToken cancellationToken = default)
    {
        var payload = BuildPayload(envelope);

        await using var connection = new NpgsqlConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        // pg_notify accepts parameters; raw NOTIFY does not ($1 causes a syntax error).
        await using var command = new NpgsqlCommand("SELECT pg_notify(@channel, @payload)", connection);
        command.Parameters.AddWithValue("channel", ChannelName);
        command.Parameters.AddWithValue("payload", payload);
        await command.ExecuteNonQueryAsync(cancellationToken);

        _logger.LogDebug("Published realtime message for game {GameId} to channel {Channel}", gameId, ChannelName);
    }

    internal string BuildPayload(GameRealtimeEnvelope envelope)
    {
        var full = envelope.ToJson();
        var maxBytes = Math.Max(512, _snapshotOptions.MaxPayloadBytes);
        var hasPushBody = envelope.Snapshot is not null || !string.IsNullOrWhiteSpace(envelope.SnapshotJson);
        if (!hasPushBody || Encoding.UTF8.GetByteCount(full) <= maxBytes)
        {
            return full;
        }

        _logger.LogWarning(
            "Dropping snapshot from backplane payload for game {GameId}; serialized size exceeded {MaxBytes} bytes",
            envelope.Notification.GameId,
            maxBytes);

        // Stripping the snapshot would otherwise leave other instances serving a pre-mutation
        // body. Eviction is revision-scoped, so the publisher's own seed at this revision stays.
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
            SnapshotJson = null,
            InvalidateCache = true,
        }.ToJson();
    }
}
