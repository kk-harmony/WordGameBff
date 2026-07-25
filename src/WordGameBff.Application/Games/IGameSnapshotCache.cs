namespace WordGameBff.Application.Games;

public sealed class CachedGameSnapshot
{
    public required string RawJson { get; init; }
    public required long Revision { get; init; }
}

public interface IGameSnapshotCache
{
    bool TryGet(long gameId, out CachedGameSnapshot? snapshot);
    void Set(long gameId, string rawJson, long revision);
    void Invalidate(long gameId);
}
