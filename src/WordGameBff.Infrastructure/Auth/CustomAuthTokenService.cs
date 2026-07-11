using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WordGameBff.Application.Auth;
using WordGameBff.Application.Configuration;

namespace WordGameBff.Infrastructure.Auth;

public sealed class CustomAuthTokenService : ICustomAuthTokenService
{
    private const string ServiceTokenCacheKey = "customauth:service";

    private readonly HttpClient _httpClient;
    private readonly CustomAuthOptions _options;
    private readonly IMemoryCache _cache;
    private readonly ILogger<CustomAuthTokenService> _logger;
    private readonly SemaphoreSlim _discoveryLock = new(1, 1);
    private string? _tokenEndpoint;

    public CustomAuthTokenService(
        HttpClient httpClient,
        IOptions<CustomAuthOptions> options,
        IMemoryCache cache,
        ILogger<CustomAuthTokenService> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _cache = cache;
        _logger = logger;
    }

    public async Task<string> GetServiceTokenAsync(CancellationToken cancellationToken = default)
    {
        if (_cache.TryGetValue(ServiceTokenCacheKey, out string? cachedToken) && !string.IsNullOrEmpty(cachedToken))
        {
            return cachedToken;
        }

        var tokenEndpoint = await GetTokenEndpointAsync(cancellationToken);
        var token = await RequestClientCredentialsTokenAsync(tokenEndpoint, cancellationToken);

        var expiresIn = token.ExpiresIn > 30 ? token.ExpiresIn - 30 : token.ExpiresIn;
        _cache.Set(ServiceTokenCacheKey, token.AccessToken, TimeSpan.FromSeconds(Math.Max(expiresIn, 1)));

        _logger.LogDebug("Issued CustomAuth service token");
        return token.AccessToken;
    }

    private async Task<string> GetTokenEndpointAsync(CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(_tokenEndpoint))
        {
            return _tokenEndpoint;
        }

        await _discoveryLock.WaitAsync(cancellationToken);
        try
        {
            if (!string.IsNullOrEmpty(_tokenEndpoint))
            {
                return _tokenEndpoint;
            }

            var authority = _options.Authority.TrimEnd('/');
            var discovery = await _httpClient.GetFromJsonAsync<OpenIdDiscovery>(
                $"{authority}/.well-known/openid-configuration",
                cancellationToken);

            _tokenEndpoint = discovery?.TokenEndpoint
                             ?? throw new InvalidOperationException("CustomAuth discovery document missing token_endpoint.");
            return _tokenEndpoint;
        }
        finally
        {
            _discoveryLock.Release();
        }
    }

    private async Task<TokenResponse> RequestClientCredentialsTokenAsync(string tokenEndpoint, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, tokenEndpoint)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = _options.ClientId,
                ["client_secret"] = _options.ClientSecret,
                ["scope"] = "api"
            })
        };

        var response = await _httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "CustomAuth client_credentials failed with status {StatusCode}: {Body}",
                (int)response.StatusCode,
                body);
            throw new UpstreamAuthException($"CustomAuth client_credentials failed: {(int)response.StatusCode}");
        }

        return JsonSerializer.Deserialize<TokenResponse>(body, TokenJson.Options)
               ?? throw new InvalidOperationException("CustomAuth client_credentials returned empty token.");
    }

    private sealed class OpenIdDiscovery
    {
        [JsonPropertyName("token_endpoint")]
        public string? TokenEndpoint { get; init; }
    }

    private sealed class TokenResponse
    {
        [JsonPropertyName("access_token")]
        public string AccessToken { get; init; } = string.Empty;

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; init; }
    }

    private static class TokenJson
    {
        public static readonly JsonSerializerOptions Options = new()
        {
            PropertyNameCaseInsensitive = true
        };
    }
}
