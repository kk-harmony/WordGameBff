using System.Text.Json;
using WordGameBff.Application.Realtime;
using WordGameBff.Domain.Models;

namespace WordGameBff.Application.Games;

/// <summary>
/// Read-through helper: serve raw upstream game JSON from memory when present and
/// the caller is a member; otherwise fetch upstream once per concurrent miss.
/// </summary>
public interface IGameSnapshotReader
{
    Task<GameApiResponse> GetGameAsync(string userId, long gameId, CancellationToken cancellationToken = default);
    Task<Game?> GetGameModelAsync(string userId, long gameId, CancellationToken cancellationToken = default);
}

public sealed class GameSnapshotReader : IGameSnapshotReader
{
    private readonly IGameApiClient _gameApiClient;
    private readonly IGameSnapshotCache _cache;
    private readonly IGameRevisionStore _revisionStore;
    private readonly IGameSnapshotFetchGate _fetchGate;

    public GameSnapshotReader(
        IGameApiClient gameApiClient,
        IGameSnapshotCache cache,
        IGameRevisionStore revisionStore,
        IGameSnapshotFetchGate fetchGate)
    {
        _gameApiClient = gameApiClient;
        _cache = cache;
        _revisionStore = revisionStore;
        _fetchGate = fetchGate;
    }

    public async Task<GameApiResponse> GetGameAsync(
        string userId,
        long gameId,
        CancellationToken cancellationToken = default)
    {
        if (TryServeFromCache(userId, gameId, out var cached))
        {
            return cached!;
        }

        return await _fetchGate.RunAsync(gameId, async ct =>
        {
            if (TryServeFromCache(userId, gameId, out var inside))
            {
                return inside!;
            }

            // Read the revision before the fetch: a mutation landing mid-flight publishes a
            // higher one, so the cache keeps that newer state instead of this now-stale body.
            var revision = await _revisionStore.GetCurrentRevisionAsync(gameId, ct);
            var response = await _gameApiClient.GetGameAsync(userId, gameId, ct);
            if (response is null)
            {
                return new GameApiResponse
                {
                    StatusCode = 502,
                    Body = """{"error":"BAD_GATEWAY","message":"Upstream returned no response."}""",
                };
            }

            if (response.IsSuccess)
            {
                _cache.Set(gameId, response.Body, revision);
            }

            return response;
        }, cancellationToken);
    }

    public async Task<Game?> GetGameModelAsync(
        string userId,
        long gameId,
        CancellationToken cancellationToken = default)
    {
        var response = await GetGameAsync(userId, gameId, cancellationToken);
        if (!response.IsSuccess)
        {
            return null;
        }

        return JsonSerializer.Deserialize<Game>(response.Body, RealtimeJson.Options);
    }

    private bool TryServeFromCache(string userId, long gameId, out GameApiResponse? response)
    {
        response = null;
        if (!_cache.TryGet(gameId, out var snapshot) || snapshot is null)
        {
            return false;
        }

        var game = JsonSerializer.Deserialize<Game>(snapshot.RawJson, RealtimeJson.Options);
        if (game is null || !GameMembership.IsMember(userId, game))
        {
            // Non-members must not learn from the shared cache; fall through to upstream.
            return false;
        }

        response = new GameApiResponse
        {
            StatusCode = 200,
            Body = snapshot.RawJson,
        };
        return true;
    }
}
