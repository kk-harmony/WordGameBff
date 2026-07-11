using WordGameBff.Domain.Models;

namespace WordGameBff.Application.Games;

public interface IGameSanitizer
{
    Game Sanitize(Game game, string? viewerUserId = null);
}

public sealed class GameSanitizer : IGameSanitizer
{
    public Game Sanitize(Game game, string? viewerUserId = null)
    {
        var revealImpostor = GameStatusRules.IsFinished(game.Status);
        var hideOtherVotes = GameStatusRules.IsVoting(game.Status) && !string.IsNullOrEmpty(viewerUserId);
        return new Game
        {
            Id = game.Id,
            Name = game.Name,
            AdminUserId = game.AdminUserId,
            Status = game.Status,
            Outcome = game.Outcome,
            CurrentRound = game.CurrentRound,
            VoteResetCount = game.VoteResetCount ?? 0,
            CurrentTurnUserId = game.CurrentTurnUserId,
            ImpostorUserId = revealImpostor ? game.ImpostorUserId : null,
            // SecretWord intentionally omitted — clients use GET .../word-pair when finished.
            Members = game.Members?.Select(m => new GameMember
            {
                Id = m.Id,
                UserId = m.UserId,
                DisplayName = m.DisplayName,
                Role = m.Role,
                TurnCompleted = m.TurnCompleted,
                Eliminated = m.Eliminated,
                VotedForUserId = hideOtherVotes && !string.Equals(m.UserId, viewerUserId, StringComparison.Ordinal)
                    ? null
                    : m.VotedForUserId,
                Connected = m.Connected
            }).ToList()
        };
    }
}
