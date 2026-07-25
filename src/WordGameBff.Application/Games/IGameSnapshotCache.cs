namespace WordGameBff.Application.Games;

public sealed class CachedGameSnapshot
{
    public required string RawJson { get; init; }
    public required long Revision { get; init; }
}

/// <summary>
/// Revision-ordered snapshot store: writes and evictions both carry the revision they
/// describe so late-arriving older state can never replace newer state.
/// </summary>
public interface IGameSnapshotCache
{
    bool TryGet(long gameId, out CachedGameSnapshot? snapshot);

    /// <summary>Stores the body unless a newer revision is already known.</summary>
    void Set(long gameId, string rawJson, long revision);

    /// <summary>
    /// Drops the cached body only when it predates <paramref name="revision"/>, and blocks
    /// later writes older than it. Keeping an entry that already reflects this revision
    /// avoids a redundant upstream fetch.
    /// </summary>
    void InvalidateOlderThan(long gameId, long revision);
}
