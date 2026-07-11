using System.Text.Json;
using System.Text.Json.Serialization;

namespace WordGameBff.Application.Realtime;

/// <summary>
/// Lightweight realtime notification. Clients fetch authoritative state via GET /api/games/{id}.
/// </summary>
public sealed class GameRealtimeMessage
{
    public required string Type { get; init; }
    public required long GameId { get; init; }
    public required long Revision { get; init; }
    public string? TriggeredBy { get; init; }
    public string? Action { get; init; }

    public string ToJson() => JsonSerializer.Serialize(this, RealtimeJson.Options);
    public static GameRealtimeMessage? FromJson(string json) =>
        JsonSerializer.Deserialize<GameRealtimeMessage>(json, RealtimeJson.Options);
}

public static class RealtimeJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}

public interface IGameRealtimeTransport
{
    Task PublishToGameAsync(long gameId, GameRealtimeMessage message, CancellationToken cancellationToken = default);
}

public interface IGameRealtimeBackplane
{
    Task PublishAsync(long gameId, GameRealtimeMessage message, CancellationToken cancellationToken = default);
}

public interface IGameConnectionRegistry
{
    Task<bool> TryRegisterAsync(string connectionId, string userId, long gameId, CancellationToken cancellationToken = default);
    Task UnregisterAsync(string connectionId, CancellationToken cancellationToken = default);
    Task RefreshAsync(string connectionId, CancellationToken cancellationToken = default);
    Task<int> GetConnectionCountForUserAsync(string userId, CancellationToken cancellationToken = default);
    Task<bool> IsUserConnectedToGameAsync(string userId, long gameId, CancellationToken cancellationToken = default);
}

public interface IGameEventPublisher
{
    Task PublishGameChangedAsync(
        long gameId,
        string triggeredByUserId,
        string action,
        CancellationToken cancellationToken = default);
}

public interface IGameRevisionStore
{
    Task<long> GetCurrentRevisionAsync(long gameId, CancellationToken cancellationToken = default);
    Task<long> GetNextRevisionAsync(long gameId, CancellationToken cancellationToken = default);
}
