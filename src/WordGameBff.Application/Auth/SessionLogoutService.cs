using System.IdentityModel.Tokens.Jwt;
using WordGameBff.Application.Games;

namespace WordGameBff.Application.Auth;

public interface ISessionLogoutService
{
    Task<AppOutcome> LogoutAsync(string? authorizationHeader, CancellationToken cancellationToken = default);
}

public sealed class SessionLogoutService : ISessionLogoutService
{
    private readonly ISessionTokenService _sessionTokenService;
    private readonly ISessionRevocationStore _revocationStore;

    public SessionLogoutService(
        ISessionTokenService sessionTokenService,
        ISessionRevocationStore revocationStore)
    {
        _sessionTokenService = sessionTokenService;
        _revocationStore = revocationStore;
    }

    public async Task<AppOutcome> LogoutAsync(
        string? authorizationHeader,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(authorizationHeader) ||
            !authorizationHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return AppOutcomes.Fail("UNAUTHORIZED", "Missing bearer token.", AppFailureKind.Unauthorized);
        }

        var token = authorizationHeader["Bearer ".Length..];
        var validation = await _sessionTokenService.ValidateTokenAsync(token, cancellationToken);
        if (!validation.IsValid || string.IsNullOrEmpty(validation.SessionId))
        {
            return AppOutcomes.Fail("UNAUTHORIZED", "Invalid session token.", AppFailureKind.Unauthorized);
        }

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        var expiresAt = jwt.ValidTo == DateTime.MinValue
            ? DateTimeOffset.UtcNow.AddHours(1)
            : new DateTimeOffset(jwt.ValidTo, TimeSpan.Zero);

        await _revocationStore.RevokeAsync(validation.SessionId, expiresAt, cancellationToken);
        return AppOutcomes.NoContent();
    }
}
