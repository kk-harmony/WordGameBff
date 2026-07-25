using System.Text.Json;
using WordGameBff.Application.Realtime;
using WordGameBff.Domain.Models;

namespace WordGameBff.Application.Games;

public interface IGameCommandService
{
    Task<AppOutcome> CreateGameAsync(string userId, CreateGameRequest request, CancellationToken cancellationToken = default);
    Task<AppOutcome> JoinGameAsync(string userId, long gameId, JoinGameRequest? request, CancellationToken cancellationToken = default);
    Task<AppOutcome> RemoveMemberAsync(string userId, long gameId, string memberUserId, CancellationToken cancellationToken = default);
    Task<AppOutcome> StartRoundAsync(string userId, long gameId, StartGameRequest request, CancellationToken cancellationToken = default);
    Task<AppOutcome> CompleteTurnAsync(string userId, long gameId, CancellationToken cancellationToken = default);
    Task<AppOutcome> VoteAsync(string userId, long gameId, VoteRequest request, CancellationToken cancellationToken = default);
}

public sealed class GameCommandService : IGameCommandService
{
    private readonly IGameApiClient _gameApiClient;
    private readonly IGameSnapshotReader _snapshotReader;
    private readonly IGameSnapshotCache _snapshotCache;
    private readonly IGameRevisionStore _revisionStore;
    private readonly IGameResponseBuilder _responseBuilder;
    private readonly IGameEventPublisher _eventPublisher;
    private readonly IGameConnectionRegistry _connectionRegistry;
    private readonly IGameSelfVoteStore _selfVoteStore;
    private readonly IUpstreamErrorNormalizer _errorNormalizer;

    public GameCommandService(
        IGameApiClient gameApiClient,
        IGameSnapshotReader snapshotReader,
        IGameSnapshotCache snapshotCache,
        IGameRevisionStore revisionStore,
        IGameResponseBuilder responseBuilder,
        IGameEventPublisher eventPublisher,
        IGameConnectionRegistry connectionRegistry,
        IGameSelfVoteStore selfVoteStore,
        IUpstreamErrorNormalizer errorNormalizer)
    {
        _gameApiClient = gameApiClient;
        _snapshotReader = snapshotReader;
        _snapshotCache = snapshotCache;
        _revisionStore = revisionStore;
        _responseBuilder = responseBuilder;
        _eventPublisher = eventPublisher;
        _connectionRegistry = connectionRegistry;
        _selfVoteStore = selfVoteStore;
        _errorNormalizer = errorNormalizer;
    }

    public async Task<AppOutcome> CreateGameAsync(
        string userId,
        CreateGameRequest request,
        CancellationToken cancellationToken = default)
    {
        var response = await _gameApiClient.CreateGameAsync(userId, request, cancellationToken);
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
        }

        var enriched = await _responseBuilder.BuildAsync(game, userId, cancellationToken);
        var resourceId = game.Id?.ToString() ?? string.Empty;
        return AppOutcomes.Created(enriched, resourceId);
    }

    public async Task<AppOutcome> JoinGameAsync(
        string userId,
        long gameId,
        JoinGameRequest? request,
        CancellationToken cancellationToken = default)
    {
        var response = await _gameApiClient.JoinGameAsync(userId, gameId, request, cancellationToken);
        return await FromUpstreamGameMutationAsync(
            response,
            gameId,
            userId,
            GameChangeActions.Join,
            cancellationToken);
    }

    public async Task<AppOutcome> RemoveMemberAsync(
        string userId,
        long gameId,
        string memberUserId,
        CancellationToken cancellationToken = default)
    {
        if (string.Equals(memberUserId, userId, StringComparison.Ordinal))
        {
            var leaveResponse = await _gameApiClient.RemoveGameMemberAsync(userId, gameId, memberUserId, cancellationToken);
            if (leaveResponse.IsSuccess)
            {
                await _eventPublisher.PublishGameChangedAsync(
                    gameId,
                    userId,
                    GameChangeActions.Leave,
                    cancellationToken: cancellationToken);
            }

            return leaveResponse.IsNoContent
                ? AppOutcomes.NoContent()
                : leaveResponse.ToPassthrough(_errorNormalizer);
        }

        var currentGame = await _snapshotReader.GetGameModelAsync(userId, gameId, cancellationToken);
        if (currentGame is null)
        {
            return AppOutcomes.NotFound("NOT_FOUND", "Game not found.");
        }

        var removalCheck = await GameMemberRemovalGuard.ValidateAdminRemovalAsync(
            currentGame,
            userId,
            memberUserId,
            _connectionRegistry,
            cancellationToken);
        if (!removalCheck.Allowed)
        {
            return AppOutcomes.Fail(
                removalCheck.ErrorCode!,
                removalCheck.Message!,
                AppOutcomes.FailureKindFromErrorCode(removalCheck.ErrorCode!));
        }

        var response = await _gameApiClient.RemoveGameMemberAsync(userId, gameId, memberUserId, cancellationToken);
        if (!response.IsSuccess && GameMemberRemovalGuard.IsLegacyInsufficientMembersError(response.Body))
        {
            return AppOutcomes.BadRequest("INSUFFICIENT_MEMBERS", GameRules.InsufficientMembersForRemovalMessage);
        }

        return await FromUpstreamGameMutationAsync(
            response,
            gameId,
            userId,
            GameChangeActions.MemberRemoved,
            cancellationToken);
    }

    public async Task<AppOutcome> StartRoundAsync(
        string userId,
        long gameId,
        StartGameRequest request,
        CancellationToken cancellationToken = default)
    {
        var response = await _gameApiClient.StartGameAsync(userId, gameId, request, cancellationToken);
        return await FromUpstreamGameMutationAsync(
            response,
            gameId,
            userId,
            GameChangeActions.Start,
            cancellationToken);
    }

    public async Task<AppOutcome> CompleteTurnAsync(
        string userId,
        long gameId,
        CancellationToken cancellationToken = default)
    {
        var response = await _gameApiClient.CompleteTurnAsync(userId, gameId, cancellationToken);
        return await FromUpstreamGameMutationAsync(
            response,
            gameId,
            userId,
            GameChangeActions.TurnComplete,
            cancellationToken);
    }

    public async Task<AppOutcome> VoteAsync(
        string userId,
        long gameId,
        VoteRequest request,
        CancellationToken cancellationToken = default)
    {
        var response = await _gameApiClient.VoteAsync(userId, gameId, request, cancellationToken);
        if (response.IsSuccess)
        {
            await _selfVoteStore.RecordSelfVoteAsync(gameId, userId, request.VotedUserId, cancellationToken);
        }

        return await FromUpstreamGameMutationAsync(
            response,
            gameId,
            userId,
            GameChangeActions.Vote,
            cancellationToken);
    }

    private async Task<AppOutcome> FromUpstreamGameMutationAsync(
        GameApiResponse response,
        long gameId,
        string userId,
        string action,
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
        await _eventPublisher.PublishGameChangedAsync(
            gameId,
            userId,
            action,
            response.Body,
            cancellationToken);
        return AppOutcomes.Ok(enriched);
    }
}
