using System.Collections.Concurrent;
using WordGameBff.Application.Games;
using WordGameBff.Domain.Models;

namespace WordGameBff.Infrastructure.Realtime;

/// <summary>
/// Retains each player's own vote when upstream GET omits vote fields during voting.
/// </summary>
public sealed class InMemoryGameSelfVoteStore : IGameSelfVoteStore
{
    private readonly ConcurrentDictionary<(long GameId, string UserId), string> _votes = new();
    private readonly ConcurrentDictionary<long, int> _voteResetCounts = new();

    public Task RecordSelfVoteAsync(
        long gameId,
        string voterUserId,
        string votedForUserId,
        CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrEmpty(votedForUserId))
        {
            _votes[(gameId, voterUserId)] = votedForUserId;
        }

        return Task.CompletedTask;
    }

    public Task SyncFromUpstreamAsync(Game game, CancellationToken cancellationToken = default)
    {
        if (game.Id is not long gameId)
        {
            return Task.CompletedTask;
        }

        var resetCount = game.VoteResetCount ?? 0;
        if (_voteResetCounts.TryGetValue(gameId, out var previousReset) && previousReset != resetCount)
        {
            ClearGame(gameId);
        }

        _voteResetCounts[gameId] = resetCount;

        if (!IsVotingStatus(game.Status))
        {
            ClearGame(gameId);
            return Task.CompletedTask;
        }

        foreach (var member in game.Members ?? [])
        {
            if (string.IsNullOrEmpty(member.VotedForUserId))
            {
                continue;
            }

            _votes[(gameId, member.UserId)] = member.VotedForUserId;
        }

        return Task.CompletedTask;
    }

    public Task<Game> ApplyViewerSelfVoteAsync(
        Game game,
        string viewerUserId,
        CancellationToken cancellationToken = default)
    {
        if (game.Id is not long gameId || !IsVotingStatus(game.Status) || game.Members is null)
        {
            return Task.FromResult(game);
        }

        var members = game.Members.ToList();
        var memberIndex = members.FindIndex(member => member.UserId == viewerUserId);
        if (memberIndex < 0)
        {
            return Task.FromResult(game);
        }

        var member = members[memberIndex];
        if (!string.IsNullOrEmpty(member.VotedForUserId))
        {
            return Task.FromResult(game);
        }

        if (!_votes.TryGetValue((gameId, viewerUserId), out var votedForUserId))
        {
            return Task.FromResult(game);
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

        return Task.FromResult(new Game
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
        });
    }

    private void ClearGame(long gameId)
    {
        foreach (var key in _votes.Keys.Where(key => key.GameId == gameId).ToList())
        {
            _votes.TryRemove(key, out _);
        }
    }

    private static bool IsVotingStatus(string? status) =>
        string.Equals(status, "VOTING", StringComparison.OrdinalIgnoreCase)
        || string.Equals(status, "VOTE", StringComparison.OrdinalIgnoreCase);
}
