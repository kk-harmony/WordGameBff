using System.Text.Json;
using System.Text.Json.Serialization;
using WordGameBff.Application.Games;
using WordGameBff.Domain.Models;

namespace WordGameBff.Infrastructure.Storage.Redis;

public sealed class RedisGameSelfVoteStore : IGameSelfVoteStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IBffRedisDatabase _redis;

    public RedisGameSelfVoteStore(IBffRedisDatabase redis)
    {
        _redis = redis;
    }

    public async Task RecordSelfVoteAsync(
        long gameId,
        string voterUserId,
        string votedForUserId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(votedForUserId))
        {
            return;
        }

        var state = await LoadStateAsync(gameId, cancellationToken);
        state.Votes[voterUserId] = votedForUserId;
        await SaveStateAsync(gameId, state, cancellationToken);
    }

    public async Task SyncFromUpstreamAsync(Game game, CancellationToken cancellationToken = default)
    {
        if (game.Id is not long gameId)
        {
            return;
        }

        var state = await LoadStateAsync(gameId, cancellationToken);
        var resetCount = game.VoteResetCount ?? 0;
        if (state.VoteResetCount != resetCount)
        {
            state.Votes.Clear();
        }

        state.VoteResetCount = resetCount;

        if (!IsVotingStatus(game.Status))
        {
            state.Votes.Clear();
            await SaveStateAsync(gameId, state, cancellationToken);
            return;
        }

        foreach (var member in game.Members ?? [])
        {
            if (string.IsNullOrEmpty(member.VotedForUserId))
            {
                continue;
            }

            state.Votes[member.UserId] = member.VotedForUserId;
        }

        await SaveStateAsync(gameId, state, cancellationToken);
    }

    public async Task<Game> ApplyViewerSelfVoteAsync(
        Game game,
        string viewerUserId,
        CancellationToken cancellationToken = default)
    {
        if (game.Id is not long gameId || !IsVotingStatus(game.Status) || game.Members is null)
        {
            return game;
        }

        var state = await LoadStateAsync(gameId, cancellationToken);
        var members = game.Members.ToList();
        var memberIndex = members.FindIndex(member => member.UserId == viewerUserId);
        if (memberIndex < 0)
        {
            return game;
        }

        var member = members[memberIndex];
        if (!string.IsNullOrEmpty(member.VotedForUserId))
        {
            return game;
        }

        if (!state.Votes.TryGetValue(viewerUserId, out var votedForUserId))
        {
            return game;
        }

        members[memberIndex] = new GameMember
        {
            Id = member.Id,
            UserId = member.UserId,
            DisplayName = member.DisplayName,
            Role = member.Role,
            TurnCompleted = member.TurnCompleted,
            Eliminated = member.Eliminated,
            VotedForUserId = votedForUserId,
            Connected = member.Connected,
        };

        return new Game
        {
            Id = game.Id,
            Name = game.Name,
            AdminUserId = game.AdminUserId,
            Status = game.Status,
            Outcome = game.Outcome,
            CurrentRound = game.CurrentRound,
            VoteResetCount = game.VoteResetCount,
            CurrentTurnUserId = game.CurrentTurnUserId,
            ImpostorUserId = game.ImpostorUserId,
            Members = members,
        };
    }

    private async Task<SelfVoteState> LoadStateAsync(long gameId, CancellationToken cancellationToken)
    {
        var json = await _redis.GetStringAsync(Key(gameId), cancellationToken);
        if (json is null)
        {
            return new SelfVoteState();
        }

        return JsonSerializer.Deserialize<SelfVoteState>(json, JsonOptions) ?? new SelfVoteState();
    }

    private Task SaveStateAsync(long gameId, SelfVoteState state, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(state, JsonOptions);
        return _redis.SetStringAsync(Key(gameId), json, ttl: null, cancellationToken);
    }

    private static bool IsVotingStatus(string? status) =>
        string.Equals(status, "VOTING", StringComparison.OrdinalIgnoreCase)
        || string.Equals(status, "VOTE", StringComparison.OrdinalIgnoreCase);

    private static string Key(long gameId) => $"bff:selfvote:{gameId}";

    private sealed class SelfVoteState
    {
        [JsonPropertyName("votes")]
        public Dictionary<string, string> Votes { get; set; } = new();

        [JsonPropertyName("voteResetCount")]
        public int VoteResetCount { get; set; }
    }
}
