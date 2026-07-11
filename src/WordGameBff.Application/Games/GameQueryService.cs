using System.Text.Json;
using WordGameBff.Application.Realtime;
using WordGameBff.Domain.Models;

namespace WordGameBff.Application.Games;

public interface IGameQueryService
{
    Task<AppOutcome> GetGameAsync(string userId, long gameId, CancellationToken cancellationToken = default);
    Task<AppOutcome> GetAssignedWordAsync(string userId, long gameId, CancellationToken cancellationToken = default);
    Task<AppOutcome> GetWordPairAsync(string userId, long gameId, CancellationToken cancellationToken = default);
}

public sealed class GameQueryService : IGameQueryService
{
    private readonly IGameApiClient _gameApiClient;
    private readonly IGameResponseBuilder _responseBuilder;
    private readonly IUpstreamErrorNormalizer _errorNormalizer;

    public GameQueryService(
        IGameApiClient gameApiClient,
        IGameResponseBuilder responseBuilder,
        IUpstreamErrorNormalizer errorNormalizer)
    {
        _gameApiClient = gameApiClient;
        _responseBuilder = responseBuilder;
        _errorNormalizer = errorNormalizer;
    }

    public async Task<AppOutcome> GetGameAsync(
        string userId,
        long gameId,
        CancellationToken cancellationToken = default)
    {
        var response = await _gameApiClient.GetGameAsync(userId, gameId, cancellationToken);
        return await ToSanitizedGameResultAsync(response, userId, cancellationToken);
    }

    public async Task<AppOutcome> GetAssignedWordAsync(
        string userId,
        long gameId,
        CancellationToken cancellationToken = default)
    {
        var response = await _gameApiClient.GetMyWordAsync(userId, gameId, cancellationToken);
        return response.ToPassthrough(_errorNormalizer);
    }

    public async Task<AppOutcome> GetWordPairAsync(
        string userId,
        long gameId,
        CancellationToken cancellationToken = default)
    {
        var response = await _gameApiClient.GetGameAsync(userId, gameId, cancellationToken);
        if (!response.IsSuccess)
        {
            return response.ToPassthrough(_errorNormalizer);
        }

        var game = JsonSerializer.Deserialize<Game>(response.Body, RealtimeJson.Options);
        if (game is null)
        {
            return AppOutcomes.NotFound("NOT_FOUND", "Game not found.");
        }

        if (!IsMember(userId, game))
        {
            return AppOutcomes.Forbidden("FORBIDDEN", "Not a member of this game.");
        }

        if (!GameStatusRules.IsFinished(game.Status))
        {
            return AppOutcomes.Conflict(
                "GAME_NOT_FINISHED",
                "The word pair is only available after the game has finished.");
        }

        var secretWord = game.SecretWord;
        if (secretWord is null
            || string.IsNullOrWhiteSpace(secretWord.Authentic)
            || string.IsNullOrWhiteSpace(secretWord.Imposed))
        {
            return AppOutcomes.Conflict(
                "WORD_PAIR_UNAVAILABLE",
                "The word pair is not available for this game.");
        }

        return AppOutcomes.Ok(new ClientSecretWordResponse
        {
            Id = secretWord.Id,
            Authentic = secretWord.Authentic,
            Imposed = secretWord.Imposed
        });
    }

    private async Task<AppOutcome> ToSanitizedGameResultAsync(
        GameApiResponse response,
        string userId,
        CancellationToken cancellationToken)
    {
        if (!response.IsSuccess)
        {
            return response.ToPassthrough(_errorNormalizer);
        }

        var game = JsonSerializer.Deserialize<Game>(response.Body, RealtimeJson.Options);
        if (game is null)
        {
            return response.ToPassthrough(_errorNormalizer);
        }

        var enriched = await _responseBuilder.BuildAsync(game, userId, cancellationToken);
        return AppOutcomes.Ok(enriched);
    }

    private static bool IsMember(string userId, Game game) =>
        string.Equals(game.AdminUserId, userId, StringComparison.Ordinal)
        || (game.Members?.Any(m => string.Equals(m.UserId, userId, StringComparison.Ordinal)) ?? false);
}
