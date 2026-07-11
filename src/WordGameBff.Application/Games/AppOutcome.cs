namespace WordGameBff.Application.Games;

/// <summary>
/// HTTP-free Application outcome. Api maps this to status codes and Location headers.
/// </summary>
public abstract record AppOutcome;

public enum AppSuccessKind
{
    Ok,
    Created,
}

public enum AppFailureKind
{
    NotFound,
    Forbidden,
    Unauthorized,
    BadRequest,
    Conflict,
    Upstream,
}

public sealed record AppSuccess(object Body, AppSuccessKind Kind, string? ResourceId = null) : AppOutcome;

public sealed record AppRawJson(string Json, bool IsSuccess, int UpstreamStatus) : AppOutcome;

public sealed record AppFailure(string Code, string Message, AppFailureKind Kind) : AppOutcome;

public sealed record AppNoContent : AppOutcome;

public static class AppOutcomes
{
    public static AppSuccess Ok(object body) => new(body, AppSuccessKind.Ok);

    public static AppSuccess Created(object body, string resourceId) =>
        new(body, AppSuccessKind.Created, resourceId);

    public static AppNoContent NoContent() => new();

    public static AppFailure Fail(string code, string message, AppFailureKind kind) =>
        new(code, message, kind);

    public static AppFailure NotFound(string code, string message) =>
        Fail(code, message, AppFailureKind.NotFound);

    public static AppFailure Forbidden(string code, string message) =>
        Fail(code, message, AppFailureKind.Forbidden);

    public static AppFailure BadRequest(string code, string message) =>
        Fail(code, message, AppFailureKind.BadRequest);

    public static AppFailure Conflict(string code, string message) =>
        Fail(code, message, AppFailureKind.Conflict);

    public static AppFailureKind FailureKindFromErrorCode(string errorCode) =>
        errorCode switch
        {
            "FORBIDDEN" => AppFailureKind.Forbidden,
            "NOT_FOUND" => AppFailureKind.NotFound,
            "GAME_NOT_FINISHED" => AppFailureKind.Conflict,
            "WORD_PAIR_UNAVAILABLE" => AppFailureKind.Conflict,
            _ => AppFailureKind.BadRequest,
        };
}
