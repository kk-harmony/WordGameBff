using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WordGameBff.Application.Configuration;

namespace WordGameBff.Infrastructure.Realtime.Redis;

public sealed class RedisBackplaneListener : BackgroundService
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(5);

    private readonly IRedisBackplaneMessaging _messaging;
    private readonly BackplaneEnvelopeDispatcher _dispatcher;
    private readonly string _channelName;
    private readonly ILogger<RedisBackplaneListener> _logger;

    public RedisBackplaneListener(
        IRedisBackplaneMessaging messaging,
        BackplaneEnvelopeDispatcher dispatcher,
        IOptions<RealtimeOptions> options,
        ILogger<RedisBackplaneListener> logger)
    {
        _messaging = messaging;
        _dispatcher = dispatcher;
        _channelName = string.IsNullOrWhiteSpace(options.Value.Backplane.ChannelName)
            ? RedisGameRealtimeBackplane.DefaultChannelName
            : options.Value.Backplane.ChannelName;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await _messaging.RunSubscriberAsync(
                    _channelName,
                    (payload, ct) => _dispatcher.DispatchAsync(payload, ct),
                    stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Redis backplane listener failed; retrying in {RetrySeconds} seconds", RetryDelay.TotalSeconds);
                await Task.Delay(RetryDelay, stoppingToken);
            }
        }
    }
}
