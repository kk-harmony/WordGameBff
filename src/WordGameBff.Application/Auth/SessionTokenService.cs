using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using WordGameBff.Application.Configuration;
using WordGameBff.Domain.Models;

namespace WordGameBff.Application.Auth;

public sealed class SessionTokenService : ISessionTokenService
{
    private readonly SessionOptions _options;
    private readonly ISessionRevocationStore _revocationStore;
    private readonly SymmetricSecurityKey _signingKey;

    public SessionTokenService(
        IOptions<SessionOptions> options,
        ISessionRevocationStore revocationStore)
    {
        _options = options.Value;
        _revocationStore = revocationStore;
        _signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey));
    }

    public SessionTokenResult CreateToken(string userId)
    {
        var sessionId = Guid.NewGuid().ToString("N");
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(_options.ExpiryMinutes);

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Issuer,
            claims:
            [
                new Claim(JwtRegisteredClaimNames.Sub, userId),
                new Claim("sid", sessionId)
            ],
            expires: expiresAt.UtcDateTime,
            signingCredentials: new SigningCredentials(_signingKey, SecurityAlgorithms.HmacSha256));

        return new SessionTokenResult
        {
            SessionToken = new JwtSecurityTokenHandler().WriteToken(token),
            UserId = userId,
            ExpiresAt = expiresAt
        };
    }

    public async Task<(bool IsValid, string? UserId, string? SessionId)> ValidateTokenAsync(
        string token,
        CancellationToken cancellationToken = default)
    {
        var handler = new JwtSecurityTokenHandler();
        try
        {
            var principal = handler.ValidateToken(token, new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = _options.Issuer,
                ValidateAudience = true,
                ValidAudience = _options.Issuer,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = _signingKey,
                ClockSkew = TimeSpan.FromSeconds(30)
            }, out _);

            var userId = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value
                         ?? principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
            var sessionId = principal.FindFirst("sid")?.Value;

            if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(sessionId))
            {
                return (false, null, null);
            }

            if (await _revocationStore.IsRevokedAsync(sessionId, cancellationToken))
            {
                return (false, null, null);
            }

            return (true, userId, sessionId);
        }
        catch
        {
            return (false, null, null);
        }
    }
}
