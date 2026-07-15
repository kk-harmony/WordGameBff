using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WordGameBff.Application.Auth;
using WordGameBff.Application.Configuration;
using WordGameBff.Application.Games;
using WordGameBff.Domain.Models;

namespace WordGameBff.Infrastructure.Games;

public sealed class GameApiClient : IGameApiClient
{
    private const int ConflictRetryDelayMs = 100;
    private static readonly TimeSpan[] TransientRetryDelays =
    [
        TimeSpan.FromMilliseconds(250),
        TimeSpan.FromMilliseconds(750),
        TimeSpan.FromMilliseconds(1500),
        TimeSpan.FromSeconds(3),
    ];

    private readonly HttpClient _httpClient;
    private readonly ICustomAuthTokenService _tokenService;
    private readonly IIdempotencyKeyGenerator _idempotencyKeyGenerator;
    private readonly ILogger<GameApiClient> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public GameApiClient(
        HttpClient httpClient,
        IOptions<GameApiOptions> options,
        ICustomAuthTokenService tokenService,
        IIdempotencyKeyGenerator idempotencyKeyGenerator,
        ILogger<GameApiClient> logger)
    {
        _httpClient = httpClient;
        _tokenService = tokenService;
        _idempotencyKeyGenerator = idempotencyKeyGenerator;
        _logger = logger;
        _httpClient.BaseAddress = new Uri(options.Value.BaseUrl.TrimEnd('/') + "/");
    }

    public Task<GameApiResponse> CreateGameAsync(string userId, CreateGameRequest request, CancellationToken cancellationToken = default) =>
        SendAsync(userId, HttpMethod.Post, "games", request, cancellationToken);

    public Task<GameApiResponse> GetGameAsync(string userId, long gameId, CancellationToken cancellationToken = default) =>
        SendAsync(userId, HttpMethod.Get, $"games/{gameId}", null, cancellationToken);

    public Task<GameApiResponse> JoinGameAsync(string userId, long gameId, JoinGameRequest? request = null, CancellationToken cancellationToken = default)
    {
        var path = $"games/{gameId}/members";
        if (!string.IsNullOrWhiteSpace(request?.DisplayName))
        {
            path += $"?displayName={Uri.EscapeDataString(request.DisplayName.Trim())}";
        }

        return SendAsync(userId, HttpMethod.Post, path, null, cancellationToken);
    }

    public Task<GameApiResponse> RemoveGameMemberAsync(string userId, long gameId, string memberUserId, CancellationToken cancellationToken = default) =>
        SendAsync(userId, HttpMethod.Delete, $"games/{gameId}/members/{Uri.EscapeDataString(memberUserId)}", null, cancellationToken);

    public Task<GameApiResponse> StartGameAsync(string userId, long gameId, StartGameRequest request, CancellationToken cancellationToken = default) =>
        SendAsync(userId, HttpMethod.Post, $"games/{gameId}/start", request, cancellationToken);

    public Task<GameApiResponse> CompleteTurnAsync(string userId, long gameId, CancellationToken cancellationToken = default) =>
        SendIdempotentAsync(userId, HttpMethod.Post, $"games/{gameId}/turn/complete", null, cancellationToken);

    public Task<GameApiResponse> GetMyWordAsync(string userId, long gameId, CancellationToken cancellationToken = default) =>
        SendAsync(userId, HttpMethod.Get, $"games/{gameId}/my-word", null, cancellationToken);

    public Task<GameApiResponse> VoteAsync(string userId, long gameId, VoteRequest request, CancellationToken cancellationToken = default) =>
        SendIdempotentAsync(userId, HttpMethod.Post, $"games/{gameId}/vote", request, cancellationToken);

    public Task<GameApiResponse> GetRandomSecretWordAsync(string userId, CancellationToken cancellationToken = default) =>
        SendAsync(userId, HttpMethod.Get, "secretwords/random", null, cancellationToken);

