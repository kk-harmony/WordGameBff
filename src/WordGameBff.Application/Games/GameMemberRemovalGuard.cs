using WordGameBff.Application.Realtime;
using WordGameBff.Domain.Models;

namespace WordGameBff.Application.Games;

public static class GameMemberRemovalGuard
{
    public sealed record ValidationResult(bool Allowed, string? ErrorCode, string? Message);

    public static async Task<ValidationResult> ValidateAdminRemovalAsync(
        Game game,
        string adminUserId,
        string targetUserId,
        IGameConnectionRegistry connectionRegistry,
        CancellationToken cancellationToken = default)
    {
        if (!string.Equals(game.AdminUserId, adminUserId, StringComparison.Ordinal))
        {
            return Deny("FORBIDDEN", "Only the game admin can remove other players.");
        }

        if (string.Equals(game.Status, "WAITING", StringComparison.OrdinalIgnoreCase))
        {
            return Deny("GAME_NOT_STARTED", "Cannot remove other players before the game has started.");
        }

        if (game.Members is null || game.Members.Count < GameRules.MinMembersForAdminRemoval)
        {
            return Deny("INSUFFICIENT_MEMBERS", GameRules.InsufficientMembersForRemovalMessage);
        }

        if (string.Equals(targetUserId, game.AdminUserId, StringComparison.Ordinal))
        {
            return Deny("CANNOT_REMOVE_ADMIN", "Cannot remove the game admin.");
        }

        if (game.Members.All(member => !string.Equals(member.UserId, targetUserId, StringComparison.Ordinal)))
        {
            return Deny("NOT_FOUND", "User is not a member of this game.");
        }

        if (game.Id is null)
        {
            return Deny("NOT_FOUND", "Game not found.");
        }

        if (await connectionRegistry.IsUserConnectedToGameAsync(targetUserId, game.Id.Value, cancellationToken))
        {
            return Deny("PLAYER_CONNECTED", "Cannot remove a player who is still connected.");
        }

        return Allow();
    }

    private static ValidationResult Allow() => new(true, null, null);

    private static ValidationResult Deny(string errorCode, string message) => new(false, errorCode, message);

    /// <summary>
    /// Maps legacy upstream kick errors (e.g. old 5-player threshold) to the current BFF message.
    /// </summary>
    public static bool IsLegacyInsufficientMembersError(string? responseBody) =>
        responseBody is not null && (
            responseBody.Contains("more than 4 players", StringComparison.OrdinalIgnoreCase) ||
            responseBody.Contains("Kick is only allowed", StringComparison.OrdinalIgnoreCase) ||
            responseBody.Contains("INSUFFICIENT_MEMBERS", StringComparison.OrdinalIgnoreCase)
                && responseBody.Contains("more than 4", StringComparison.OrdinalIgnoreCase));
}
