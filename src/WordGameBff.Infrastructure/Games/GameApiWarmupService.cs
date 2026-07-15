using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WordGameBff.Application.Configuration;

namespace WordGameBff.Infrastructure.Games;

public sealed class GameApiWarmupService : BackgroundService
{
    private static readonly TimeSpan AttemptTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan[] RetryDelays =
    [
        TimeSpan.Zero,
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(4),
        TimeSpan.FromSeconds(8),
        TimeSpan.FromSeconds(15),
    ];

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly GameApiOptions _options;
    private readonly ILogger<GameApiWarmupService> _logger;

    public GameApiWarmupService(
        IHttpClientFactory httpClientFactory,
        IOptions<GameApiOptions> options,
        ILogger<GameApiWarmupService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var warmupUri = new Uri(
            $"{_options.BaseUrl.TrimEnd('/')}/{_options.WarmupPath.TrimStart('/')}");
        var client = _httpClientFactory.CreateClient();

        for (var attempt = 0; attempt < RetryDelays.Length; attempt++)
        {
            if (RetryDelays[attempt] > TimeSpan.Zero)
            {
                await Task.Delay(RetryDelays[attempt], stoppingToken);
            }

            try
            {
                using var attemptTimeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                attemptTimeout.CancelAfter(AttemptTimeout);
                using var response = await client.GetAsync(warmupUri, attemptTimeout.Token);
                if ((int)response.StatusCode < 500)
                {
                    _logger.LogInformation(
                        "Game API warmup reached {WarmupUri} with status {StatusCode}",
                        warmupUri,
                        (int)response.StatusCode);
                    return;
                }

                _logger.LogWarning(
                    "Game API warmup attempt {Attempt} returned status {StatusCode}",
                    attempt + 1,
                    (int)response.StatusCode);
            }
            catch (HttpRequestException ex)
            {
                _logger.LogWarning(
                    ex,
                    "Game API warmup attempt {Attempt} could not reach {WarmupUri}",
                    attempt + 1,
                    warmupUri);
            }
            catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogWarning(
                    "Game API warmup attempt {Attempt} timed out after {TimeoutSeconds} seconds",
                    attempt + 1,
                    AttemptTimeout.TotalSeconds);
            }
        }

        _logger.LogError(
            "Game API did not become reachable after {AttemptCount} warmup attempts",
            RetryDelays.Length);
    }
}
