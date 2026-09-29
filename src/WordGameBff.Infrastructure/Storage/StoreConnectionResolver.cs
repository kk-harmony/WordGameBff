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
    /// When <c>Stores:Type=Redis</c>, uses <see cref="StoreOptions.ConnectionString"/> if set;
    /// otherwise the SignalR backplane Redis URL. Postgres store strings must not be used here.
    /// </summary>
    public static string ResolveRedisConnectionString(IConfiguration configuration)
    {
        var storeOptions = configuration.GetSection(StoreOptions.SectionName).Get<StoreOptions>() ?? new StoreOptions();
        if (UseRedisStores(configuration) && !string.IsNullOrWhiteSpace(storeOptions.ConnectionString))
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