    public Task<GameApiResponse> CreateSecretWordAsync(string userId, SecretWord request, CancellationToken cancellationToken = default) =>
        SendAsync(userId, HttpMethod.Post, "secretwords", request, cancellationToken);

    public Task<GameApiResponse> GetSecretWordAsync(string userId, long secretWordId, CancellationToken cancellationToken = default) =>
        SendAsync(userId, HttpMethod.Get, $"secretwords/{secretWordId}", null, cancellationToken);

    public async Task<Game?> GetGameModelAsync(string userId, long gameId, CancellationToken cancellationToken = default)
    {
        var response = await GetGameAsync(userId, gameId, cancellationToken);
        if (!response.IsSuccess)
        {
            return null;
        }

        return JsonSerializer.Deserialize<Game>(response.Body, JsonOptions);
    }

    private async Task<GameApiResponse> SendIdempotentAsync(
        string userId,
        HttpMethod method,
        string path,
        object? body,
        CancellationToken cancellationToken)
    {
        var idempotencyKey = _idempotencyKeyGenerator.CreateKey();
        var response = await SendAsync(userId, method, path, body, cancellationToken, idempotencyKey);
        if (response.StatusCode != 409)
        {
            return response;
        }

        _logger.LogDebug(
            "Retrying idempotent {Method} {Path} for user {UserId} after 409",
            method,
            path,
            userId);

        await Task.Delay(ConflictRetryDelayMs, cancellationToken);
        return await SendAsync(userId, method, path, body, cancellationToken, idempotencyKey);
    }

    private async Task<GameApiResponse> SendAsync(
        string userId,
        HttpMethod method,
        string path,
        object? body,
        CancellationToken cancellationToken,
        string? idempotencyKey = null)
    {
        var token = await _tokenService.GetServiceTokenAsync(cancellationToken);
        var serializedBody = body is null ? null : JsonSerializer.Serialize(body, JsonOptions);
        var canRetryTransientFailure =
            method == HttpMethod.Get || !string.IsNullOrWhiteSpace(idempotencyKey);

        for (var attempt = 0; ; attempt++)
        {
            try
            {
                var response = await SendOnceAsync(
                    userId,
                    method,
                    path,
                    serializedBody,
                    token,
                    idempotencyKey,
                    cancellationToken);

                if (!canRetryTransientFailure ||
                    !IsTransientStatusCode(response.StatusCode) ||
                    attempt >= TransientRetryDelays.Length)
                {
                    return response;
                }

                _logger.LogWarning(
                    "Game API {Method} {Path} returned {StatusCode}; retrying attempt {Attempt}",
                    method,
                    path,
                    response.StatusCode,
                    attempt + 2);
            }
            catch (HttpRequestException ex) when (
                canRetryTransientFailure && attempt < TransientRetryDelays.Length)
            {
                _logger.LogWarning(
                    ex,
                    "Game API {Method} {Path} failed; retrying attempt {Attempt}",
                    method,
                    path,
                    attempt + 2);
            }

            await Task.Delay(TransientRetryDelays[attempt], cancellationToken);
        }
    }

    private async Task<GameApiResponse> SendOnceAsync(
        string userId,
        HttpMethod method,
        string path,
        string? serializedBody,
        string token,
        string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add(GameApiHeaders.DelegatedUserId, userId);

        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            request.Headers.Add(GameApiHeaders.IdempotencyKey, idempotencyKey);
        }

        if (serializedBody is not null)
        {
            request.Content = new StringContent(serializedBody, Encoding.UTF8, "application/json");
        }

        _logger.LogDebug("Proxying {Method} {Path} for user {UserId}", method, path, userId);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        return new GameApiResponse
        {
            StatusCode = (int)response.StatusCode,
            Body = responseBody
        };
    }

    private static bool IsTransientStatusCode(int statusCode) =>
        statusCode is 408 or 500 or 502 or 503 or 504;
}
