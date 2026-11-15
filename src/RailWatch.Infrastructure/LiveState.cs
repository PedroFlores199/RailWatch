using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RailWatch.Shared;
using StackExchange.Redis;

namespace RailWatch.Infrastructure;

public sealed class LiveState
{
    private FeedSnapshot snapshot = new("Waiting", "Esperando al worker", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, []);
    public FeedSnapshot Snapshot => Volatile.Read(ref snapshot);
    public event Action<FeedSnapshot>? Updated;
    public event Action<AlertEvent>? AlertRaised;
    public void Apply(FeedSnapshot value) { Interlocked.Exchange(ref snapshot, value); Updated?.Invoke(value); }
    public void Alert(AlertEvent value) => AlertRaised?.Invoke(value);
}
public sealed class RedisRelay(IConnectionMultiplexer redis, LiveState state, ILogger<RedisRelay> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var subscriber = redis.GetSubscriber();
        var channel = RedisChannel.Literal("railwatch:changes");
        await subscriber.SubscribeAsync(channel, (_, value) =>
        {
            try { var snapshot = JsonSerializer.Deserialize<FeedSnapshot>(value.ToString()); if (snapshot is not null) state.Apply(snapshot); }
            catch (JsonException e) { logger.LogWarning(e, "Snapshot Redis inválido"); }
        });
        await subscriber.SubscribeAsync(RedisChannel.Literal("railwatch:alerts"), (_, value) =>
        {
            try { var alert = JsonSerializer.Deserialize<AlertEvent>(value.ToString()); if (alert is not null) state.Alert(alert); }
            catch (JsonException e) { logger.LogWarning(e, "Alerta Redis inválida"); }
        });
        var last = await redis.GetDatabase().StringGetAsync("railwatch:snapshot");
        if (last.HasValue && JsonSerializer.Deserialize<FeedSnapshot>(last.ToString()) is { } initial) state.Apply(initial);
        try { await Task.Delay(Timeout.Infinite, stoppingToken); } catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        await subscriber.UnsubscribeAsync(channel);
        await subscriber.UnsubscribeAsync(RedisChannel.Literal("railwatch:alerts"));
    }
}
