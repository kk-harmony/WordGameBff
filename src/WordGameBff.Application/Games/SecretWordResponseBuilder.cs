using WordGameBff.Domain.Models;

namespace WordGameBff.Application.Games;

public sealed class SecretWordResponseBuilder : ISecretWordResponseBuilder
{
    public ClientSecretWordResponse Build(SecretWord upstreamWord, bool includeWordPair) =>
        includeWordPair
            ? new ClientSecretWordResponse
            {
                Id = upstreamWord.Id,
                Authentic = upstreamWord.Authentic,
                Imposed = upstreamWord.Imposed
            }
            : new ClientSecretWordResponse
            {
                Id = upstreamWord.Id
            };
}
