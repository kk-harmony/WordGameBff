using WordGameBff.Application.Games;
using WordGameBff.Domain.Models;

namespace WordGameBff.Tests;

public class UpstreamErrorNormalizerTests
{
    private readonly IUpstreamErrorNormalizer _normalizer = new UpstreamErrorNormalizer();

    [Fact]
    public void NormalizeErrorBody_MapsUpstreamTypeToClientError()
    {
        var normalized = _normalizer.NormalizeErrorBody(
            """{"type":"NOT_FOUND","message":"Game not found","details":null}""");

        Assert.Contains("\"error\":\"NOT_FOUND\"", normalized);
        Assert.Contains("\"message\":\"Game not found\"", normalized);
    }

    [Fact]
    public void NormalizeErrorBody_PreservesExistingClientError()
    {
        var input = """{"error":"FORBIDDEN","message":"Denied"}""";
        var normalized = _normalizer.NormalizeErrorBody(input);

        Assert.Contains("\"error\":\"FORBIDDEN\"", normalized);
        Assert.Contains("\"message\":\"Denied\"", normalized);
    }

    [Fact]
    public void NormalizeErrorBody_ReturnsFallbackForEmptyBody()
    {
        var normalized = _normalizer.NormalizeErrorBody(null);

        Assert.Contains("\"error\":\"HTTP_ERROR\"", normalized);
    }
}

public class SecretWordAccessPolicyTests
{
    private readonly ISecretWordAccessPolicy _policy = new SecretWordAccessPolicy();

    [Fact]
    public void CanViewWordPair_AllowsWaitingGameAdmin()
    {
        var game = new Game
        {
            Name = "Test",
            AdminUserId = "admin",
            Status = "WAITING"
        };

        Assert.True(_policy.CanViewWordPair("admin", game));
    }

    [Fact]
    public void CanViewWordPair_DeniesNonAdmin()
    {
        var game = new Game
        {
            Name = "Test",
            AdminUserId = "admin",
            Status = "WAITING"
        };

        Assert.False(_policy.CanViewWordPair("member", game));
    }

    [Fact]
    public void CanViewWordPair_DeniesAdminAfterGameStarts()
    {
        var game = new Game
        {
            Name = "Test",
            AdminUserId = "admin",
            Status = "IN_PROGRESS"
        };

        Assert.False(_policy.CanViewWordPair("admin", game));
    }
}

public class SecretWordResponseBuilderTests
{
    private readonly ISecretWordResponseBuilder _builder = new SecretWordResponseBuilder();

    [Fact]
    public void Build_WithWordPair_IncludesBothWords()
    {
        var response = _builder.Build(new SecretWord
        {
            Id = 7,
            Authentic = "apple",
            Imposed = "apricot"
        }, includeWordPair: true);

        Assert.Equal("apple", response.Authentic);
        Assert.Equal("apricot", response.Imposed);
    }

    [Fact]
    public void Build_WithoutWordPair_OmitsBothWords()
    {
        var response = _builder.Build(new SecretWord
        {
            Id = 7,
            Authentic = "apple",
            Imposed = "apricot"
        }, includeWordPair: false);

        Assert.Equal(7, response.Id);
        Assert.Null(response.Authentic);
        Assert.Null(response.Imposed);
    }
}
