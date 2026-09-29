using System.Text.Json;
using WordGameBff.Application.Realtime;
using WordGameBff.Domain.Models;

namespace WordGameBff.Application.Games;

public interface ISecretWordService
{
    Task<AppOutcome> GetRandomAsync(string userId, long gameId, CancellationToken cancellationToken = default);
    Task<AppOutcome> GetRandomForSessionAsync(string userId, string sessionCode, CancellationToken cancellationToken = default);
    Task<AppOutcome> GetByIdAsync(string userId, long gameId, long secretWordId, CancellationToken cancellationToken = default);
    Task<AppOutcome> CreateAsync(string userId, SecretWord request, CancellationToken cancellationToken = default);
}

public sealed class SecretWordService : ISecretWordService
{
    private readonly IGameApiClient _gameApiClient;
    private readonly IGameSnapshotReader _snapshotReader;
    private readonly ISecretWordAccessPolicy _accessPolicy;
    private readonly ISecretWordResponseBuilder _responseBuilder;
    private readonly IUpstreamErrorNormalizer _errorNormalizer;

    public SecretWordService(
        IGameApiClient gameApiClient,
        IGameSnapshotReader snapshotReader,
        ISecretWordAccessPolicy accessPolicy,
        ISecretWordResponseBuilder responseBuilder,
        IUpstreamErrorNormalizer errorNormalizer)
    {
        _gameApiClient = gameApiClient;
        _snapshotReader = snapshotReader;
        _accessPolicy = accessPolicy;
        _responseBuilder = responseBuilder;
        _errorNormalizer = errorNormalizer;
    }

    public async Task<AppOutcome> GetRandomAsync(
        string userId,
        long gameId,
        CancellationToken cancellationToken = default)
    {
        var access = await EnsureCanViewWordPairAsync(userId, gameId, cancellationToken);
        if (access is not null)
        {
            return access;
        }

        var response = await _gameApiClient.GetRandomSecretWordAsync(userId, cancellationToken);
        return BuildSecretWordResult(response, includeWordPair: true);
    }

    public async Task<AppOutcome> GetRandomForSessionAsync(
        string userId,
        string sessionCode,
        CancellationToken cancellationToken = default)
    {
        var sessionResponse = await _gameApiClient.GetSessionAsync(userId, sessionCode, cancellationToken);
        if (!sessionResponse.IsSuccess)
        {
            return sessionResponse.ToPassthrough(_errorNormalizer);
        }

        var session = JsonSerializer.Deserialize<GameSession>(sessionResponse.Body, RealtimeJson.Options);
        if (session is null)
        {
            return AppOutcomes.NotFound("NOT_FOUND", "Session not found.");
        }

        if (!string.Equals(session.AdminUserId, userId, StringComparison.Ordinal))
        {
            return AppOutcomes.Forbidden(
                "FORBIDDEN",
                "Only the session admin can view secret word pairs before the game starts.");
        }

        if (IsActiveSessionGameStatus(session.CurrentGameStatus))
        {
            return AppOutcomes.Forbidden(
                "FORBIDDEN",
                "Secret word pairs are only available between games.");
        }

        var response = await _gameApiClient.GetRandomSecretWordAsync(userId, cancellationToken);
        return BuildSecretWordResult(response, includeWordPair: true);
    }

    private static bool IsActiveSessionGameStatus(string? status) =>
        string.Equals(status, "IN_PROGRESS", StringComparison.OrdinalIgnoreCase)
        || string.Equals(status, "VOTING", StringComparison.OrdinalIgnoreCase);

    public async Task<AppOutcome> GetByIdAsync(
        string userId,
        long gameId,
        long secretWordId,
        CancellationToken cancellationToken = default)
    {
        var access = await EnsureCanViewWordPairAsync(userId, gameId, cancellationToken);
        if (access is not null)
        {
            return access;
        }

        var response = await _gameApiClient.GetSecretWordAsync(userId, secretWordId, cancellationToken);
        return BuildSecretWordResult(response, includeWordPair: true);
    }

    public async Task<AppOutcome> CreateAsync(
        string userId,
        SecretWord request,
        CancellationToken cancellationToken = default)
    {
        var response = await _gameApiClient.CreateSecretWordAsync(userId, request, cancellationToken);
        return BuildSecretWordResult(response, includeWordPair: true);
    }

    private async Task<AppOutcome?> EnsureCanViewWordPairAsync(
        string userId,
        long gameId,
        CancellationToken cancellationToken)
    {
        var game = await _snapshotReader.GetGameModelAsync(userId, gameId, cancellationToken);
        if (game is null)
        {
            return AppOutcomes.NotFound("NOT_FOUND", "Game not found.");
        }

        if (!_accessPolicy.CanViewWordPair(userId, game))
        {
            return AppOutcomes.Forbidden(
                "FORBIDDEN",
                "Only the game admin can view secret word pairs before the game starts.");
        }

        return null;
    }

    private AppOutcome BuildSecretWordResult(GameApiResponse response, bool includeWordPair)
    {
        if (!response.IsSuccess)
        {
            return response.ToPassthrough(_errorNormalizer);
        }

        var secretWord = JsonSerializer.Deserialize<SecretWord>(response.Body, RealtimeJson.Options);
        if (secretWord is null)
        {
            return response.ToPassthrough(_errorNormalizer);
        }

        var clientResponse = _responseBuilder.Build(secretWord, includeWordPair);
        return AppOutcomes.Ok(clientResponse);
    }
}
