using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WordGameBff.Application.Auth;
using WordGameBff.Application.Configuration;
using WordGameBff.Application.Games;
using WordGameBff.Application.Realtime;
using WordGameBff.Infrastructure.Auth;
using WordGameBff.Infrastructure.Games;
using WordGameBff.Infrastructure.Realtime;
using WordGameBff.Infrastructure.Realtime.Redis;
using WordGameBff.Infrastructure.Realtime.SignalR;
using WordGameBff.Infrastructure.Storage;

namespace WordGameBff.Infrastructure;

public static class InfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddWordGameBffInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        services.Configure<GameApiOptions>(configuration.GetSection(GameApiOptions.SectionName));
        services.Configure<CustomAuthOptions>(configuration.GetSection(CustomAuthOptions.SectionName));
        services.Configure<SessionOptions>(configuration.GetSection(SessionOptions.SectionName));
        services.Configure<PowOptions>(configuration.GetSection(PowOptions.SectionName));
        services.Configure<CorsSettings>(configuration.GetSection(CorsSettings.SectionName));
        services.Configure<RealtimeOptions>(configuration.GetSection(RealtimeOptions.SectionName));
        services.Configure<GameSnapshotOptions>(configuration.GetSection(GameSnapshotOptions.SectionName));
        services.Configure<StoreOptions>(configuration.GetSection(StoreOptions.SectionName));

        services.AddMemoryCache();
        services.AddSingleton<IGameSnapshotCache, MemoryGameSnapshotCache>();
        services.AddSingleton<GameRealtimeEnvelopePayloadBuilder>();
        services.AddSingleton<BackplaneEnvelopeDispatcher>();

        var usePostgresStores = StoreConnectionResolver.UsePostgreSqlStores(configuration);
        var storeConnectionString = StoreConnectionResolver.Resolve(configuration);

        if (usePostgresStores)
        {
            if (string.IsNullOrWhiteSpace(storeConnectionString))
            {
                throw new InvalidOperationException(
                    "Stores:Type is PostgreSQL but no connection string is configured. Set Stores:ConnectionString.");
            }

            services.AddSingleton(new PostgresStoreConnection(storeConnectionString));
            services.AddSingleton(new PostgresKeyValueStore(storeConnectionString));
            services.AddSingleton<IChallengeStore, PostgresChallengeStore>();
            services.AddSingleton<ISessionRevocationStore, PostgresSessionRevocationStore>();
            services.AddSingleton<IGameRevisionStore, PostgresGameRevisionStore>();
            services.AddSingleton<IGameConnectionRegistry, PostgresGameConnectionRegistry>();
            services.AddSingleton<IGameSelfVoteStore, PostgresGameSelfVoteStore>();
            services.AddHostedService<PostgresStoreCleanupService>();
            services.AddHostedService<PostgresSchemaInitializer>();
        }
        else
        {
            services.AddSingleton<IChallengeStore, MemoryChallengeStore>();
            services.AddSingleton<ISessionRevocationStore, MemorySessionRevocationStore>();
            services.AddSingleton<IGameRevisionStore, InMemoryGameRevisionStore>();
            services.AddSingleton<IGameConnectionRegistry, InMemoryGameConnectionRegistry>();
            services.AddSingleton<IGameSelfVoteStore, InMemoryGameSelfVoteStore>();
        }

        services.AddHttpClient<ICustomAuthTokenService, CustomAuthTokenService>();
        services.AddHttpClient<IGameApiClient, GameApiClient>();
        services.AddHostedService<GameApiWarmupService>();

        var realtime = configuration.GetSection(RealtimeOptions.SectionName).Get<RealtimeOptions>() ?? new RealtimeOptions();
        if (string.Equals(realtime.Transport, "SignalR", StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<IGameRealtimeTransport, SignalRGameRealtimeTransport>();
        }

        var useRedisBackplane = string.Equals(realtime.BackplaneType, "Redis", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(realtime.Backplane.ConnectionString);

        if (useRedisBackplane)
        {
            services.AddSingleton<IRedisBackplaneMessaging>(sp =>
            {
                var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<RealtimeOptions>>().Value;
                var logger = sp.GetRequiredService<ILogger<EquinoctialRedisBackplaneMessaging>>();
                return EquinoctialRedisBackplaneMessaging
                    .ConnectAsync(options.Backplane.ConnectionString, logger)
                    .GetAwaiter()
                    .GetResult();
            });
            services.AddSingleton<IGameRealtimeBackplane, RedisGameRealtimeBackplane>();
            services.AddHostedService<RedisBackplaneListener>();
        }
        else if (environment.IsDevelopment())
        {
            services.AddSingleton<IGameRealtimeBackplane, InMemoryGameRealtimeBackplane>();
        }
        else
        {
            throw new InvalidOperationException(
                "Production requires Realtime:BackplaneType=Redis with a configured connection string.");
        }

        return services;
    }
}
