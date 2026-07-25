using WordGameBff.Domain.Models;

namespace WordGameBff.Application.Games;

public static class GameMembership
{
    public static bool IsMember(string userId, Game game) =>
        string.Equals(game.AdminUserId, userId, StringComparison.Ordinal)
        || (game.Members?.Any(m => string.Equals(m.UserId, userId, StringComparison.Ordinal)) ?? false);

    public static IEnumerable<string> ViewerUserIds(Game game)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        if (!string.IsNullOrWhiteSpace(game.AdminUserId) && seen.Add(game.AdminUserId))
        {
            yield return game.AdminUserId;
        }

        if (game.Members is null)
        {
            yield break;
        }

        foreach (var member in game.Members)
        {
            if (!string.IsNullOrWhiteSpace(member.UserId) && seen.Add(member.UserId))
            {
                yield return member.UserId;
            }
        }
    }
}
