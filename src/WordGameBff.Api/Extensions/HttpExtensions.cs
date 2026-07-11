using System.Security.Claims;
using WordGameBff.Domain.Models;

namespace WordGameBff.Api.Extensions;

public static class HttpContextExtensions
{
    public static string? GetUserId(this ClaimsPrincipal user) =>
        user.FindFirst(ClaimTypes.NameIdentifier)?.Value
        ?? user.FindFirst("sub")?.Value;
}

public static class ResultsExtensions
{
    public static IResult ApiError(int statusCode, string error, string message) =>
        Results.Json(new ApiError { Error = error, Message = message }, statusCode: statusCode);
}
