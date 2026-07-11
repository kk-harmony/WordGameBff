using Microsoft.AspNetCore.Diagnostics;
using WordGameBff.Application.Auth;
using WordGameBff.Domain.Models;

namespace WordGameBff.Api.Middleware;

public sealed class GlobalExceptionHandler : IExceptionHandler
{
    private readonly IHostEnvironment _environment;
    private readonly ILogger<GlobalExceptionHandler> _logger;

    public GlobalExceptionHandler(IHostEnvironment environment, ILogger<GlobalExceptionHandler> logger)
    {
        _environment = environment;
        _logger = logger;
    }

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        _logger.LogError(
            exception,
            "Unhandled exception for {Method} {Path}",
            httpContext.Request.Method,
            httpContext.Request.Path);

        var (statusCode, error, message) = exception switch
        {
            UpstreamAuthException =>
                (StatusCodes.Status502BadGateway, "UPSTREAM_AUTH_ERROR", "Failed to obtain upstream credentials."),
            _ => (
                StatusCodes.Status500InternalServerError,
                "INTERNAL_ERROR",
                _environment.IsDevelopment()
                    ? exception.Message
                    : "An unexpected error occurred."),
        };

        httpContext.Response.StatusCode = statusCode;
        httpContext.Response.ContentType = "application/json";
        await httpContext.Response.WriteAsJsonAsync(
            new ApiError { Error = error, Message = message },
            cancellationToken);

        return true;
    }
}
