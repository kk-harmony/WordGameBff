using WordGameBff.Infrastructure.Storage.Redis;

namespace WordGameBff.Tests;

/// <summary>In-process Redis stand-in for store unit tests.</summary>
public sealed class InMemoryBffRedisDatabase : IBffRedisDatabase
{
    private readonly object _gate = new();
    private readonly Dictionary<string, (string Value, DateTimeOffset? ExpiresAt)> _strings = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HashSet<string>> _sets = new(StringComparer.Ordinal);
    private readonly Func<DateTimeOffset> _utcNow;

    public InMemoryBffRedisDatabase(Func<DateTimeOffset>? utcNow = null)
    {
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
    }

    public Task SetStringAsync(
        string key,
        string value,
        TimeSpan? ttl = null,
        CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            DateTimeOffset? expires = ttl is { } life ? _utcNow().Add(life) : null;
            _strings[key] = (value, expires);
        }

        return Task.CompletedTask;
    }

    public Task<string?> GetStringAsync(string key, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            return Task.FromResult(ReadString(key));
        }
    }

    public Task<string?> GetDeleteStringAsync(string key, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            var value = ReadString(key);
            if (value is not null)
            {
                _strings.Remove(key);
            }

            return Task.FromResult(value);
        }
    }

    public Task<bool> KeyExistsAsync(string key, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            return Task.FromResult(ReadString(key) is not null || LiveSet(key) is not null);
        }
    }

    public Task<bool> DeleteKeyAsync(string key, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            var removed = _strings.Remove(key) | _sets.Remove(key);
            return Task.FromResult(removed);
        }
    }

    public Task ExpireAsync(string key, TimeSpan ttl, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (_strings.TryGetValue(key, out var entry) && !IsExpired(entry.ExpiresAt))
            {
                _strings[key] = (entry.Value, _utcNow().Add(ttl));
            }
        }

        return Task.CompletedTask;
    }

    public Task<long> IncrementAsync(string key, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            var current = 0L;
            var existing = ReadString(key);
            if (existing is not null)
            {
                _ = long.TryParse(existing, out current);
            }

            var next = current + 1;
            _strings[key] = (next.ToString(), null);
            return Task.FromResult(next);
        }
    }

    public Task SetAddAsync(string key, string member, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (!_sets.TryGetValue(key, out var set))
            {
                set = new HashSet<string>(StringComparer.Ordinal);
                _sets[key] = set;
            }

            set.Add(member);
        }

        return Task.CompletedTask;
    }

    public Task SetRemoveAsync(string key, string member, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (_sets.TryGetValue(key, out var set))
            {
                set.Remove(member);
            }
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<string>> SetMembersAsync(string key, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            var set = LiveSet(key);
            IReadOnlyList<string> members = set is null ? Array.Empty<string>() : set.ToList();
            return Task.FromResult(members);
        }
    }

    private string? ReadString(string key)
    {
        if (!_strings.TryGetValue(key, out var entry))
        {
            return null;
        }

        if (IsExpired(entry.ExpiresAt))
        {
            _strings.Remove(key);
            return null;
        }

        return entry.Value;
    }

    private HashSet<string>? LiveSet(string key) =>
        _sets.TryGetValue(key, out var set) ? set : null;

    private bool IsExpired(DateTimeOffset? expiresAt) =>
        expiresAt is { } at && at <= _utcNow();
}
