namespace WordGameBff.Infrastructure.Storage;

/// <summary>
/// Dedicated Postgres schema for all BFF shared state (KV store + revisions).
/// </summary>
internal static class BffDbSchema
{
    public const string Name = "bff";
    public const string StoreTable = "bff.store";
    public const string GameRevisionsTable = "bff.game_revisions";
}
