using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using WordGameBff.Application.Configuration;
using WordGameBff.Application.Games;
using WordGameBff.Application.Realtime;

namespace WordGameBff.Infrastructure.Realtime.Postgres;

public sealed class PostgresBackplaneListener : BackgroundService
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(5);

    private readonly RealtimeBackplaneOptions _options;
    private readonly IGameSnapshotCache _snapshotCache;
    private readonly IGameSnapshotFanout _fanout;
    private readonly ILogger<PostgresBackplaneListener> _logger;

    public PostgresBackplaneListener(
        IOptions<RealtimeOptions> options,
        IGameSnapshotCache snapshotCache,
        IGameSnapshotFanout fanout,
        ILogger<PostgresBackplaneListener> logger)
    {
        _options = options.Value.Backplane;
        _snapshotCache = snapshotCache;
        _fanout = fanout;
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
                var envelope = ParseEnvelope(payload);
                if (envelope is null)
                {
                    continue;
                }

                GameSnapshotCacheSync.Apply(_snapshotCache, envelope);
                await _fanout.DispatchAsync(envelope, stoppingToken);
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

    private static GameRealtimeEnvelope? ParseEnvelope(string payload)
    {
        using var document = JsonDocument.Parse(payload);
        if (document.RootElement.TryGetProperty("notification", out _))
        {
            return GameRealtimeEnvelope.FromJson(payload);
        }

        // Backward-compatible: older lightweight messages without an envelope wrapper.
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
