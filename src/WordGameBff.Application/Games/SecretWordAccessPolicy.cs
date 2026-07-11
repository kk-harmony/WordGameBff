using WordGameBff.Domain.Models;

namespace WordGameBff.Application.Games;

public sealed class SecretWordAccessPolicy : ISecretWordAccessPolicy
{
    public bool CanViewWordPair(string userId, Game game) =>
        string.Equals(game.AdminUserId, userId, StringComparison.Ordinal)
        && string.Equals(game.Status, "WAITING", StringComparison.OrdinalIgnoreCase);
}
