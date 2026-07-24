using WordGameBff.Application.Realtime;

namespace WordGameBff.Infrastructure.Realtime;

public sealed class InMemoryGameRealtimeBackplane : IGameRealtimeBackplane
{
    private readonly IGameRealtimeTransport _transport;

    public InMemoryGameRealtimeBackplane(IGameRealtimeTransport transport)
    {
        _transport = transport;
    }

    public Task PublishAsync(long gameId, GameRealtimeMessage message, CancellationToken cancellationToken = default) =>
        _transport.PublishToGameAsync(gameId, message, cancellationToken);
}
