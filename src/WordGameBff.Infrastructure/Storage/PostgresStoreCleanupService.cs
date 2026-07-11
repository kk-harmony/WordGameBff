using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace WordGameBff.Infrastructure.Storage;

public sealed class PostgresStoreCleanupService : BackgroundService
{
    private readonly PostgresKeyValueStore _store;
    private readonly ILogger<PostgresStoreCleanupService> _logger;

    public PostgresStoreCleanupService(PostgresKeyValueStore store, ILogger<PostgresStoreCleanupService> logger)
    {
        _store = store;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await _store.DeleteExpiredAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Failed to purge expired BFF store entries");
            }

            await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
        }
    }
}
