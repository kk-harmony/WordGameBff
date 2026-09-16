using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using WordGameBff.Api.Extensions;
using WordGameBff.Application.Games;
using WordGameBff.Domain.Models;

namespace WordGameBff.Api.Endpoints;

public static class SessionEndpoints
{
    public static IEndpointRouteBuilder MapSessionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api")
            .RequireAuthorization()
            .RequireRateLimiting(RateLimitingExtensions.ApiIpPolicy)
            .RequireRateLimiting(RateLimitingExtensions.ApiSessionPolicy);

        group.MapPost("/sessions", async (
            HttpContext httpContext,
            CreateSessionRequest request,
            ISessionService sessions,
            CancellationToken cancellationToken) =>
        {
            var result = await sessions.CreateSessionAsync(httpContext.User.GetUserId()!, request, cancellationToken);
            return AppOutcomeMapper.ToHttpResult(result, httpContext);
        });

        group.MapGet("/sessions/{code}", async (
            HttpContext httpContext,
            string code,
            ISessionService sessions,
            CancellationToken cancellationToken) =>
        {
            var result = await sessions.GetSessionAsync(httpContext.User.GetUserId()!, code, cancellationToken);
            return AppOutcomeMapper.ToHttpResult(result);
        });

        group.MapPost("/sessions/{code}/members", async (
            HttpContext httpContext,
            string code,
            [FromBody] JoinSessionRequest? request,
            ISessionService sessions,
            CancellationToken cancellationToken) =>
        {
            var result = await sessions.JoinSessionAsync(httpContext.User.GetUserId()!, code, request, cancellationToken);
            return AppOutcomeMapper.ToHttpResult(result);
        });

        group.MapDelete("/sessions/{code}/members/{memberUserId}", async (
            HttpContext httpContext,
            string code,
            string memberUserId,
            ISessionService sessions,
            CancellationToken cancellationToken) =>
        {
            var result = await sessions.RemoveMemberAsync(
                httpContext.User.GetUserId()!,
                code,
                memberUserId,
                cancellationToken);
            return AppOutcomeMapper.ToHttpResult(result);
        });

        group.MapPost("/sessions/{code}/games", async (
            HttpContext httpContext,
            string code,
            StartGameRequest request,
            ISessionService sessions,
            CancellationToken cancellationToken) =>
        {
            var result = await sessions.StartGameAsync(httpContext.User.GetUserId()!, code, request, cancellationToken);
            return AppOutcomeMapper.ToHttpResult(result, httpContext);
        });

        return app;
    }
}
