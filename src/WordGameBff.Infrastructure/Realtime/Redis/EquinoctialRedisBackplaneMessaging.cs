using Equinoctial.Redis;
using Microsoft.Extensions.Logging;

namespace WordGameBff.Infrastructure.Realtime.Redis;

public sealed class EquinoctialRedisBackplaneMessaging : IRedisBackplaneMessaging, IAsyncDisposable
{
    private readonly RedisClient _client;
    private readonly ILogger<EquinoctialRedisBackplaneMessaging> _logger;

    private EquinoctialRedisBackplaneMessaging(RedisClient client, ILogger<EquinoctialRedisBackplaneMessaging> logger)
    {
        _client = client;
        _logger = logger;
    }

    public static async Task<EquinoctialRedisBackplaneMessaging> ConnectAsync(
        string connectionString,
        ILogger<EquinoctialRedisBackplaneMessaging> logger,
        CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;
        var client = await RedisClient.ConnectAsync(connectionString);
        return new EquinoctialRedisBackplaneMessaging(client, logger);
    }

    public async Task PublishAsync(string channel, string payload, CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;
        await _client.PubSub.PublishAsync(channel, payload);
    }

    public async Task PingAsync(CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;
        await _client.PingAsync();
    }

    public async Task RunSubscriberAsync(
        string channel,
        Func<string, CancellationToken, Task> onMessage,
        CancellationToken stoppingToken)
    {
        await using var subscriber = await _client.PubSub.CreateSubscriberAsync();
        subscriber.MessageReceived += async (_, args) =>
        {
            if (string.IsNullOrEmpty(args.Message))
            {
                return;
            }

            try
            {
                await onMessage(args.Message, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Redis backplane subscriber handler failed");
            }
        };

        await subscriber.SubscribeAsync(channel);
        _logger.LogInformation("Redis backplane subscriber listening on {Channel}", channel);

        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _client.DisposeAsync();
    }
}
