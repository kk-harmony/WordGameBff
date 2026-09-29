using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WordGameBff.Application.Configuration;
using WordGameBff.Application.Realtime;

namespace WordGameBff.Infrastructure.Realtime.Redis;

public sealed class RedisGameRealtimeBackplane : IGameRealtimeBackplane
{
    internal const string DefaultChannelName = "wordgamebff_backplane";

    private readonly IRedisBackplaneMessaging _messaging;
    private readonly GameRealtimeEnvelopePayloadBuilder _payloadBuilder;
    private readonly BackplaneEnvelopeDispatcher _dispatcher;
    private readonly string _channelName;
    private readonly ILogger<RedisGameRealtimeBackplane> _logger;

    public RedisGameRealtimeBackplane(
        IRedisBackplaneMessaging messaging,
        GameRealtimeEnvelopePayloadBuilder payloadBuilder,
        BackplaneEnvelopeDispatcher dispatcher,
        IOptions<RealtimeOptions> options,
        ILogger<RedisGameRealtimeBackplane> logger)
    {
        _messaging = messaging;
        _payloadBuilder = payloadBuilder;
        _dispatcher = dispatcher;
        _channelName = string.IsNullOrWhiteSpace(options.Value.Backplane.ChannelName)
            ? DefaultChannelName
            : options.Value.Backplane.ChannelName;
        _logger = logger;
    }

    public async Task PublishAsync(long gameId, GameRealtimeEnvelope envelope, CancellationToken cancellationToken = default)
    {
        var started = Stopwatch.GetTimestamp();
        var payload = _payloadBuilder.BuildWirePayload(envelope);
        var payloadBytes = Encoding.UTF8.GetByteCount(payload);

        await _messaging.PublishAsync(_channelName, payload, cancellationToken);
        await _dispatcher.DispatchAsync(payload, cancellationToken);

        _logger.LogInformation(
            "Backplane publish for game {GameId} revision {Revision} action {Action} bytes {PayloadBytes} in {ElapsedMs}ms",
            gameId,
            envelope.Notification.Revision,
            envelope.Notification.Action,
            payloadBytes,
            (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds);
    }
}
