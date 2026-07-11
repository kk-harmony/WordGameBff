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

    private sealed class CapturingHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            handler(request, cancellationToken);
    }
}
