namespace WordGameBff.Application.Games;

public interface IIdempotencyKeyGenerator
{
    string CreateKey();
}
