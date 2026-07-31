using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using WordGameBff.Application.Realtime;

namespace WordGameBff.Infrastructure.Realtime.SignalR;

[AllowAnonymous]
public sealed class GameHub : Hub
{
    public const string Path = "/hubs/game";
    private static readonly TimeSpan PresenceRefreshInterval = TimeSpan.FromSeconds(60);

    private readonly IGameHubJoinService _hubJoinService;
    private readonly IGameConnectionRegistry _connectionRegistry;
    private readonly ILogger<GameHub> _logger;

    public GameHub(
        IGameHubJoinService hubJoinService,
        IGameConnectionRegistry connectionRegistry,
        ILogger<GameHub> logger)
    {
        _hubJoinService = hubJoinService;
        _connectionRegistry = connectionRegistry;
        _logger = logger;
    }

    public override async Task OnConnectedAsync()
    {
        var started = Stopwatch.GetTimestamp();
        var httpContext = Context.GetHttpContext();
        var gameIdRaw = httpContext?.Request.Query["gameId"].ToString();
        var accessToken = httpContext?.Request.Query["access_token"].ToString();

        if (string.IsNullOrWhiteSpace(gameIdRaw) || !long.TryParse(gameIdRaw, out var gameId) ||
            string.IsNullOrWhiteSpace(accessToken))
        {
            Context.Abort();
            return;
        }

        try
        {
            var joinResult = await _hubJoinService.TryJoinAsync(
                accessToken,
                gameId,
                Context.ConnectionId,
                Context.ConnectionAborted);

            if (joinResult is not HubJoinSuccess success)
            {
                if (joinResult is HubJoinFailure failure)
                {
                    _logger.LogWarning(
                        "Hub connect rejected for game {GameId} connection {ConnectionId}: {Reason} in {ElapsedMs}ms",
                        gameId,
                        Context.ConnectionId,
                        failure.Reason,
                        ElapsedMs(started));
                }

                Context.Abort();
                return;
            }

            Context.Items["userId"] = success.UserId;
            Context.Items["gameId"] = gameId;

            await Groups.AddToGroupAsync(Context.ConnectionId, GetGroupName(gameId));
            await Groups.AddToGroupAsync(Context.ConnectionId, GetUserGroupName(gameId, success.UserId));
            _ = RefreshPresenceLoopAsync(Context.ConnectionId, Context.ConnectionAborted);
            await base.OnConnectedAsync();

            _logger.LogInformation(
                "Hub connected for game {GameId} user {UserId} connection {ConnectionId} in {ElapsedMs}ms",
                gameId,
                success.UserId,
                Context.ConnectionId,
                ElapsedMs(started));
        }
        catch (OperationCanceledException) when (Context.ConnectionAborted.IsCancellationRequested)
        {
            _logger.LogInformation(
                "Hub connect canceled for game {GameId} connection {ConnectionId} after {ElapsedMs}ms",
                gameId,
                Context.ConnectionId,
                ElapsedMs(started));
        }
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var gameId = Context.Items.TryGetValue("gameId", out var gameIdObj) ? gameIdObj : null;
        var userId = Context.Items.TryGetValue("userId", out var userIdObj) ? userIdObj : null;

        if (userId is not null)
        {
            using var cleanupTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try
            {
                await _hubJoinService.LeaveAsync(Context.ConnectionId, cleanupTimeout.Token);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Hub disconnect cleanup failed for game {GameId} user {UserId} connection {ConnectionId}",
                    gameId,
                    userId,
                    Context.ConnectionId);
            }
        }

        if (exception is null)
        {
            _logger.LogInformation(
                "Hub disconnected for game {GameId} user {UserId} connection {ConnectionId} (clean)",
                gameId,
                userId,
                Context.ConnectionId);
        }
        else
        {
            _logger.LogWarning(
                exception,
                "Hub disconnected for game {GameId} user {UserId} connection {ConnectionId} (error)",
                gameId,
                userId,
                Context.ConnectionId);
        }

        await base.OnDisconnectedAsync(exception);
    }

    private static long ElapsedMs(long started) =>
        (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds;

    public static string GetGroupName(long gameId) => $"game:{gameId}";

    public static string GetUserGroupName(long gameId, string userId) => $"game:{gameId}:user:{userId}";

    private async Task RefreshPresenceLoopAsync(string connectionId, CancellationToken cancellationToken)
    {
        try
        {
            using var timer = new PeriodicTimer(PresenceRefreshInterval);
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                await _connectionRegistry.RefreshAsync(connectionId, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Connection closed.
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Presence refresh loop ended for connection {ConnectionId}", connectionId);
        }
    }
}
