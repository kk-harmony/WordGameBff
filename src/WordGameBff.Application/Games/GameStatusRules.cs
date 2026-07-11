namespace WordGameBff.Application.Games;

public static class GameStatusRules
{
    public static bool IsFinished(string? status) =>
        string.Equals(status, "FINISHED", StringComparison.OrdinalIgnoreCase)
        || string.Equals(status, "COMPLETED", StringComparison.OrdinalIgnoreCase)
        || string.Equals(status, "ENDED", StringComparison.OrdinalIgnoreCase);

    public static bool IsVoting(string? status) =>
        string.Equals(status, "VOTING", StringComparison.OrdinalIgnoreCase)
        || string.Equals(status, "VOTE", StringComparison.OrdinalIgnoreCase);
}
