using System.Text.Json.Serialization;

namespace WordGameBff.Domain.Models;

public sealed class CreateGameRequest
{
    public required string Name { get; init; }
    public string? DisplayName { get; init; }
}

public sealed class StartGameRequest
{
    public long SecretWordId { get; init; }
}

public sealed class VoteRequest
{
    public required string VotedUserId { get; init; }
}

public sealed class JoinGameRequest
{
    public string? DisplayName { get; init; }
}

public sealed class UserInfo
{
    public required string UserId { get; init; }
}

public sealed class Game
{
    public long? Id { get; init; }
    public required string Name { get; init; }
    public required string AdminUserId { get; init; }
    public string? Status { get; init; }
    public string? Outcome { get; init; }
    public int? CurrentRound { get; init; }
    public int? VoteResetCount { get; init; }
    public string? CurrentTurnUserId { get; init; }
    public string? ImpostorUserId { get; init; }
    /// <summary>Upstream may nest the word pair on finished games; never forwarded via GameSanitizer.</summary>
    public SecretWord? SecretWord { get; init; }
    public IList<GameMember>? Members { get; init; }
}

public sealed class GameMember
{
    public long? Id { get; init; }
    public required string UserId { get; init; }
    public string? DisplayName { get; init; }
    public string? Role { get; init; }
    public bool? TurnCompleted { get; init; }
    public bool? Eliminated { get; init; }
    [JsonIgnore]
    public string? AssignedWord { get; init; }
    public string? VotedForUserId { get; init; }
    public bool? Connected { get; init; }
}

public sealed class SecretWord
{
    public long? Id { get; init; }
    public required string Authentic { get; init; }
    public required string Imposed { get; init; }
}

public sealed class MyWordResponse
{
    public string? Word { get; init; }
    public string? Type { get; init; }
}

public sealed class PowChallenge
{
    public required string ChallengeId { get; init; }
    public required string Prefix { get; init; }
    public int Difficulty { get; init; }
    public DateTimeOffset ExpiresAt { get; init; }
    public bool Consumed { get; set; }
}

public sealed class SessionTokenResult
{
    public required string SessionToken { get; init; }
    public required string UserId { get; init; }
    public DateTimeOffset ExpiresAt { get; init; }
}

public sealed class ApiError
{
    public required string Error { get; init; }
    public required string Message { get; init; }
}
