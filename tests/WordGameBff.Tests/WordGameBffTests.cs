using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;
using WordGameBff.Application.Auth;

namespace WordGameBff.Tests;

public class HealthEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public HealthEndpointTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Health_ReturnsOk()
    {
        var response = await _client.GetAsync("/health");
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("healthy", body.GetProperty("status").GetString());
    }
}

public class PowChallengeTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public PowChallengeTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Verify_ValidNonce_ReturnsSessionToken()
    {
        var client = _factory.CreateClient();
        var challenge = await client.GetFromJsonAsync<JsonElement>("/auth/challenge");
        var prefix = challenge.GetProperty("prefix").GetString()!;
        var challengeId = challenge.GetProperty("challengeId").GetString()!;
        var difficulty = challenge.GetProperty("difficulty").GetInt32();
        var nonce = SolvePow(prefix, difficulty);

        var response = await client.PostAsJsonAsync("/auth/verify", new { challengeId, nonce });
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(string.IsNullOrWhiteSpace(result.GetProperty("sessionToken").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(result.GetProperty("userId").GetString()));
    }

    [Fact]
    public void ResolveSessionUserId_ValidGuid_ReusesIdentity()
    {
        var requested = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee";
        Assert.Equal(requested, PowChallengeService.ResolveSessionUserId(requested));
    }

    [Fact]
    public void ResolveSessionUserId_InvalidOrMissing_MintsNewGuid()
    {
        Assert.True(Guid.TryParse(PowChallengeService.ResolveSessionUserId(null), out _));
        Assert.True(Guid.TryParse(PowChallengeService.ResolveSessionUserId(""), out _));
        Assert.True(Guid.TryParse(PowChallengeService.ResolveSessionUserId("not-a-guid"), out _));
        Assert.NotEqual(
            PowChallengeService.ResolveSessionUserId("not-a-guid"),
            PowChallengeService.ResolveSessionUserId("also-bad"));
    }

    [Fact]
    public async Task Verify_WithExistingUserId_ReturnsSameUserId()
    {
        var client = _factory.CreateClient();
        var challenge = await client.GetFromJsonAsync<JsonElement>("/auth/challenge");
        var prefix = challenge.GetProperty("prefix").GetString()!;
        var challengeId = challenge.GetProperty("challengeId").GetString()!;
        var difficulty = challenge.GetProperty("difficulty").GetInt32();
        var nonce = SolvePow(prefix, difficulty);
        var userId = "11111111-2222-3333-4444-555555555555";

        var response = await client.PostAsJsonAsync("/auth/verify", new { challengeId, nonce, userId });
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(userId, result.GetProperty("userId").GetString());
    }

    [Fact]
    public async Task Verify_InvalidNonce_Returns400()
    {
        var client = _factory.CreateClient();
        var challenge = await client.GetFromJsonAsync<JsonElement>("/auth/challenge");
        var prefix = challenge.GetProperty("prefix").GetString()!;
        var difficulty = challenge.GetProperty("difficulty").GetInt32();

        // Pick a nonce that definitely fails for this prefix (random strings can pass at low difficulty).
        string nonce = "0";
        for (var i = 0; i < 1_000_000; i++)
        {
            if (!PowChallengeService.VerifyProof(prefix, i.ToString(), difficulty))
            {
                nonce = i.ToString();
                break;
            }
        }

        var response = await client.PostAsJsonAsync("/auth/verify", new
        {
            challengeId = challenge.GetProperty("challengeId").GetString(),
            nonce
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Verify_ReusedChallenge_Returns400()
    {
        var client = _factory.CreateClient();
        var challenge = await client.GetFromJsonAsync<JsonElement>("/auth/challenge");
        var prefix = challenge.GetProperty("prefix").GetString()!;
        var challengeId = challenge.GetProperty("challengeId").GetString()!;
        var nonce = SolvePow(prefix, challenge.GetProperty("difficulty").GetInt32());

        var first = await client.PostAsJsonAsync("/auth/verify", new { challengeId, nonce });
        first.EnsureSuccessStatusCode();

        var second = await client.PostAsJsonAsync("/auth/verify", new { challengeId, nonce = "0000" });
        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
    }

    [Fact]
    public async Task Verify_ExpiredChallenge_Returns400()
    {
        using var scope = _factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IChallengeStore>();
        var challengeId = Guid.NewGuid().ToString("N");
        await store.StoreAsync(new Domain.Models.PowChallenge
        {
            ChallengeId = challengeId,
            Prefix = "abc",
            Difficulty = 1,
            ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(-1)
        });

        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/auth/verify", new { challengeId, nonce = "1" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AuthEndpoints_ExceedRateLimit_Returns429WithRetryAfter()
    {
        await using var factory = new WebApplicationFactory<Program>();
        var client = factory.CreateClient();
        HttpResponseMessage? last = null;

        for (var i = 0; i < 35; i++)
        {
            last = await client.GetAsync("/auth/challenge");
        }

        Assert.NotNull(last);
        Assert.Equal(HttpStatusCode.TooManyRequests, last.StatusCode);
        Assert.True(last.Headers.Contains("Retry-After"));
    }

    private static string SolvePow(string prefix, int difficultyBits)
    {
        for (var i = 0; i < 1_000_000; i++)
        {
            var nonce = i.ToString();
            if (PowChallengeService.VerifyProof(prefix, nonce, difficultyBits))
            {
                return nonce;
            }
        }

        throw new InvalidOperationException("Unable to solve PoW in test range.");
    }
}

public class SessionAuthTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public SessionAuthTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task ApiWithoutToken_Returns401()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/me");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ApiWithValidToken_ReturnsSessionUserId()
    {
        var client = _factory.CreateClient();
        var session = await AuthenticateAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session);

        var response = await client.GetAsync("/api/me");
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var userId = body.GetProperty("userId").GetString();
        Assert.False(string.IsNullOrEmpty(userId));
    }

    private static async Task<string> AuthenticateAsync(HttpClient client)
    {
        var challenge = await client.GetFromJsonAsync<JsonElement>("/auth/challenge");
        var prefix = challenge.GetProperty("prefix").GetString()!;
        var challengeId = challenge.GetProperty("challengeId").GetString()!;
        var difficulty = challenge.GetProperty("difficulty").GetInt32();

        string nonce = "0";
        for (var i = 0; i < 1_000_000; i++)
        {
            if (PowChallengeService.VerifyProof(prefix, i.ToString(), difficulty))
            {
                nonce = i.ToString();
                break;
            }
        }

        var verify = await client.PostAsJsonAsync("/auth/verify", new { challengeId, nonce });
        verify.EnsureSuccessStatusCode();
        var body = await verify.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("sessionToken").GetString()!;
    }
}

public class GameSanitizerTests
{
    [Fact]
    public void Sanitize_RemovesSecretsPreservesVotingFields()
    {
        var sanitizer = new Application.Games.GameSanitizer();
        var sanitized = sanitizer.Sanitize(new Domain.Models.Game
        {
            Id = 1,
            Name = "Test",
            AdminUserId = "admin",
            ImpostorUserId = "secret",
            Members =
            [
                new Domain.Models.GameMember
                {
                    UserId = "u1",
                    AssignedWord = "word",
                    VotedForUserId = "u2"
                }
            ]
        });

        Assert.Null(sanitized.ImpostorUserId);
        Assert.Null(sanitized.Members![0].AssignedWord);
        Assert.Equal("u2", sanitized.Members[0].VotedForUserId);
    }

    [Fact]
    public void Sanitize_RevealsImpostorWhenFinished()
    {
        var sanitizer = new Application.Games.GameSanitizer();
        var sanitized = sanitizer.Sanitize(new Domain.Models.Game
        {
            Id = 1,
            Name = "Test",
            AdminUserId = "admin",
            Status = "FINISHED",
            ImpostorUserId = "u2",
            Members = [new Domain.Models.GameMember { UserId = "u1" }]
        });

        Assert.Equal("u2", sanitized.ImpostorUserId);
    }

    [Fact]
    public void Sanitize_DoesNotForwardSecretWordWhenFinished()
    {
        var sanitizer = new Application.Games.GameSanitizer();
        var sanitized = sanitizer.Sanitize(new Domain.Models.Game
        {
            Id = 1,
            Name = "Test",
            AdminUserId = "admin",
            Status = "FINISHED",
            ImpostorUserId = "u2",
            SecretWord = new Domain.Models.SecretWord
            {
                Id = 9,
                Authentic = "crew",
                Imposed = "impostor"
            },
            Members = [new Domain.Models.GameMember { UserId = "u1" }]
        });

        Assert.Equal("u2", sanitized.ImpostorUserId);
        Assert.Null(sanitized.SecretWord);
    }

    [Fact]
    public void Sanitize_HidesImpostorWhileGameIsActive()
    {
        var sanitizer = new Application.Games.GameSanitizer();
        var sanitized = sanitizer.Sanitize(new Domain.Models.Game
        {
            Id = 1,
            Name = "Test",
            AdminUserId = "admin",
            Status = "IN_PROGRESS",
            ImpostorUserId = "u2",
            Members = [new Domain.Models.GameMember { UserId = "u1" }]
        });

        Assert.Null(sanitized.ImpostorUserId);
    }

    [Fact]
    public void Sanitize_HidesOtherPlayersVotesDuringVoting()
    {
        var sanitizer = new Application.Games.GameSanitizer();
        var sanitized = sanitizer.Sanitize(new Domain.Models.Game
        {
            Id = 1,
            Name = "Test",
            AdminUserId = "admin",
            Status = "VOTING",
            Members =
            [
                new Domain.Models.GameMember { UserId = "u1", VotedForUserId = "u2" },
                new Domain.Models.GameMember { UserId = "u2", VotedForUserId = "u1" },
            ]
        }, "u1");

        Assert.Equal("u2", sanitized.Members![0].VotedForUserId);
        Assert.Null(sanitized.Members[1].VotedForUserId);
    }

    [Fact]
    public async Task SelfVoteStore_RestoresViewerVoteWhenUpstreamOmitsItOnGet()
    {
        var store = new Infrastructure.Realtime.InMemoryGameSelfVoteStore();
        await store.RecordSelfVoteAsync(42, "u1", "u2");

        var upstream = new Domain.Models.Game
        {
            Id = 42,
            Name = "Test",
            AdminUserId = "admin",
            Status = "VOTING",
            Members =
            [
                new Domain.Models.GameMember { UserId = "u1" },
                new Domain.Models.GameMember { UserId = "u2" },
            ]
        };

        var builder = new Application.Games.GameResponseBuilder(
            new Application.Games.GameSanitizer(),
            new Application.Games.GamePresenceEnricher(new Infrastructure.Realtime.InMemoryGameConnectionRegistry()),
            store);

        var response = await builder.BuildAsync(upstream, "u1");
        Assert.Equal("u2", response.Members!.First(m => m.UserId == "u1").VotedForUserId);
        Assert.Null(response.Members.First(m => m.UserId == "u2").VotedForUserId);
    }

    [Fact]
    public async Task SelfVoteStore_ClearsVotesOnTieReset()
    {
        var store = new Infrastructure.Realtime.InMemoryGameSelfVoteStore();
        await store.SyncFromUpstreamAsync(new Domain.Models.Game
        {
            Id = 42,
            Name = "Test",
            AdminUserId = "admin",
            Status = "VOTING",
            VoteResetCount = 0,
            Members = [new Domain.Models.GameMember { UserId = "u1", VotedForUserId = "u2" }],
        });

        await store.SyncFromUpstreamAsync(new Domain.Models.Game
        {
            Id = 42,
            Name = "Test",
            AdminUserId = "admin",
            Status = "VOTING",
            VoteResetCount = 1,
            Members = [new Domain.Models.GameMember { UserId = "u1" }],
        });

        var upstream = new Domain.Models.Game
        {
            Id = 42,
            Name = "Test",
            AdminUserId = "admin",
            Status = "VOTING",
            VoteResetCount = 1,
            Members = [new Domain.Models.GameMember { UserId = "u1" }],
        };

        var builder = new Application.Games.GameResponseBuilder(
            new Application.Games.GameSanitizer(),
            new Application.Games.GamePresenceEnricher(new Infrastructure.Realtime.InMemoryGameConnectionRegistry()),
            store);

        var response = await builder.BuildAsync(upstream, "u1");
        Assert.Null(response.Members![0].VotedForUserId);
    }

    [Fact]
    public void Sanitize_SerializedGameJsonNeverIncludesAssignedWord()
    {
        var sanitizer = new Application.Games.GameSanitizer();
        var sanitized = sanitizer.Sanitize(new Domain.Models.Game
        {
            Id = 1,
            Name = "Test",
            AdminUserId = "admin",
            Members =
            [
                new Domain.Models.GameMember
                {
                    UserId = "u1",
                    AssignedWord = "secret-word",
                }
            ]
        });

        var json = JsonSerializer.Serialize(sanitized, Application.Realtime.RealtimeJson.Options);
        Assert.DoesNotContain("assignedWord", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret-word", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Sanitize_PreservesDisplayNameAndGameState()
    {
        var sanitizer = new Application.Games.GameSanitizer();
        var sanitized = sanitizer.Sanitize(new Domain.Models.Game
        {
            Id = 1,
            Name = "Test",
            AdminUserId = "admin",
            Status = "VOTING",
            VoteResetCount = 2,
            CurrentTurnUserId = "u2",
            Members =
            [
                new Domain.Models.GameMember
                {
                    UserId = "u1",
                    DisplayName = "Alex",
                    Role = "ADMIN",
                    TurnCompleted = true,
                    Eliminated = false
                }
            ]
        });

        Assert.Equal("Alex", sanitized.Members![0].DisplayName);
        Assert.Equal("VOTING", sanitized.Status);
        Assert.Equal(2, sanitized.VoteResetCount);
        Assert.Equal("u2", sanitized.CurrentTurnUserId);
    }

    [Fact]
    public async Task Enrich_AddsConnectedFlagsPerMember()
    {
        var registry = new Infrastructure.Realtime.InMemoryGameConnectionRegistry();
        await registry.TryRegisterAsync("conn-1", "u1", 9);

        var enricher = new Application.Games.GamePresenceEnricher(registry);
        var enriched = await enricher.EnrichAsync(new Domain.Models.Game
        {
            Id = 9,
            Name = "Test",
            AdminUserId = "admin",
            Members =
            [
                new Domain.Models.GameMember { UserId = "u1" },
                new Domain.Models.GameMember { UserId = "u2" }
            ]
        });

        Assert.True(enriched.Members![0].Connected);
        Assert.False(enriched.Members[1].Connected);
    }
}

public class GameMemberRemovalGuardTests
{
    private static Domain.Models.Game ActiveGame(int memberCount, string adminUserId = "admin") =>
        new()
        {
            Id = 1,
            Name = "Test",
            AdminUserId = adminUserId,
            Status = "IN_PROGRESS",
            Members = Enumerable.Range(1, memberCount)
                .Select(i => new Domain.Models.GameMember
                {
                    UserId = i == 1 ? adminUserId : $"p{i}",
                })
                .ToList(),
        };

    [Fact]
    public async Task ValidateAdminRemoval_AllowsOfflinePlayerInThreePlayerGame()
    {
        var registry = new Infrastructure.Realtime.InMemoryGameConnectionRegistry();
        var game = ActiveGame(3);

        var result = await Application.Games.GameMemberRemovalGuard.ValidateAdminRemovalAsync(
            game,
            "admin",
            "p2",
            registry);

        Assert.True(result.Allowed);
    }

    [Fact]
    public async Task ValidateAdminRemoval_RejectsWhenFewerThanThreeMembers()
    {
        var registry = new Infrastructure.Realtime.InMemoryGameConnectionRegistry();
        var game = ActiveGame(2);

        var result = await Application.Games.GameMemberRemovalGuard.ValidateAdminRemovalAsync(
            game,
            "admin",
            "p2",
            registry);

        Assert.False(result.Allowed);
        Assert.Equal("INSUFFICIENT_MEMBERS", result.ErrorCode);
        Assert.Equal(Application.Games.GameRules.InsufficientMembersForRemovalMessage, result.Message);
    }

    [Fact]
    public async Task ValidateAdminRemoval_RejectsWhenTargetStillConnected()
    {
        var registry = new Infrastructure.Realtime.InMemoryGameConnectionRegistry();
        await registry.TryRegisterAsync("conn-1", "p2", 1);
        var game = ActiveGame(3);

        var result = await Application.Games.GameMemberRemovalGuard.ValidateAdminRemovalAsync(
            game,
            "admin",
            "p2",
            registry);

        Assert.False(result.Allowed);
        Assert.Equal("PLAYER_CONNECTED", result.ErrorCode);
    }

    [Fact]
    public async Task ValidateAdminRemoval_RejectsNonAdmin()
    {
        var registry = new Infrastructure.Realtime.InMemoryGameConnectionRegistry();
        var game = ActiveGame(3);

        var result = await Application.Games.GameMemberRemovalGuard.ValidateAdminRemovalAsync(
            game,
            "p3",
            "p2",
            registry);

        Assert.False(result.Allowed);
        Assert.Equal("FORBIDDEN", result.ErrorCode);
    }

    [Fact]
    public async Task ValidateAdminRemoval_RejectsBeforeGameStarts()
    {
        var registry = new Infrastructure.Realtime.InMemoryGameConnectionRegistry();
        var game = new Domain.Models.Game
        {
            Id = 1,
            Name = "Test",
            AdminUserId = "admin",
            Status = "WAITING",
            Members =
            [
                new Domain.Models.GameMember { UserId = "admin" },
                new Domain.Models.GameMember { UserId = "p2" },
                new Domain.Models.GameMember { UserId = "p3" },
            ]
        };

        var result = await Application.Games.GameMemberRemovalGuard.ValidateAdminRemovalAsync(
            game,
            "admin",
            "p2",
            registry);

        Assert.False(result.Allowed);
        Assert.Equal("GAME_NOT_STARTED", result.ErrorCode);
    }

    [Fact]
    public void IsLegacyInsufficientMembersError_DetectsOldUpstreamMessages()
    {
        Assert.True(Application.Games.GameMemberRemovalGuard.IsLegacyInsufficientMembersError(
            "Removal is only allowed when more than 4 players are in the game."));
        Assert.True(Application.Games.GameMemberRemovalGuard.IsLegacyInsufficientMembersError(
            "Kick is only allowed when more than 4 players are in the game"));
        Assert.False(Application.Games.GameMemberRemovalGuard.IsLegacyInsufficientMembersError(
            Application.Games.GameRules.InsufficientMembersForRemovalMessage));
    }
}

public class GameEventPublisherTests
{
    [Fact]
    public async Task PublishGameChanged_SendsLightweightNotification()
    {
        Application.Realtime.GameRealtimeEnvelope? published = null;
        var backplane = new Mock<Application.Realtime.IGameRealtimeBackplane>();
        backplane.Setup(x => x.PublishAsync(42, It.IsAny<Application.Realtime.GameRealtimeEnvelope>(), It.IsAny<CancellationToken>()))
            .Callback<long, Application.Realtime.GameRealtimeEnvelope, CancellationToken>((_, envelope, _) => published = envelope)
            .Returns(Task.CompletedTask);

        var publisher = new Application.Realtime.GameEventPublisher(
            backplane.Object,
            new Infrastructure.Realtime.InMemoryGameRevisionStore(),
            new Infrastructure.Games.MemoryGameSnapshotCache(
                new Microsoft.Extensions.Caching.Memory.MemoryCache(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions()),
                Microsoft.Extensions.Options.Options.Create(new Application.Configuration.GameSnapshotOptions())),
            Microsoft.Extensions.Options.Options.Create(new Application.Configuration.GameSnapshotOptions()),
            Moq.Mock.Of<Microsoft.Extensions.Logging.ILogger<Application.Realtime.GameEventPublisher>>());

        await publisher.PublishGameChangedAsync(42, "user1", Application.Realtime.GameChangeActions.Vote);

        Assert.NotNull(published);
        Assert.Equal("gameChanged", published!.Notification.Type);
        Assert.Equal("vote", published.Notification.Action);
        Assert.Equal(42, published.Notification.GameId);
        Assert.Equal("user1", published.Notification.TriggeredBy);
        Assert.True(published.Notification.Revision > 0);
        Assert.Null(published.Snapshot);
    }
}

public class GameProxyIntegrationTests
{
    [Fact]
    public async Task CreateAndJoinGame_ProxiesUpstreamResponses()
    {
        var gameApi = new Mock<Application.Games.IGameApiClient>();
        gameApi.Setup(x => x.CreateGameAsync(It.IsAny<string>(), It.IsAny<Domain.Models.CreateGameRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Application.Games.GameApiResponse
            {
                StatusCode = 200,
                Body = """{"id":1,"name":"Test Game","adminUserId":"test-user","status":"WAITING","members":[{"userId":"test-user","role":"ADMIN"}]}"""
            });
        gameApi.Setup(x => x.JoinGameAsync(It.IsAny<string>(), 1, It.IsAny<Domain.Models.JoinGameRequest?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Application.Games.GameApiResponse
            {
                StatusCode = 200,
                Body = """{"id":1,"name":"Test Game","adminUserId":"admin","status":"WAITING","members":[{"userId":"admin","role":"ADMIN"},{"userId":"test-user","role":"MEMBER"}]}"""
            });
        gameApi.Setup(x => x.GetGameAsync(It.IsAny<string>(), 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Application.Games.GameApiResponse
            {
                StatusCode = 200,
                Body = """{"id":1,"name":"Test Game","adminUserId":"admin","status":"WAITING","members":[{"userId":"admin","role":"ADMIN"},{"userId":"test-user","role":"MEMBER"}]}"""
            });

        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<Application.Games.IGameApiClient>();
                services.AddSingleton(gameApi.Object);
            });
        });

        var client = factory.CreateClient();
        var token = await GetSessionTokenAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var create = await client.PostAsJsonAsync("/api/games", new { name = "Test Game" });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        Assert.EndsWith("/api/games/1", create.Headers.Location?.ToString());

        var join = await client.PostAsync("/api/games/1/members", null);
        join.EnsureSuccessStatusCode();

        var get = await client.GetAsync("/api/games/1");
        get.EnsureSuccessStatusCode();
        var game = await get.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, game.GetProperty("id").GetInt64());
    }

    [Fact]
    public async Task JoinWithDisplayName_ProxiesJoinRequest()
    {
        Domain.Models.JoinGameRequest? capturedRequest = null;
        var gameApi = new Mock<Application.Games.IGameApiClient>();
        gameApi.Setup(x => x.JoinGameAsync(
                It.IsAny<string>(),
                1,
                It.IsAny<Domain.Models.JoinGameRequest?>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, long, Domain.Models.JoinGameRequest?, CancellationToken>((_, _, req, _) => capturedRequest = req)
            .ReturnsAsync(new Application.Games.GameApiResponse
            {
                StatusCode = 200,
                Body = """{"id":1,"name":"Test Game","adminUserId":"admin","status":"WAITING","members":[{"userId":"admin","role":"ADMIN"},{"userId":"test-user","role":"MEMBER","displayName":"Alex"}]}"""
            });

        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<Application.Games.IGameApiClient>();
                services.AddSingleton(gameApi.Object);
            });
        });

        var client = factory.CreateClient();
        var token = await GetSessionTokenAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var join = await client.PostAsJsonAsync("/api/games/1/members", new { displayName = "Alex" });
        join.EnsureSuccessStatusCode();

        Assert.NotNull(capturedRequest);
        Assert.Equal("Alex", capturedRequest!.DisplayName);
    }

    [Fact]
    public async Task GetRandomSecretWord_RequiresWaitingGameAdmin()
    {
        var gameApi = new Mock<Application.Games.IGameApiClient>();
        gameApi.Setup(x => x.GetGameAsync(It.IsAny<string>(), 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync((string userId, long _, CancellationToken _) => new Application.Games.GameApiResponse
            {
                StatusCode = 200,
                Body = $$"""{"id":1,"name":"Test Game","adminUserId":"{{userId}}","status":"WAITING","members":[{"userId":"{{userId}}","role":"ADMIN"}]}"""
            });
        gameApi.Setup(x => x.GetRandomSecretWordAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Application.Games.GameApiResponse
            {
                StatusCode = 200,
                Body = """{"id":7,"authentic":"स्याउ","imposed":"सुन्तला"}"""
            });

        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<Application.Games.IGameApiClient>();
                services.AddSingleton(gameApi.Object);
            });
        });

        var client = factory.CreateClient();
        var token = await GetSessionTokenAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync("/api/games/1/secret-words/random");
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(7, body.GetProperty("id").GetInt64());
        Assert.Equal("स्याउ", body.GetProperty("authentic").GetString());
    }

    [Fact]
    public async Task GetRandomSecretWord_DeniesNonAdmin()
    {
        var gameApi = new Mock<Application.Games.IGameApiClient>();
        gameApi.Setup(x => x.GetGameAsync(It.IsAny<string>(), 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Application.Games.GameApiResponse
            {
                StatusCode = 200,
                Body = """{"id":1,"name":"Test Game","adminUserId":"other-admin","status":"WAITING","members":[{"userId":"other-admin","role":"ADMIN"},{"userId":"member","role":"MEMBER"}]}"""
            });

        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<Application.Games.IGameApiClient>();
                services.AddSingleton(gameApi.Object);
            });
        });

        var client = factory.CreateClient();
        var token = await GetSessionTokenAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync("/api/games/1/secret-words/random");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetWordPair_ReturnsPairWhenFinishedMember()
    {
        var gameApi = new Mock<Application.Games.IGameApiClient>();
        gameApi.Setup(x => x.GetGameAsync(It.IsAny<string>(), 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync((string userId, long _, CancellationToken _) => new Application.Games.GameApiResponse
            {
                StatusCode = 200,
                Body = $$"""
                {"id":1,"name":"Test Game","adminUserId":"admin","status":"FINISHED","impostorUserId":"admin",
                 "secretWord":{"id":9,"authentic":"crew","imposed":"impostor"},
                 "members":[{"userId":"admin","role":"ADMIN"},{"userId":"{{userId}}","role":"MEMBER"}]}
                """
            });

        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<Application.Games.IGameApiClient>();
                services.AddSingleton(gameApi.Object);
            });
        });

        var client = factory.CreateClient();
        var token = await GetSessionTokenAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync("/api/games/1/word-pair");
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("crew", body.GetProperty("authentic").GetString());
        Assert.Equal("impostor", body.GetProperty("imposed").GetString());
    }

    [Fact]
    public async Task GetWordPair_ConflictsWhenGameNotFinished()
    {
        var gameApi = new Mock<Application.Games.IGameApiClient>();
        gameApi.Setup(x => x.GetGameAsync(It.IsAny<string>(), 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync((string userId, long _, CancellationToken _) => new Application.Games.GameApiResponse
            {
                StatusCode = 200,
                Body = $$"""
                {"id":1,"name":"Test Game","adminUserId":"{{userId}}","status":"IN_PROGRESS",
                 "members":[{"userId":"{{userId}}","role":"ADMIN"}]}
                """
            });

        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<Application.Games.IGameApiClient>();
                services.AddSingleton(gameApi.Object);
            });
        });

        var client = factory.CreateClient();
        var token = await GetSessionTokenAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync("/api/games/1/word-pair");
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task GetWordPair_ForbiddenForNonMember()
    {
        var gameApi = new Mock<Application.Games.IGameApiClient>();
        gameApi.Setup(x => x.GetGameAsync(It.IsAny<string>(), 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Application.Games.GameApiResponse
            {
                StatusCode = 200,
                Body = """
                {"id":1,"name":"Test Game","adminUserId":"admin","status":"FINISHED",
                 "secretWord":{"id":9,"authentic":"crew","imposed":"impostor"},
                 "members":[{"userId":"admin","role":"ADMIN"},{"userId":"other","role":"MEMBER"}]}
                """
            });

        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<Application.Games.IGameApiClient>();
                services.AddSingleton(gameApi.Object);
            });
        });

        var client = factory.CreateClient();
        var token = await GetSessionTokenAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync("/api/games/1/word-pair");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task StartGame_ProxiesUpstreamResponse()
    {
        var gameApi = new Mock<Application.Games.IGameApiClient>();
        gameApi.Setup(x => x.StartGameAsync(
                It.IsAny<string>(),
                1,
                It.IsAny<Domain.Models.StartGameRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Application.Games.GameApiResponse
            {
                StatusCode = 200,
                Body = """{"id":1,"name":"Test Game","adminUserId":"test-user","status":"IN_PROGRESS","currentRound":1,"members":[]}"""
            });

        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<Application.Games.IGameApiClient>();
                services.AddSingleton(gameApi.Object);
            });
        });

        var client = factory.CreateClient();
        var token = await GetSessionTokenAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.PostAsJsonAsync("/api/games/1/rounds", new { secretWordId = 7 });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("IN_PROGRESS", body.GetProperty("status").GetString());
    }

    private static async Task<string> GetSessionTokenAsync(HttpClient client)
    {
        var challenge = await client.GetFromJsonAsync<JsonElement>("/auth/challenge");
        var prefix = challenge.GetProperty("prefix").GetString()!;
        var challengeId = challenge.GetProperty("challengeId").GetString()!;
        var difficulty = challenge.GetProperty("difficulty").GetInt32();

        for (var i = 0; i < 1_000_000; i++)
        {
            if (PowChallengeService.VerifyProof(prefix, i.ToString(), difficulty))
            {
                var verify = await client.PostAsJsonAsync("/auth/verify", new { challengeId, nonce = i.ToString() });
                verify.EnsureSuccessStatusCode();
                var body = await verify.Content.ReadFromJsonAsync<JsonElement>();
                return body.GetProperty("sessionToken").GetString()!;
            }
        }

        throw new InvalidOperationException("Unable to solve PoW.");
    }
}

