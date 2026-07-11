using WordGameBff.Application.Games;
using WordGameBff.Application.Realtime;
using WordGameBff.Api.Extensions;

namespace WordGameBff.Api.Endpoints;

internal static class AppOutcomeMapper
{
    public static IResult ToHttpResult(AppOutcome outcome, HttpContext? httpContext = null)
    {
        switch (outcome)
        {
            case AppNoContent:
                return Results.NoContent();

            case AppFailure failure:
                return ResultsExtensions.ApiError(
                    ToStatusCode(failure.Kind),
                    failure.Code,
                    failure.Message);

            case AppRawJson raw:
                return Results.Content(raw.Json, "application/json", statusCode: raw.UpstreamStatus);

            case AppSuccess success:
                if (success.Kind == AppSuccessKind.Created &&
                    !string.IsNullOrEmpty(success.ResourceId) &&
                    httpContext is not null)
                {
                    httpContext.Response.Headers.Location = $"/api/games/{success.ResourceId}";
                    return Results.Json(success.Body, RealtimeJson.Options, statusCode: StatusCodes.Status201Created);
                }

                return Results.Json(success.Body, RealtimeJson.Options, statusCode: StatusCodes.Status200OK);

            default:
                return ResultsExtensions.ApiError(
                    StatusCodes.Status500InternalServerError,
                    "INTERNAL_ERROR",
                    "Empty application outcome.");
        }
    }

    private static int ToStatusCode(AppFailureKind kind) =>
        kind switch
        {
            AppFailureKind.NotFound => StatusCodes.Status404NotFound,
            AppFailureKind.Forbidden => StatusCodes.Status403Forbidden,
            AppFailureKind.Unauthorized => StatusCodes.Status401Unauthorized,
            AppFailureKind.BadRequest => StatusCodes.Status400BadRequest,
            AppFailureKind.Conflict => StatusCodes.Status409Conflict,
            AppFailureKind.Upstream => StatusCodes.Status502BadGateway,
            _ => StatusCodes.Status400BadRequest,
        };
}
