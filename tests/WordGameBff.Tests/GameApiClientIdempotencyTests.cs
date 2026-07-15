using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;
using WordGameBff.Application.Auth;
using WordGameBff.Application.Games;
using WordGameBff.Infrastructure.Games;

namespace WordGameBff.Tests;

public class GameApiClientIdempotencyTests
{
    [Fact]
    public async Task GetGameAsync_RetriesTransientFailure()
    {
        var requestCount = 0;
        var handler = new CapturingHandler((_, _) =>
        {
            requestCount++;
            return Task.FromResult(new HttpResponseMessage(
                requestCount == 1 ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK)
            {
                Content = new StringContent("""{"id":9}""", Encoding.UTF8, "application/json")
            });
        });

        var client = CreateClient(handler);

        var response = await client.GetGameAsync("user-1", 9);

        Assert.True(response.IsSuccess);
        Assert.Equal(2, requestCount);
    }

    [Fact]
    public async Task VoteAsync_SendsIdempotencyKeyHeader()
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
        idempotencyKeyGenerator.Setup(x => x.CreateKey()).Returns("vote-key-123");

        var client = new GameApiClient(
            httpClient,
            Microsoft.Extensions.Options.Options.Create(new Application.Configuration.GameApiOptions
            {
                BaseUrl = "http://wordgames.test"
            }),
            tokenService.Object,
            idempotencyKeyGenerator.Object,
            Mock.Of<Microsoft.Extensions.Logging.ILogger<GameApiClient>>());

        await client.VoteAsync("user-1", 9, new Domain.Models.VoteRequest { VotedUserId = "user-2" });

        Assert.NotNull(captured);
        Assert.Equal("vote-key-123", captured!.Headers.GetValues(GameApiHeaders.IdempotencyKey).Single());
    }

    [Fact]
    public async Task VoteAsync_RetriesTransientFailureWithSameIdempotencyKey()
    {
        var idempotencyKeys = new List<string>();
        var handler = new CapturingHandler((request, _) =>
        {
            idempotencyKeys.Add(request.Headers.GetValues(GameApiHeaders.IdempotencyKey).Single());
            return Task.FromResult(new HttpResponseMessage(
                idempotencyKeys.Count == 1
                    ? HttpStatusCode.InternalServerError
                    : HttpStatusCode.OK));
        });
        var tokenService = new Mock<ICustomAuthTokenService>();
        tokenService.Setup(x => x.GetServiceTokenAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("service-token");
        var idempotencyKeyGenerator = new Mock<IIdempotencyKeyGenerator>();
        idempotencyKeyGenerator.Setup(x => x.CreateKey()).Returns("stable-key");
        var client = new GameApiClient(
            new HttpClient(handler),
            Microsoft.Extensions.Options.Options.Create(new Application.Configuration.GameApiOptions
            {
                BaseUrl = "http://wordgames.test"
            }),
            tokenService.Object,
            idempotencyKeyGenerator.Object,
            Mock.Of<Microsoft.Extensions.Logging.ILogger<GameApiClient>>());

        var response = await client.VoteAsync(
            "user-1",
            9,
            new Domain.Models.VoteRequest { VotedUserId = "user-2" });

        Assert.True(response.IsSuccess);
        Assert.Equal(["stable-key", "stable-key"], idempotencyKeys);
    }

    private static GameApiClient CreateClient(HttpMessageHandler handler)
    {
        var tokenService = new Mock<ICustomAuthTokenService>();
        tokenService.Setup(x => x.GetServiceTokenAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("service-token");

        return new GameApiClient(
            new HttpClient(handler),
            Microsoft.Extensions.Options.Options.Create(new Application.Configuration.GameApiOptions
            {
                BaseUrl = "http://wordgames.test"
            }),
            tokenService.Object,
            Mock.Of<IIdempotencyKeyGenerator>(),
            Mock.Of<Microsoft.Extensions.Logging.ILogger<GameApiClient>>());
    }

    private sealed class CapturingHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            handler(request, cancellationToken);
    }
}
