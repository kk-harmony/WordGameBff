using Microsoft.AspNetCore.RateLimiting;
using WordGameBff.Api.Extensions;
using WordGameBff.Application.Games;
using WordGameBff.Domain.Models;

namespace WordGameBff.Api.Endpoints;

public static class SecretWordEndpoints
{
    public static IEndpointRouteBuilder MapSecretWordEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api")
            .RequireAuthorization()
            .RequireRateLimiting(RateLimitingExtensions.ApiIpPolicy)
            .RequireRateLimiting(RateLimitingExtensions.ApiSessionPolicy);

        group.MapGet("/games/{gameId:long}/secret-words/random", async (
            HttpContext httpContext,
            long gameId,
            ISecretWordService secretWords,
            CancellationToken cancellationToken) =>
        {
            var result = await secretWords.GetRandomAsync(httpContext.User.GetUserId()!, gameId, cancellationToken);
            return AppOutcomeMapper.ToHttpResult(result);
        });

        group.MapGet("/games/{gameId:long}/secret-words/{id:long}", async (
            HttpContext httpContext,
            long gameId,
            long id,
            ISecretWordService secretWords,
            CancellationToken cancellationToken) =>
        {
            var result = await secretWords.GetByIdAsync(httpContext.User.GetUserId()!, gameId, id, cancellationToken);
            return AppOutcomeMapper.ToHttpResult(result);
        });

        group.MapPost("/secret-words", async (
            HttpContext httpContext,
            SecretWord request,
            ISecretWordService secretWords,
            CancellationToken cancellationToken) =>
        {
            var result = await secretWords.CreateAsync(httpContext.User.GetUserId()!, request, cancellationToken);
            return AppOutcomeMapper.ToHttpResult(result);
        });

        return app;
    }
}
