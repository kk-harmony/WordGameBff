namespace WordGameBff.Application.Games;

public static class GameRules
{
    /// <summary>
    /// Minimum members required before an admin can remove an offline player mid-game.
    /// Matches the frontend KICK_MIN_MEMBERS constant.
    /// </summary>
    public const int MinMembersForAdminRemoval = 3;

    public static string InsufficientMembersForRemovalMessage =>
        $"Removal is only allowed when at least {MinMembersForAdminRemoval} players are in the game.";
}
