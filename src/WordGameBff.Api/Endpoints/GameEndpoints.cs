using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using WordGameBff.Api.Extensions;
using WordGameBff.Application.Games;
using WordGameBff.Domain.Models;

namespace WordGameBff.Api.Endpoints;

public static class GameEndpoints
{
    public static IEndpointRouteBuilder MapGameEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api")
            .RequireAuthorization()
            .RequireRateLimiting(RateLimitingExtensions.ApiIpPolicy)
            .RequireRateLimiting(RateLimitingExtensions.ApiSessionPolicy);

        group.MapGet("/me", (HttpContext httpContext) =>
        {
            var userId = httpContext.User.GetUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return ResultsExtensions.ApiError(StatusCodes.Status401Unauthorized, "UNAUTHORIZED", "Missing user identity.");
            }

            return Results.Ok(new { userId });
        });

        group.MapPost("/games", async (
            HttpContext httpContext,
            CreateGameRequest request,
            IGameCommandService games,
            CancellationToken cancellationToken) =>
        {
            var result = await games.CreateGameAsync(httpContext.User.GetUserId()!, request, cancellationToken);
            return AppOutcomeMapper.ToHttpResult(result, httpContext);
        });

        group.MapGet("/games/{id:long}", async (
            HttpContext httpContext,
            long id,
            IGameQueryService games,
            CancellationToken cancellationToken) =>
        {
            var result = await games.GetGameAsync(httpContext.User.GetUserId()!, id, cancellationToken);
            return AppOutcomeMapper.ToHttpResult(result);
        });

        group.MapPost("/games/{id:long}/rounds", async (
            HttpContext httpContext,
            long id,
            StartGameRequest request,
            IGameCommandService games,
            CancellationToken cancellationToken) =>
        {
            var result = await games.StartRoundAsync(httpContext.User.GetUserId()!, id, request, cancellationToken);
            return AppOutcomeMapper.ToHttpResult(result);
        });

        group.MapPost("/games/{id:long}/members", async (
            HttpContext httpContext,
            long id,
            [FromBody] JoinGameRequest? request,
            IGameCommandService games,
            CancellationToken cancellationToken) =>
        {
            var result = await games.JoinGameAsync(httpContext.User.GetUserId()!, id, request, cancellationToken);
            return AppOutcomeMapper.ToHttpResult(result);
        });

        group.MapDelete("/games/{id:long}/members/{memberUserId}", async (
            HttpContext httpContext,
            long id,
            string memberUserId,
            IGameCommandService games,
            CancellationToken cancellationToken) =>
        {
            var result = await games.RemoveMemberAsync(
                httpContext.User.GetUserId()!,
                id,
                memberUserId,
                cancellationToken);
            return AppOutcomeMapper.ToHttpResult(result);
        });

        group.MapPost("/games/{id:long}/turns", async (
            HttpContext httpContext,
            long id,
            IGameCommandService games,
            CancellationToken cancellationToken) =>
        {
            var result = await games.CompleteTurnAsync(httpContext.User.GetUserId()!, id, cancellationToken);
            return AppOutcomeMapper.ToHttpResult(result);
        });

        group.MapGet("/games/{id:long}/assigned-word", async (
            HttpContext httpContext,
            long id,
            IGameQueryService games,
            CancellationToken cancellationToken) =>
        {
            var result = await games.GetAssignedWordAsync(httpContext.User.GetUserId()!, id, cancellationToken);
            return AppOutcomeMapper.ToHttpResult(result);
        });

        group.MapGet("/games/{id:long}/word-pair", async (
            HttpContext httpContext,
            long id,
            IGameQueryService games,
            CancellationToken cancellationToken) =>
        {
            var result = await games.GetWordPairAsync(httpContext.User.GetUserId()!, id, cancellationToken);
            return AppOutcomeMapper.ToHttpResult(result);
        });

        group.MapPost("/games/{id:long}/votes", async (
            HttpContext httpContext,
            long id,
            VoteRequest request,
            IGameCommandService games,
            CancellationToken cancellationToken) =>
        {
            var result = await games.VoteAsync(httpContext.User.GetUserId()!, id, request, cancellationToken);
            return AppOutcomeMapper.ToHttpResult(result);
        });

        return app;
    }
}
