using System.Text.Json;
using Npgsql;

namespace WordGameBff.Infrastructure.Storage;

public sealed class PostgresKeyValueStore
{
    private readonly string _connectionString;

    public PostgresKeyValueStore(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task<T?> GetAsync<T>(string ns, string key, CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var sql = $"""
            SELECT value
            FROM {BffDbSchema.StoreTable}
            WHERE namespace = @namespace
              AND key = @key
              AND (expires_at IS NULL OR expires_at > now())
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("namespace", ns);
        command.Parameters.AddWithValue("key", key);

        var result = await command.ExecuteScalarAsync(cancellationToken);
        if (result is null or DBNull)
        {
            return default;
        }

        return JsonSerializer.Deserialize<T>((string)result);
    }

    public async Task SetAsync<T>(
        string ns,
        string key,
        T value,
        DateTimeOffset? expiresAt = null,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var sql = $"""
            INSERT INTO {BffDbSchema.StoreTable} (namespace, key, value, expires_at, updated_at)
            VALUES (@namespace, @key, @value::jsonb, @expires_at, now())
            ON CONFLICT (namespace, key)
            DO UPDATE SET value = EXCLUDED.value,
                          expires_at = EXCLUDED.expires_at,
                          updated_at = now()
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("namespace", ns);
        command.Parameters.AddWithValue("key", key);
        command.Parameters.AddWithValue("value", JsonSerializer.Serialize(value));
        command.Parameters.AddWithValue("expires_at", expiresAt.HasValue ? expiresAt.Value.UtcDateTime : DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<bool> TryDeleteAsync(string ns, string key, CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var sql = $"""
            DELETE FROM {BffDbSchema.StoreTable}
            WHERE namespace = @namespace
              AND key = @key
              AND (expires_at IS NULL OR expires_at > now())
            RETURNING key
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("namespace", ns);
        command.Parameters.AddWithValue("key", key);

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is not null and not DBNull;
    }

    public async Task<bool> ExistsAsync(string ns, string key, CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var sql = $"""
            SELECT 1
            FROM {BffDbSchema.StoreTable}
            WHERE namespace = @namespace
              AND key = @key
              AND (expires_at IS NULL OR expires_at > now())
            LIMIT 1
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("namespace", ns);
        command.Parameters.AddWithValue("key", key);

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is not null and not DBNull;
    }

    public async Task<int> CountByNamespaceAndJsonFieldAsync(
        string ns,
        string jsonField,
        string fieldValue,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var sql = $"""
            SELECT COUNT(*)
            FROM {BffDbSchema.StoreTable}
            WHERE namespace = @namespace
              AND (expires_at IS NULL OR expires_at > now())
              AND value->>'{jsonField}' = @fieldValue
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("namespace", ns);
        command.Parameters.AddWithValue("fieldValue", fieldValue);

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(result);
    }

    public async Task<bool> ExistsByNamespaceAndJsonFieldsAsync(
        string ns,
        IReadOnlyDictionary<string, string> fields,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var conditions = fields.Select((pair, index) => $"value->>'{pair.Key}' = @f{index}");
        var sql = $"""
            SELECT 1
            FROM {BffDbSchema.StoreTable}
            WHERE namespace = @namespace
              AND (expires_at IS NULL OR expires_at > now())
              AND {string.Join(" AND ", conditions)}
            LIMIT 1
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("namespace", ns);
        var i = 0;
        foreach (var pair in fields)
        {
            command.Parameters.AddWithValue($"f{i++}", pair.Value);
        }

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is not null and not DBNull;
    }

    public async Task DeleteExpiredAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var sql = $"DELETE FROM {BffDbSchema.StoreTable} WHERE expires_at IS NOT NULL AND expires_at <= now()";
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
