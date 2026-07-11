using Microsoft.Extensions.DependencyInjection;
using WordGameBff.Application.Auth;
using WordGameBff.Application.Games;
using WordGameBff.Application.Realtime;

namespace WordGameBff.Application;

public static class ApplicationServiceCollectionExtensions
{
    public static IServiceCollection AddWordGameBffApplication(this IServiceCollection services)
    {
        services.AddSingleton<IGameSanitizer, GameSanitizer>();
        services.AddSingleton<IGameResponseBuilder, GameResponseBuilder>();
        services.AddSingleton<IGamePresenceEnricher, GamePresenceEnricher>();
        services.AddSingleton<IUpstreamErrorNormalizer, UpstreamErrorNormalizer>();
        services.AddSingleton<IIdempotencyKeyGenerator, GuidIdempotencyKeyGenerator>();
        services.AddSingleton<ISecretWordAccessPolicy, SecretWordAccessPolicy>();
        services.AddSingleton<ISecretWordResponseBuilder, SecretWordResponseBuilder>();
        services.AddScoped<IPowChallengeService, PowChallengeService>();
        services.AddScoped<ISessionTokenService, SessionTokenService>();
        services.AddScoped<ISessionLogoutService, SessionLogoutService>();
        services.AddScoped<IGameEventPublisher, GameEventPublisher>();
        services.AddScoped<IGameHubJoinService, GameHubJoinService>();
        services.AddScoped<IGameQueryService, GameQueryService>();
        services.AddScoped<IGameCommandService, GameCommandService>();
        services.AddScoped<ISecretWordService, SecretWordService>();
        return services;
    }
}
