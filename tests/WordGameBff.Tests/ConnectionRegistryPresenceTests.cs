using WordGameBff.Application.Realtime;

namespace WordGameBff.Tests;

public class ConnectionRegistryPresenceTests
{
    [Fact]
    public async Task Refresh_ExtendsTtlSoPresenceSurvivesPastOriginalExpiry()
    {
        var clock = new ControllableClock(DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
        var registry = new TtlGameConnectionRegistry(TimeSpan.FromMinutes(5), clock);
        await registry.TryRegisterAsync("conn-1", "u1", 9);

        clock.Advance(TimeSpan.FromMinutes(4));
        await registry.RefreshAsync("conn-1");
        clock.Advance(TimeSpan.FromMinutes(4));

        Assert.True(await registry.IsUserConnectedToGameAsync("u1", 9));
    }

    [Fact]
    public async Task WithoutRefresh_PresenceExpiresAfterTtl()
    {
        var clock = new ControllableClock(DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
        var registry = new TtlGameConnectionRegistry(TimeSpan.FromMinutes(5), clock);
        await registry.TryRegisterAsync("conn-1", "u1", 9);

        clock.Advance(TimeSpan.FromMinutes(6));

        Assert.False(await registry.IsUserConnectedToGameAsync("u1", 9));
    }

    private sealed class ControllableClock
    {
        public ControllableClock(DateTimeOffset now) => UtcNow = now;
        public DateTimeOffset UtcNow { get; private set; }
        public void Advance(TimeSpan by) => UtcNow = UtcNow.Add(by);
    }

    /// <summary>Test double mirroring Postgres TTL + RefreshAsync behavior.</summary>
    private sealed class TtlGameConnectionRegistry : IGameConnectionRegistry
    {
        private readonly TimeSpan _ttl;
        private readonly ControllableClock _clock;
        private readonly Dictionary<string, (string UserId, long GameId, DateTimeOffset ExpiresAt)> _entries = new();

        public TtlGameConnectionRegistry(TimeSpan ttl, ControllableClock clock)
        {
            _ttl = ttl;
            _clock = clock;
        }

        public Task<bool> TryRegisterAsync(
            string connectionId,
            string userId,
            long gameId,
            CancellationToken cancellationToken = default)
        {
            _entries[connectionId] = (userId, gameId, _clock.UtcNow.Add(_ttl));
            return Task.FromResult(true);
        }

        public Task UnregisterAsync(string connectionId, CancellationToken cancellationToken = default)
        {
            _entries.Remove(connectionId);
            return Task.CompletedTask;
        }

        public Task RefreshAsync(string connectionId, CancellationToken cancellationToken = default)
        {
            if (_entries.TryGetValue(connectionId, out var entry) && entry.ExpiresAt > _clock.UtcNow)
            {
                _entries[connectionId] = (entry.UserId, entry.GameId, _clock.UtcNow.Add(_ttl));
            }

            return Task.CompletedTask;
        }

        public Task<int> GetConnectionCountForUserAsync(string userId, CancellationToken cancellationToken = default)
        {
            PurgeExpired();
            return Task.FromResult(_entries.Values.Count(e => e.UserId == userId));
        }

        public Task<bool> IsUserConnectedToGameAsync(string userId, long gameId, CancellationToken cancellationToken = default)
        {
            PurgeExpired();
            return Task.FromResult(_entries.Values.Any(e => e.UserId == userId && e.GameId == gameId));
        }

        private void PurgeExpired()
        {
            var now = _clock.UtcNow;
            foreach (var key in _entries.Where(pair => pair.Value.ExpiresAt <= now).Select(pair => pair.Key).ToList())
            {
                _entries.Remove(key);
            }
        }
    }
}