internal sealed class StubTokenHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null
            ? "{}"
            : request.Content.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult();

        if (body.Contains("client_credentials", StringComparison.Ordinal))
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""
                    {"access_token":"stub-token","expires_in":3600,"token_type":"Bearer"}
                    """, Encoding.UTF8, "application/json")
            });
        }

        if (request.RequestUri!.AbsolutePath.Contains("openid-configuration"))
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""
                    {"token_endpoint":"https://customauth.test/connect/token"}
                    """, Encoding.UTF8, "application/json")
            });
        }

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
    }
}

public class SecurityHeadersTests : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task Responses_IncludeSecurityHeaders()
    {
        var client = new WebApplicationFactory<Program>().CreateClient();
        var response = await client.GetAsync("/health");
        response.EnsureSuccessStatusCode();

        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
        Assert.True(response.Headers.Contains("Referrer-Policy"));
    }
}

public class CustomAuthTokenServiceTests
{
    [Fact]
    public async Task GetServiceTokenAsync_CachesUntilNearExpiry()
    {
        var handler = new CountingTokenHandler();
        var services = new ServiceCollection();
        services.AddMemoryCache();
        services.AddLogging();
        services.Configure<Application.Configuration.CustomAuthOptions>(options =>
        {
            options.Authority = "https://customauth.test/";
            options.ClientId = "client";
            options.ClientSecret = "secret";
            options.Audience = "wordgame";
        });
        services.AddHttpClient<ICustomAuthTokenService, Infrastructure.Auth.CustomAuthTokenService>()
            .ConfigurePrimaryHttpMessageHandler(() => handler);

        await using var provider = services.BuildServiceProvider();
        var service = provider.GetRequiredService<ICustomAuthTokenService>();

        var token1 = await service.GetServiceTokenAsync();
        var token2 = await service.GetServiceTokenAsync();

        Assert.Equal("stub-token", token1);
        Assert.Equal(token1, token2);
        Assert.Equal(2, handler.CallCount);
    }
}

internal sealed class CountingTokenHandler : HttpMessageHandler
{
    public int CallCount { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        CallCount++;

        if (request.RequestUri!.AbsolutePath.Contains("openid-configuration"))
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""
                    {"token_endpoint":"https://customauth.test/connect/token"}
                    """, Encoding.UTF8, "application/json")
            });
        }

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""
                {"access_token":"stub-token","expires_in":3600,"token_type":"Bearer"}
                """, Encoding.UTF8, "application/json")
        });
    }
}
