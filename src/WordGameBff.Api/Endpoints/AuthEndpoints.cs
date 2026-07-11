using Microsoft.AspNetCore.RateLimiting;
using WordGameBff.Api.Extensions;
using WordGameBff.Application.Auth;
using WordGameBff.Application.Games;

namespace WordGameBff.Api.Endpoints;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/auth").RequireRateLimiting(RateLimitingExtensions.AuthIpPolicy);

        group.MapGet("/challenge", async (IPowChallengeService powService, CancellationToken cancellationToken) =>
        {
            var challenge = await powService.CreateChallengeAsync(cancellationToken);
            return Results.Ok(new
            {
                challengeId = challenge.ChallengeId,
                prefix = challenge.Prefix,
                difficulty = challenge.Difficulty,
                expiresAt = challenge.ExpiresAt
            });
        });

        group.MapPost("/verify", async (
            VerifyRequest request,
            IPowChallengeService powService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var result = await powService.VerifyAsync(request.ChallengeId, request.Nonce, cancellationToken);
                return Results.Ok(new
                {
                    sessionToken = result.SessionToken,
                    userId = result.UserId,
                    expiresAt = result.ExpiresAt
                });
            }
            catch (PowVerificationException ex)
            {
                return ResultsExtensions.ApiError(StatusCodes.Status400BadRequest, ex.ErrorCode, ex.Message);
            }
        });

        group.MapPost("/logout", async (
            HttpContext httpContext,
            ISessionLogoutService logoutService,
            CancellationToken cancellationToken) =>
        {
            var outcome = await logoutService.LogoutAsync(
                httpContext.Request.Headers.Authorization.ToString(),
                cancellationToken);
            return AppOutcomeMapper.ToHttpResult(outcome);
        });

        return app;
    }

    private sealed record VerifyRequest(string ChallengeId, string Nonce);
}
