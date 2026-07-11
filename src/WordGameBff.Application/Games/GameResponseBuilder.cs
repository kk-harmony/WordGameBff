using WordGameBff.Domain.Models;

namespace WordGameBff.Application.Games;

public interface IGameResponseBuilder
{
    Task<Game> BuildAsync(Game upstreamGame, string viewerUserId, CancellationToken cancellationToken = default);
}

public sealed class GameResponseBuilder : IGameResponseBuilder
{
    private readonly IGameSanitizer _sanitizer;
    private readonly IGamePresenceEnricher _presenceEnricher;
    private readonly IGameSelfVoteStore _selfVoteStore;

    public GameResponseBuilder(
        IGameSanitizer sanitizer,
        IGamePresenceEnricher presenceEnricher,
        IGameSelfVoteStore selfVoteStore)
    {
        _sanitizer = sanitizer;
        _presenceEnricher = presenceEnricher;
        _selfVoteStore = selfVoteStore;
    }

    public async Task<Game> BuildAsync(
        Game upstreamGame,
        string viewerUserId,
        CancellationToken cancellationToken = default)
    {
        await _selfVoteStore.SyncFromUpstreamAsync(upstreamGame, cancellationToken);
        var sanitized = _sanitizer.Sanitize(upstreamGame, viewerUserId);
        var enriched = await _presenceEnricher.EnrichAsync(sanitized, cancellationToken);
        return await _selfVoteStore.ApplyViewerSelfVoteAsync(enriched, viewerUserId, cancellationToken);
    }
}
