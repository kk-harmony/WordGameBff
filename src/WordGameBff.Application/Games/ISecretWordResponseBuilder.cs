using WordGameBff.Domain.Models;

namespace WordGameBff.Application.Games;

public interface ISecretWordResponseBuilder
{
    ClientSecretWordResponse Build(SecretWord upstreamWord, bool includeWordPair);
}
