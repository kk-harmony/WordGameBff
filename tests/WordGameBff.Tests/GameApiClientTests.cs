using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using WordGameBff.Application.Auth;
using WordGameBff.Application.Configuration;
using WordGameBff.Application.Games;
using WordGameBff.Domain.Models;
using WordGameBff.Infrastructure.Games;

namespace WordGameBff.Tests;

public class GameApiClientTests
{
    [Fact]
    public async Task JoinGameAsync_WithDisplayName_AppendsQueryParameter()
    {
        HttpRequestMessage? captured = null;
        var handler = new CapturingHandler((request, _) =>
        {
            captured = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"id":1}""", Encoding.UTF8, "application/json")
            });
        });

        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://wordgames.test/")
        };

        var tokenService = new Mock<ICustomAuthTokenService>();
        tokenService.Setup(x => x.GetServiceTokenAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("service-token");

        var idempotencyKeyGenerator = new Mock<IIdempotencyKeyGenerator>();
        idempotencyKeyGenerator.Setup(x => x.CreateKey()).Returns("test-idempotency-key");

        var client = new GameApiClient(
            httpClient,
            Options.Create(new GameApiOptions { BaseUrl = "http://wordgames.test" }),
            tokenService.Object,
            idempotencyKeyGenerator.Object,
            Mock.Of<ILogger<GameApiClient>>());

        await client.JoinGameAsync("delegated-user", 5, new JoinGameRequest { DisplayName = "Alex" });

        Assert.NotNull(captured);
        Assert.Equal(HttpMethod.Post, captured!.Method);
        Assert.Equal("/games/5/members?displayName=Alex", captured.RequestUri!.PathAndQuery);
        Assert.Equal("delegated-user", captured.Headers.GetValues(GameApiHeaders.DelegatedUserId).Single());
        Assert.Null(captured.Content);
    }

    [Fact]
    public async Task JoinGameAsync_WithoutDisplayName_UsesPlainMembersPath()
    {
        HttpRequestMessage? captured = null;
        var handler = new CapturingHandler((request, _) =>
        {
            captured = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"id":1}""", Encoding.UTF8, "application/json")
            });
        });

        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://wordgames.test/")
        };

        var tokenService = new Mock<ICustomAuthTokenService>();
        tokenService.Setup(x => x.GetServiceTokenAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("service-token");

        var idempotencyKeyGenerator = new Mock<IIdempotencyKeyGenerator>();
        idempotencyKeyGenerator.Setup(x => x.CreateKey()).Returns("test-idempotency-key");

        var client = new GameApiClient(
            httpClient,
            Options.Create(new GameApiOptions { BaseUrl = "http://wordgames.test" }),
            tokenService.Object,
            idempotencyKeyGenerator.Object,
            Mock.Of<ILogger<GameApiClient>>());

        await client.JoinGameAsync("delegated-user", 5);

        Assert.NotNull(captured);
        Assert.Equal("/games/5/members", captured!.RequestUri!.PathAndQuery);
    }

    [Fact]
    public async Task CreateSessionAsync_PostsToSessions()
    {
        HttpRequestMessage? captured = null;
        var handler = new CapturingHandler((request, _) =>
        {
            captured = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Created)
            {
                Content = new StringContent("""{"id":9,"name":"Lobby","adminUserId":"admin"}""", Encoding.UTF8, "application/json")
            });
        });

        var client = CreateClient(handler);
        await client.CreateSessionAsync("admin", new CreateSessionRequest { Name = "Lobby", DisplayName = "Host" });

        Assert.NotNull(captured);
        Assert.Equal(HttpMethod.Post, captured!.Method);
        Assert.Equal("/sessions", captured.RequestUri!.PathAndQuery);
        Assert.Equal("admin", captured.Headers.GetValues(GameApiHeaders.DelegatedUserId).Single());
    }

    [Fact]
    public async Task JoinSessionAsync_WithDisplayName_AppendsQueryParameter()
    {
        HttpRequestMessage? captured = null;
        var handler = new CapturingHandler((request, _) =>
        {
            captured = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Created)
            {
                Content = new StringContent("""{"id":9}""", Encoding.UTF8, "application/json")
            });
        });

        var client = CreateClient(handler);
        await client.JoinSessionAsync("player", "K7M2Q", new JoinSessionRequest { DisplayName = "Alex" });

        Assert.NotNull(captured);
        Assert.Equal("/sessions/K7M2Q/members?displayName=Alex", captured!.RequestUri!.PathAndQuery);
    }

    [Fact]
    public async Task StartSessionGameAsync_PostsSecretWordId()
    {
        HttpRequestMessage? captured = null;
        string? capturedBody = null;
        var handler = new CapturingHandler(async (request, _) =>
        {
            captured = request;
            capturedBody = request.Content is null ? null : await request.Content.ReadAsStringAsync();
            return new HttpResponseMessage(HttpStatusCode.Created)
            {
                Content = new StringContent("""{"id":42,"status":"IN_PROGRESS"}""", Encoding.UTF8, "application/json")
            };
        });

        var client = CreateClient(handler);
        await client.StartSessionGameAsync("admin", "K7M2Q", new StartGameRequest { SecretWordId = 3 });

        Assert.NotNull(captured);
        Assert.Equal(HttpMethod.Post, captured!.Method);
        Assert.Equal("/sessions/K7M2Q/games", captured.RequestUri!.PathAndQuery);
        Assert.Contains("\"secretWordId\":3", capturedBody);
    }

    [Fact]
    public async Task GetSessionAsync_EncodesJoinCodeInPath()
    {
        HttpRequestMessage? captured = null;
        var handler = new CapturingHandler((request, _) =>
        {
            captured = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"id":"K7M2Q"}""", Encoding.UTF8, "application/json")
            });
        });

        var client = CreateClient(handler);
        await client.GetSessionAsync("admin", "K7M2Q");

        Assert.NotNull(captured);
        Assert.Equal(HttpMethod.Get, captured!.Method);
        Assert.Equal("/sessions/K7M2Q", captured.RequestUri!.PathAndQuery);
    }

    private static GameApiClient CreateClient(HttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://wordgames.test/")
        };

        var tokenService = new Mock<ICustomAuthTokenService>();
        tokenService.Setup(x => x.GetServiceTokenAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("service-token");

        var idempotencyKeyGenerator = new Mock<IIdempotencyKeyGenerator>();
        idempotencyKeyGenerator.Setup(x => x.CreateKey()).Returns("test-idempotency-key");

        return new GameApiClient(
            httpClient,
            Options.Create(new GameApiOptions { BaseUrl = "http://wordgames.test" }),
            tokenService.Object,
            idempotencyKeyGenerator.Object,
            Mock.Of<ILogger<GameApiClient>>());
    }

    private sealed class CapturingHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            handler(request, cancellationToken);
    }
}
