namespace WordGameBff.Infrastructure.Storage;

public sealed class PostgresStoreConnection
{
    public PostgresStoreConnection(string connectionString)
    {
        ConnectionString = connectionString;
    }

    public string ConnectionString { get; }
}
