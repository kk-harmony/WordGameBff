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

    private sealed class CapturingHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            handler(request, cancellationToken);
    }
}
