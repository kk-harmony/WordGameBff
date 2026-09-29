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
        Assert.False(StoreConnectionResolver.UseRedisStores(configuration));
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
    public void UseRedisStores_ReturnsTrue_WhenTypeIsRedis()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Stores:Type"] = "Redis",
            })
            .Build();

        Assert.True(StoreConnectionResolver.UseRedisStores(configuration));
        Assert.False(StoreConnectionResolver.UsePostgreSqlStores(configuration));
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
    public void Resolve_DoesNotFallBackToRedisBackplaneConnectionString()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Realtime:Backplane:ConnectionString"] = "host=redis;port=6379",
            })
            .Build();

        Assert.Equal(string.Empty, StoreConnectionResolver.Resolve(configuration));
    }

    [Fact]
    public void ResolveRedisConnectionString_FallsBackToBackplane()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Stores:Type"] = "Redis",
                ["Realtime:Backplane:ConnectionString"] = "host=redis;port=6379;password=secret",
            })
            .Build();

        Assert.Equal(
            "host=redis;port=6379;password=secret",
            StoreConnectionResolver.ResolveRedisConnectionString(configuration));
    }

    [Fact]
    public void ResolveRedisConnectionString_PrefersStoresConnectionString_WhenTypeIsRedis()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Stores:Type"] = "Redis",
                ["Stores:ConnectionString"] = "host=stores-redis;port=6379",
                ["Realtime:Backplane:ConnectionString"] = "host=backplane-redis;port=6379",
            })
            .Build();

        Assert.Equal(
            "host=stores-redis;port=6379",
            StoreConnectionResolver.ResolveRedisConnectionString(configuration));
    }

    [Fact]
    public void ResolveRedisConnectionString_IgnoresPostgresStoreConnectionString()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Stores:Type"] = "PostgreSQL",
                ["Stores:ConnectionString"] = "Host=db;Port=5432;Database=wordgamebff;Username=u;Password=p",
                ["Realtime:Backplane:ConnectionString"] = "cluster://redis-1:6379,redis-2:6379",
            })
            .Build();

        Assert.Equal(
            "cluster://redis-1:6379,redis-2:6379",
            StoreConnectionResolver.ResolveRedisConnectionString(configuration));
    }
}
