using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace WordGameBff.Infrastructure.Realtime.Redis;

public sealed class RedisBackplaneHealthCheck : IHealthCheck
{
    private readonly IRedisBackplaneMessaging _messaging;

    public RedisBackplaneHealthCheck(IRedisBackplaneMessaging messaging)
    {
        _messaging = messaging;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await _messaging.PingAsync(cancellationToken);
            return HealthCheckResult.Healthy("Redis backplane reachable");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Redis backplane unreachable", ex);
        }
    }
}
