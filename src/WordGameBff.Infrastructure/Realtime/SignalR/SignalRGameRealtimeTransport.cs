using Microsoft.AspNetCore.SignalR;
using WordGameBff.Application.Realtime;

namespace WordGameBff.Infrastructure.Realtime.SignalR;

public sealed class SignalRGameRealtimeTransport : IGameRealtimeTransport
{
    public const string ReceiveMethod = "gameEvent";

    private readonly IHubContext<GameHub> _hubContext;

    public SignalRGameRealtimeTransport(IHubContext<GameHub> hubContext)
    {
        _hubContext = hubContext;
    }

    public Task PublishToGameAsync(long gameId, GameRealtimeMessage message, CancellationToken cancellationToken = default) =>
        _hubContext.Clients.Group(GameHub.GetGroupName(gameId)).SendAsync(ReceiveMethod, message, cancellationToken);
}
