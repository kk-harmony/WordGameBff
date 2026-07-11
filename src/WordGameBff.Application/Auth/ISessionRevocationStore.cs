namespace WordGameBff.Application.Auth;

public interface ISessionRevocationStore
{
    Task RevokeAsync(string sessionId, DateTimeOffset expiresAt, CancellationToken cancellationToken = default);
    Task<bool> IsRevokedAsync(string sessionId, CancellationToken cancellationToken = default);
}
