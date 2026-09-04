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

    public static bool UsePostgreSqlStores(IConfiguration configuration)
    {
        var storeOptions = configuration.GetSection(StoreOptions.SectionName).Get<StoreOptions>() ?? new StoreOptions();
        return string.Equals(storeOptions.Type, "PostgreSQL", StringComparison.OrdinalIgnoreCase);
    }
}
