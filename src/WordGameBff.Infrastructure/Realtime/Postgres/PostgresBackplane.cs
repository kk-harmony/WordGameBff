using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using WordGameBff.Application.Configuration;
using WordGameBff.Application.Realtime;

namespace WordGameBff.Infrastructure.Realtime.Postgres;

public sealed class PostgresGameRealtimeBackplane : IGameRealtimeBackplane
{
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

        var payload = message.ToJson();
        const string channel = "wordgamebff_backplane";

        // pg_notify accepts parameters; raw NOTIFY does not ($1 causes a syntax error).
        await using var command = new NpgsqlCommand("SELECT pg_notify(@channel, @payload)", connection);
        command.Parameters.AddWithValue("channel", channel);
        command.Parameters.AddWithValue("payload", payload);
        await command.ExecuteNonQueryAsync(cancellationToken);

        _logger.LogDebug("Published realtime message for game {GameId} to channel {Channel}", gameId, channel);
    }
}

public sealed class PostgresBackplaneListener : BackgroundService
{
    private readonly RealtimeBackplaneOptions _options;
    private readonly IGameRealtimeTransport _transport;
    private readonly ILogger<PostgresBackplaneListener> _logger;

    public PostgresBackplaneListener(
        IOptions<RealtimeOptions> options,
        IGameRealtimeTransport transport,
        ILogger<PostgresBackplaneListener> logger)
    {
        _options = options.Value.Backplane;
        _transport = transport;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ListenLoopAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Postgres backplane listener failed; retrying in 5 seconds");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }

    private async Task ListenLoopAsync(CancellationToken stoppingToken)
    {
        await using var connection = new NpgsqlConnection(_options.ConnectionString);
        await connection.OpenAsync(stoppingToken);

        connection.Notification += async (_, args) =>
        {
            try
            {
                var message = GameRealtimeMessage.FromJson(args.Payload);
                if (message is null)
                {
                    return;
                }

                await _transport.PublishToGameAsync(message.GameId, message, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to handle backplane notification");
            }
        };

        await using (var listenCommand = new NpgsqlCommand("LISTEN wordgamebff_backplane", connection))
        {
            await listenCommand.ExecuteNonQueryAsync(stoppingToken);
        }

        _logger.LogInformation("Postgres backplane listener started");

        while (!stoppingToken.IsCancellationRequested)
        {
            await connection.WaitAsync(stoppingToken);
        }
    }
}

public sealed class InMemoryGameRealtimeBackplane : IGameRealtimeBackplane
{
    private readonly IGameRealtimeTransport _transport;

    public InMemoryGameRealtimeBackplane(IGameRealtimeTransport transport)
    {
        _transport = transport;
    }

    public Task PublishAsync(long gameId, GameRealtimeMessage message, CancellationToken cancellationToken = default) =>
        _transport.PublishToGameAsync(gameId, message, cancellationToken);
}
