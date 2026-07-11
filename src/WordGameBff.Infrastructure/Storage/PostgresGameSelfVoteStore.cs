using System.Text.Json.Serialization;
using WordGameBff.Application.Games;
using WordGameBff.Domain.Models;

namespace WordGameBff.Infrastructure.Storage;

public sealed class PostgresGameSelfVoteStore : IGameSelfVoteStore
{
    private const string Namespace = "selfvote";
    private readonly PostgresKeyValueStore _store;

    public PostgresGameSelfVoteStore(PostgresKeyValueStore store)
    {
        _store = store;
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

    private async Task<SelfVoteState> LoadStateAsync(long gameId, CancellationToken cancellationToken) =>
        await _store.GetAsync<SelfVoteState>(Namespace, gameId.ToString(), cancellationToken)
        ?? new SelfVoteState();

    private Task SaveStateAsync(long gameId, SelfVoteState state, CancellationToken cancellationToken) =>
        _store.SetAsync(Namespace, gameId.ToString(), state, cancellationToken: cancellationToken);

    private static bool IsVotingStatus(string? status) =>
        string.Equals(status, "VOTING", StringComparison.OrdinalIgnoreCase)
        || string.Equals(status, "VOTE", StringComparison.OrdinalIgnoreCase);

    private sealed class SelfVoteState
    {
        [JsonPropertyName("votes")]
        public Dictionary<string, string> Votes { get; set; } = new();

        [JsonPropertyName("voteResetCount")]
        public int VoteResetCount { get; set; }
    }
}
