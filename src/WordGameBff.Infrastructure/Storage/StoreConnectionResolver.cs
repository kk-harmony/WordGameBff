using Microsoft.Extensions.Configuration;
using WordGameBff.Application.Configuration;

namespace WordGameBff.Infrastructure.Storage;

public static class StoreConnectionResolver
{
    public static string Resolve(IConfiguration configuration)
    {
        var storeOptions = configuration.GetSection(StoreOptions.SectionName).Get<StoreOptions>() ?? new StoreOptions();
        return storeOptions.ConnectionString;
    }

    /// <summary>
    /// Redis stores use <see cref="StoreOptions.ConnectionString"/> when set; otherwise the SignalR backplane Redis URL.
    /// </summary>
    public static string ResolveRedisConnectionString(IConfiguration configuration)
    {
        var storeOptions = configuration.GetSection(StoreOptions.SectionName).Get<StoreOptions>() ?? new StoreOptions();
        if (!string.IsNullOrWhiteSpace(storeOptions.ConnectionString))
        {
            return storeOptions.ConnectionString;
        }

        var realtime = configuration.GetSection(RealtimeOptions.SectionName).Get<RealtimeOptions>() ?? new RealtimeOptions();
        return realtime.Backplane.ConnectionString ?? string.Empty;
    }

    public static bool UsePostgreSqlStores(IConfiguration configuration)
    {
        var storeOptions = configuration.GetSection(StoreOptions.SectionName).Get<StoreOptions>() ?? new StoreOptions();
        return string.Equals(storeOptions.Type, "PostgreSQL", StringComparison.OrdinalIgnoreCase);
    }

    public static bool UseRedisStores(IConfiguration configuration)
    {
        var storeOptions = configuration.GetSection(StoreOptions.SectionName).Get<StoreOptions>() ?? new StoreOptions();
        return string.Equals(storeOptions.Type, "Redis", StringComparison.OrdinalIgnoreCase);
    }
}
