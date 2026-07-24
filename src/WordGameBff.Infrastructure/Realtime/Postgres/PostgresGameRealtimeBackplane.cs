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
    private readonly ILogger<PostgresGameRealtimeBackplane> _logger;

    public PostgresGameRealtimeBackplane(
        IOptions<RealtimeOptions> options,
        ILogger<PostgresGameRealtimeBackplane> logger)
    {
        _options = options.Value.Backplane;
        _logger = logger;
    }

    public async Task PublishAsync(long gameId, GameRealtimeMessage message, CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        // pg_notify accepts parameters; raw NOTIFY does not ($1 causes a syntax error).
        await using var command = new NpgsqlCommand("SELECT pg_notify(@channel, @payload)", connection);
        command.Parameters.AddWithValue("channel", ChannelName);
        command.Parameters.AddWithValue("payload", message.ToJson());
        await command.ExecuteNonQueryAsync(cancellationToken);

        _logger.LogDebug("Published realtime message for game {GameId} to channel {Channel}", gameId, ChannelName);
    }
}
