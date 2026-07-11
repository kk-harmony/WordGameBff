using Npgsql;
using WordGameBff.Application.Realtime;

namespace WordGameBff.Infrastructure.Storage;

public sealed class PostgresGameRevisionStore : IGameRevisionStore
{
    private readonly PostgresStoreConnection _connection;

    public PostgresGameRevisionStore(PostgresStoreConnection connection)
    {
        _connection = connection;
    }

    public async Task<long> GetCurrentRevisionAsync(long gameId, CancellationToken cancellationToken = default)
    {
        await using var dbConnection = new NpgsqlConnection(_connection.ConnectionString);
        await dbConnection.OpenAsync(cancellationToken);

        var sql = $"SELECT revision FROM {BffDbSchema.GameRevisionsTable} WHERE game_id = @gameId";
        await using var command = new NpgsqlCommand(sql, dbConnection);
        command.Parameters.AddWithValue("gameId", gameId);

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is null or DBNull ? 0 : Convert.ToInt64(result);
    }

    public async Task<long> GetNextRevisionAsync(long gameId, CancellationToken cancellationToken = default)
    {
        await using var dbConnection = new NpgsqlConnection(_connection.ConnectionString);
        await dbConnection.OpenAsync(cancellationToken);

        var sql = $"""
            INSERT INTO {BffDbSchema.GameRevisionsTable} (game_id, revision)
            VALUES (@gameId, 1)
            ON CONFLICT (game_id)
            DO UPDATE SET revision = {BffDbSchema.GameRevisionsTable}.revision + 1
            RETURNING revision
            """;

        await using var command = new NpgsqlCommand(sql, dbConnection);
        command.Parameters.AddWithValue("gameId", gameId);

        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
    }
}
