using WordGameBff.Domain.Models;

namespace WordGameBff.Application.Auth;

public interface ISessionTokenService
{
    SessionTokenResult CreateToken(string userId);
    Task<(bool IsValid, string? UserId, string? SessionId)> ValidateTokenAsync(
        string token,
        CancellationToken cancellationToken = default);
}
