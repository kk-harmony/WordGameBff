namespace WordGameBff.Application.Games;

public interface IUpstreamErrorNormalizer
{
    /// <summary>
    /// Maps upstream error JSON ({ type, message }) to BFF client format ({ error, message }).
    /// Returns the original body when it is not a recognized error envelope.
    /// </summary>
    string NormalizeErrorBody(string? responseBody);
}
