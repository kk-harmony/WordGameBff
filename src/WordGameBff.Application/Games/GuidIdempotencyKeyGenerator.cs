namespace WordGameBff.Application.Games;

public sealed class GuidIdempotencyKeyGenerator : IIdempotencyKeyGenerator
{
    public string CreateKey() => Guid.NewGuid().ToString("N");
}
