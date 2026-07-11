using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace WordGameBff.Infrastructure.Storage;

public sealed class PostgresSchemaInitializer : IHostedService
{
    private readonly PostgresStoreConnection _connection;
    private readonly ILogger<PostgresSchemaInitializer> _logger;

    public PostgresSchemaInitializer(PostgresStoreConnection connection, ILogger<PostgresSchemaInitializer> logger)
    {
        _connection = connection;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connection.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var sql = $"""
            CREATE SCHEMA IF NOT EXISTS {BffDbSchema.Name};

            CREATE TABLE IF NOT EXISTS {BffDbSchema.StoreTable} (
                namespace   TEXT        NOT NULL,
                key         TEXT        NOT NULL,
                value       JSONB       NOT NULL,
                expires_at  TIMESTAMPTZ,
                updated_at  TIMESTAMPTZ NOT NULL DEFAULT now(),
                PRIMARY KEY (namespace, key)
            );

            CREATE INDEX IF NOT EXISTS idx_bff_store_expires
                ON {BffDbSchema.StoreTable} (expires_at)
                WHERE expires_at IS NOT NULL;

            CREATE TABLE IF NOT EXISTS {BffDbSchema.GameRevisionsTable} (
                game_id   BIGINT PRIMARY KEY,
                revision  BIGINT NOT NULL DEFAULT 0
            );
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
        _logger.LogInformation("Postgres BFF schema '{Schema}' ensured", BffDbSchema.Name);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
