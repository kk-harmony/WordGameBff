using WordGameBff.Domain.Models;

namespace WordGameBff.Application.Games;

public interface IGameSelfVoteStore
{
    Task RecordSelfVoteAsync(
        long gameId,
        string voterUserId,
        string votedForUserId,
        CancellationToken cancellationToken = default);

    Task SyncFromUpstreamAsync(Game game, CancellationToken cancellationToken = default);

    Task<Game> ApplyViewerSelfVoteAsync(
        Game game,
        string viewerUserId,
        CancellationToken cancellationToken = default);
}
