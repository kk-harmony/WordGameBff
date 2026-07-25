using System.Text.Json;
using System.Text.Json.Serialization;
using WordGameBff.Domain.Models;

namespace WordGameBff.Application.Realtime;

/// <summary>
/// Client-facing realtime notification. When <see cref="Game"/> is set, clients apply it
/// directly; otherwise they fetch authoritative state via GET /api/games/{id}.
/// </summary>
public sealed class GameRealtimeMessage
{
    public required string Type { get; init; }
    public required long GameId { get; init; }
    public required long Revision { get; init; }
    public string? TriggeredBy { get; init; }
    public string? Action { get; init; }

    /// <summary>Viewer-sanitized game snapshot. Omitted on oversized or snapshot-less events.</summary>
    public Game? Game { get; init; }

    public string ToJson() => JsonSerializer.Serialize(this, RealtimeJson.Options);
    public static GameRealtimeMessage? FromJson(string json) =>
        JsonSerializer.Deserialize<GameRealtimeMessage>(json, RealtimeJson.Options);
}

/// <summary>
/// Backplane payload. Separates fanout model, raw JSON for cache seeding, and explicit
/// invalidation so "no push payload" is not confused with "clear the cache".
/// </summary>
public sealed class GameRealtimeEnvelope
{
    public required GameRealtimeMessage Notification { get; init; }

    /// <summary>Raw upstream game for per-viewer fanout when push is enabled.</summary>
    public Game? Snapshot { get; init; }

    /// <summary>Upstream JSON used to seed the local cache (preferred over re-serializing Snapshot).</summary>
    public string? SnapshotJson { get; init; }

    /// <summary>When true, receiving instances must drop any cached body for this game.</summary>
    public bool InvalidateCache { get; init; }

    public string ToJson() => JsonSerializer.Serialize(this, RealtimeJson.Options);

    public static GameRealtimeEnvelope? FromJson(string json) =>
        JsonSerializer.Deserialize<GameRealtimeEnvelope>(json, RealtimeJson.Options);
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
    Task PublishToUserInGameAsync(long gameId, string userId, GameRealtimeMessage message, CancellationToken cancellationToken = default);
}

public interface IGameRealtimeBackplane
{
    Task PublishAsync(long gameId, GameRealtimeEnvelope envelope, CancellationToken cancellationToken = default);
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
        string? snapshotJson = null,
        CancellationToken cancellationToken = default);
}

public interface IGameRevisionStore
{
    Task<long> GetCurrentRevisionAsync(long gameId, CancellationToken cancellationToken = default);
    Task<long> GetNextRevisionAsync(long gameId, CancellationToken cancellationToken = default);
}
