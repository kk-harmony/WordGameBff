namespace WordGameBff.Domain.Models;

public sealed class ClientSecretWordResponse
{
    public long? Id { get; init; }
    public string? Authentic { get; init; }
    public string? Imposed { get; init; }
}
