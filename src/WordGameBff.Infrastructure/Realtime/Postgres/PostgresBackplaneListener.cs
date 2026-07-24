using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using WordGameBff.Application.Configuration;
using WordGameBff.Application.Realtime;

namespace WordGameBff.Infrastructure.Realtime.Postgres;

public sealed class PostgresBackplaneListener : BackgroundService
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(5);

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
                await ListenAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Postgres backplane listener failed; retrying in {RetrySeconds} seconds", RetryDelay.TotalSeconds);
                await Task.Delay(RetryDelay, stoppingToken);
            }
        }
    }

    private async Task ListenAsync(CancellationToken stoppingToken)
    {
        var payloads = Channel.CreateUnbounded<string>(new UnboundedChannelOptions
        {
            SingleReader = true,
        });

        await using var connection = new NpgsqlConnection(_options.ConnectionString);
        await connection.OpenAsync(stoppingToken);
        connection.Notification += (_, args) => Enqueue(payloads.Writer, args.Payload);

        await using (var listen = new NpgsqlCommand($"LISTEN {PostgresGameRealtimeBackplane.ChannelName}", connection))
        {
            await listen.ExecuteNonQueryAsync(stoppingToken);
        }

        _logger.LogInformation("Postgres backplane listener started");

        var dispatch = DispatchAsync(payloads.Reader, stoppingToken);
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await connection.WaitAsync(stoppingToken);
            }
        }
        finally
        {
            payloads.Writer.TryComplete();
            await AwaitCompletedAsync(dispatch, stoppingToken);
        }
    }

    private void Enqueue(ChannelWriter<string> writer, string? payload)
    {
        if (string.IsNullOrEmpty(payload))
        {
            return;
        }

        if (!writer.TryWrite(payload))
        {
            _logger.LogWarning("Dropped backplane notification; payload channel unavailable");
        }
    }

    private async Task DispatchAsync(ChannelReader<string> reader, CancellationToken stoppingToken)
    {
        await foreach (var payload in reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                var message = GameRealtimeMessage.FromJson(payload);
                if (message is null)
                {
                    continue;
                }

                await _transport.PublishToGameAsync(message.GameId, message, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to handle backplane notification");
            }
        }
    }

    private static async Task AwaitCompletedAsync(Task dispatch, CancellationToken stoppingToken)
    {
        try
        {
            await dispatch;
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }
}
