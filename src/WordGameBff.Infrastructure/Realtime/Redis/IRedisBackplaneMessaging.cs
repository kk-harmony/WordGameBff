namespace WordGameBff.Infrastructure.Realtime.Redis;

public interface IRedisBackplaneMessaging
{
    Task PublishAsync(string channel, string payload, CancellationToken cancellationToken = default);
    Task PingAsync(CancellationToken cancellationToken = default);
    Task RunSubscriberAsync(
        string channel,
        Func<string, CancellationToken, Task> onMessage,
        CancellationToken stoppingToken);
}
