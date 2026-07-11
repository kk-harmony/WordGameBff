using Microsoft.Extensions.Configuration;
using WordGameBff.Infrastructure.Storage;

namespace WordGameBff.Tests;

public class StoreConnectionResolverTests
{
    [Fact]
    public void UsePostgreSqlStores_ReturnsFalse_WhenTypeIsInMemory()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Stores:Type"] = "InMemory",
            })
            .Build();

        Assert.False(StoreConnectionResolver.UsePostgreSqlStores(configuration));
    }

    [Fact]
    public void UsePostgreSqlStores_ReturnsTrue_WhenTypeIsPostgreSql()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Stores:Type"] = "PostgreSQL",
            })
            .Build();

        Assert.True(StoreConnectionResolver.UsePostgreSqlStores(configuration));
    }

    [Fact]
    public void Resolve_PrefersStoreConnectionString_OverBackplane()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Stores:ConnectionString"] = "Host=store;",
                ["Realtime:Backplane:ConnectionString"] = "Host=backplane;",
            })
            .Build();

        Assert.Equal("Host=store;", StoreConnectionResolver.Resolve(configuration));
    }

    [Fact]
    public void Resolve_FallsBackToBackplaneConnectionString()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Realtime:Backplane:ConnectionString"] = "Host=backplane;",
            })
            .Build();

        Assert.Equal("Host=backplane;", StoreConnectionResolver.Resolve(configuration));
    }
}
