using WordGameBff.Domain.Models;

namespace WordGameBff.Application.Games;

public sealed class GameApiResponse
{
    /// <summary>HTTP status from upstream; for <see cref="ToPassthrough"/> construction only — call sites use flags.</summary>
    public required int StatusCode { get; init; }
    public required string Body { get; init; }

    public bool IsSuccess => StatusCode is >= 200 and < 300;
    public bool IsNoContent => StatusCode == 204;

    public AppOutcome ToPassthrough(IUpstreamErrorNormalizer errorNormalizer) =>
        new AppRawJson(
            IsSuccess ? Body : errorNormalizer.NormalizeErrorBody(Body),
            IsSuccess,
            StatusCode);
}

public interface IGameApiClient
{
    Task<GameApiResponse> CreateGameAsync(string userId, CreateGameRequest request, CancellationToken cancellationToken = default);
    Task<GameApiResponse> GetGameAsync(string userId, long gameId, CancellationToken cancellationToken = default);
    Task<GameApiResponse> JoinGameAsync(string userId, long gameId, JoinGameRequest? request = null, CancellationToken cancellationToken = default);
    Task<GameApiResponse> RemoveGameMemberAsync(string userId, long gameId, string memberUserId, CancellationToken cancellationToken = default);
    Task<GameApiResponse> StartGameAsync(string userId, long gameId, StartGameRequest request, CancellationToken cancellationToken = default);
    Task<GameApiResponse> CompleteTurnAsync(string userId, long gameId, CancellationToken cancellationToken = default);
    Task<GameApiResponse> GetMyWordAsync(string userId, long gameId, CancellationToken cancellationToken = default);
    Task<GameApiResponse> VoteAsync(string userId, long gameId, VoteRequest request, CancellationToken cancellationToken = default);
    Task<GameApiResponse> GetRandomSecretWordAsync(string userId, CancellationToken cancellationToken = default);
    Task<GameApiResponse> CreateSecretWordAsync(string userId, SecretWord request, CancellationToken cancellationToken = default);
    Task<GameApiResponse> GetSecretWordAsync(string userId, long secretWordId, CancellationToken cancellationToken = default);
    Task<Game?> GetGameModelAsync(string userId, long gameId, CancellationToken cancellationToken = default);
}
