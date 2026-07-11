namespace WordGameBff.Application.Auth;

public sealed class UpstreamAuthException : Exception
{
    public UpstreamAuthException(string message)
        : base(message)
    {
    }
}
