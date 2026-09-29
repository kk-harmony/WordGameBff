using System.Text.Json;
using WordGameBff.Application.Realtime;
using WordGameBff.Domain.Models;

namespace WordGameBff.Application.Games;

public interface ISessionService
{
    Task<AppOutcome> CreateSessionAsync(string userId, CreateSessionRequest request, CancellationToken cancellationToken = default);
    Task<AppOutcome> GetSessionAsync(string userId, string sessionCode, CancellationToken cancellationToken = default);
    Task<AppOutcome> JoinSessionAsync(string userId, string sessionCode, JoinSessionRequest? request, CancellationToken cancellationToken = default);
    Task<AppOutcome> RemoveMemberAsync(string userId, string sessionCode, string memberUserId, CancellationToken cancellationToken = default);
    Task<AppOutcome> StartGameAsync(string userId, string sessionCode, StartGameRequest request, CancellationToken cancellationToken = default);
}

public sealed class SessionService : ISessionService
{
    private readonly IGameApiClient _gameApiClient;
    private readonly IGameSnapshotCache _snapshotCache;
    private readonly IGameRevisionStore _revisionStore;
    private readonly IGameResponseBuilder _responseBuilder;
    private readonly IGameEventPublisher _eventPublisher;
    private readonly IUpstreamErrorNormalizer _errorNormalizer;

    public SessionService(
        IGameApiClient gameApiClient,
        IGameSnapshotCache snapshotCache,
        IGameRevisionStore revisionStore,
        IGameResponseBuilder responseBuilder,
        IGameEventPublisher eventPublisher,
        IUpstreamErrorNormalizer errorNormalizer)
    {
        _gameApiClient = gameApiClient;
        _snapshotCache = snapshotCache;
        _revisionStore = revisionStore;
        _responseBuilder = responseBuilder;
        _eventPublisher = eventPublisher;
        _errorNormalizer = errorNormalizer;
    }

    public async Task<AppOutcome> CreateSessionAsync(
        string userId,
        CreateSessionRequest request,
        CancellationToken cancellationToken = default)
    {
        var response = await _gameApiClient.CreateSessionAsync(userId, request, cancellationToken);
        if (!response.IsSuccess)
        {
            return response.ToPassthrough(_errorNormalizer);
        }

        var session = JsonSerializer.Deserialize<GameSession>(response.Body, RealtimeJson.Options);
        if (session is null)
        {
            return response.ToPassthrough(_errorNormalizer);
        }

        var resourceId = string.IsNullOrWhiteSpace(session.Id) ? string.Empty : $"sessions/{session.Id}";
        return AppOutcomes.Created(session, resourceId);
    }

    public async Task<AppOutcome> GetSessionAsync(
        string userId,
        string sessionCode,
        CancellationToken cancellationToken = default)
    {
        var response = await _gameApiClient.GetSessionAsync(userId, sessionCode, cancellationToken);
        return response.ToPassthrough(_errorNormalizer);
    }

    public async Task<AppOutcome> JoinSessionAsync(
        string userId,
        string sessionCode,
        JoinSessionRequest? request,
        CancellationToken cancellationToken = default)
    {
        var response = await _gameApiClient.JoinSessionAsync(userId, sessionCode, request, cancellationToken);
        return response.ToPassthrough(_errorNormalizer);
    }

    public async Task<AppOutcome> RemoveMemberAsync(
        string userId,
        string sessionCode,
        string memberUserId,
        CancellationToken cancellationToken = default)
    {
        var response = await _gameApiClient.RemoveSessionMemberAsync(userId, sessionCode, memberUserId, cancellationToken);
        if (response.IsNoContent)
        {
            return AppOutcomes.NoContent();
        }

        return response.ToPassthrough(_errorNormalizer);
    }

    public async Task<AppOutcome> StartGameAsync(
        string userId,
        string sessionCode,
        StartGameRequest request,
        CancellationToken cancellationToken = default)
    {
        var response = await _gameApiClient.StartSessionGameAsync(userId, sessionCode, request, cancellationToken);
        if (!response.IsSuccess)
        {
            return response.ToPassthrough(_errorNormalizer);
        }

        var game = JsonSerializer.Deserialize<Game>(response.Body, RealtimeJson.Options);
        if (game is null)
        {
            return response.ToPassthrough(_errorNormalizer);
        }

        if (game.Id is long gameId)
        {
            var revision = await _revisionStore.GetCurrentRevisionAsync(gameId, cancellationToken);
            GameSnapshotCacheSync.Seed(_snapshotCache, gameId, revision, response.Body);
            await _eventPublisher.PublishGameChangedAsync(
                gameId,
                userId,
                GameChangeActions.Start,
                response.Body,
                cancellationToken);
        }

        var enriched = await _responseBuilder.BuildAsync(game, userId, cancellationToken);
        var resourceId = game.Id?.ToString() ?? string.Empty;
        return AppOutcomes.Created(enriched, resourceId);
    }
}
