using WordGameBff.Domain.Models;

namespace WordGameBff.Application.Games;

public interface ISecretWordAccessPolicy
{
    bool CanViewWordPair(string userId, Game game);
}
