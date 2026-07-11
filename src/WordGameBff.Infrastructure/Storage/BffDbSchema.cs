namespace WordGameBff.Infrastructure.Storage;

/// <summary>
/// Dedicated Postgres schema for all BFF shared state (KV store + revisions).
/// SignalR NOTIFY/LISTEN remains on the database connection; tables live under this schema.
/// </summary>
internal static class BffDbSchema
{
    public const string Name = "bff";
    public const string StoreTable = "bff.store";
    public const string GameRevisionsTable = "bff.game_revisions";
}
