namespace WordGameBff.Application.Auth;

public interface ICustomAuthTokenService
{
    Task<string> GetServiceTokenAsync(CancellationToken cancellationToken = default);
}
