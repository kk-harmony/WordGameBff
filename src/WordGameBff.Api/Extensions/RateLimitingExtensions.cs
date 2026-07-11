using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using WordGameBff.Application.Configuration;

namespace WordGameBff.Api.Extensions;

public static class RateLimitingExtensions
{
    public const string AuthIpPolicy = "auth-ip";
    public const string ApiIpPolicy = "api-ip";
    public const string ApiSessionPolicy = "api-session";
    public const string HubIpPolicy = "hub-ip";

    public static IServiceCollection AddWordGameBffRateLimiting(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<RateLimitingOptions>(configuration.GetSection(RateLimitingOptions.SectionName));

        services.AddRateLimiter(options =>
        {
            var rateLimits = configuration.GetSection(RateLimitingOptions.SectionName).Get<RateLimitingOptions>()
                ?? new RateLimitingOptions();

            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = async (context, cancellationToken) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    context.HttpContext.Response.Headers.RetryAfter =
                        ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
                }
                else
                {
                    context.HttpContext.Response.Headers.RetryAfter = "60";
                }

                context.HttpContext.Response.ContentType = "application/json";
                await context.HttpContext.Response.WriteAsJsonAsync(
                    new { error = "RATE_LIMITED", message = "Too many requests." },
                    cancellationToken);
            };

            options.AddPolicy(AuthIpPolicy, httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    GetIp(httpContext),
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = rateLimits.AuthIpPermitLimit,
                        Window = TimeSpan.FromMinutes(rateLimits.AuthIpWindowMinutes),
                        QueueLimit = 0
                    }));

            options.AddPolicy(ApiIpPolicy, httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    GetIp(httpContext),
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = rateLimits.ApiIpPermitLimit,
                        Window = TimeSpan.FromMinutes(rateLimits.ApiIpWindowMinutes),
                        QueueLimit = 0
                    }));

            options.AddPolicy(ApiSessionPolicy, httpContext =>
            {
                var userId = httpContext.User.FindFirst("sub")?.Value ?? "anonymous";
                return RateLimitPartition.GetFixedWindowLimiter(
                    userId,
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = rateLimits.ApiSessionPermitLimit,
                        Window = TimeSpan.FromMinutes(rateLimits.ApiSessionWindowMinutes),
                        QueueLimit = 0
                    });
            });

            options.AddPolicy(HubIpPolicy, httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    GetIp(httpContext),
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = rateLimits.HubIpPermitLimit,
                        Window = TimeSpan.FromMinutes(rateLimits.HubIpWindowMinutes),
                        QueueLimit = 0
                    }));
        });

        return services;
    }

    private static string GetIp(HttpContext httpContext) =>
        httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
}
