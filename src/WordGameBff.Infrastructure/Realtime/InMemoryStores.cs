using System.Collections.Concurrent;
using WordGameBff.Application.Realtime;

namespace WordGameBff.Infrastructure.Realtime;

public sealed class InMemoryGameRevisionStore : IGameRevisionStore
{
    private readonly ConcurrentDictionary<long, long> _revisions = new();

    public Task<long> GetCurrentRevisionAsync(long gameId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_revisions.TryGetValue(gameId, out var revision) ? revision : 0);

    public Task<long> GetNextRevisionAsync(long gameId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_revisions.AddOrUpdate(gameId, 1, (_, current) => current + 1));
}

public sealed class InMemoryGameConnectionRegistry : IGameConnectionRegistry
{
    private readonly ConcurrentDictionary<string, ConnectionEntry> _connections = new();
    private readonly ConcurrentDictionary<string, int> _userConnectionCounts = new();

    public Task<bool> TryRegisterAsync(
        string connectionId,
        string userId,
        long gameId,
        CancellationToken cancellationToken = default)
    {
        _connections[connectionId] = new ConnectionEntry(userId, gameId);
        _userConnectionCounts.AddOrUpdate(userId, 1, (_, current) => current + 1);
        return Task.FromResult(true);
    }

    public Task UnregisterAsync(string connectionId, CancellationToken cancellationToken = default)
    {
        if (_connections.TryRemove(connectionId, out var entry))
        {
            _userConnectionCounts.AddOrUpdate(entry.UserId, 0, (_, current) => Math.Max(0, current - 1));
        }

        return Task.CompletedTask;
    }

    public Task RefreshAsync(string connectionId, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task<int> GetConnectionCountForUserAsync(string userId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_userConnectionCounts.TryGetValue(userId, out var count) ? count : 0);

    public Task<bool> IsUserConnectedToGameAsync(string userId, long gameId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_connections.Values.Any(entry => entry.UserId == userId && entry.GameId == gameId));

    private sealed record ConnectionEntry(string UserId, long GameId);
}
