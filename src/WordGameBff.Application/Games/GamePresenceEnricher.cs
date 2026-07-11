using WordGameBff.Application.Realtime;
using WordGameBff.Domain.Models;

namespace WordGameBff.Application.Games;

public interface IGamePresenceEnricher
{
    Task<Game> EnrichAsync(Game game, CancellationToken cancellationToken = default);
}

public sealed class GamePresenceEnricher : IGamePresenceEnricher
{
    private readonly IGameConnectionRegistry _connectionRegistry;

    public GamePresenceEnricher(IGameConnectionRegistry connectionRegistry)
    {
        _connectionRegistry = connectionRegistry;
    }

    public async Task<Game> EnrichAsync(Game game, CancellationToken cancellationToken = default)
    {
        if (game.Id is null || game.Members is null)
        {
            return game;
        }

        var gameId = game.Id.Value;
        var members = new List<GameMember>(game.Members.Count);
        foreach (var member in game.Members)
        {
            var connected = await _connectionRegistry.IsUserConnectedToGameAsync(
                member.UserId,
                gameId,
                cancellationToken);
            members.Add(new GameMember
            {
                Id = member.Id,
                UserId = member.UserId,
                DisplayName = member.DisplayName,
                Role = member.Role,
                TurnCompleted = member.TurnCompleted,
                Eliminated = member.Eliminated,
                VotedForUserId = member.VotedForUserId,
                Connected = connected
            });
        }

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
            ImpostorUserId = game.ImpostorUserId,
            Members = members
        };
    }
}
