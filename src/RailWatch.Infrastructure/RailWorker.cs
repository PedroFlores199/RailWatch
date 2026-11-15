using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;
using RailWatch.Shared;
using StackExchange.Redis;

namespace RailWatch.Infrastructure;

public sealed class RailWorker(IServiceProvider services, IHttpClientFactory clients, IConfiguration config, LiveState state, ILogger<RailWorker> log) : BackgroundService
{
    private long errors;
    private static readonly Meter Meter = new("RailWatch.Worker");
    private static readonly Counter<long> Trains = Meter.CreateCounter<long>("railwatch.trains.processed");
    private static readonly Counter<long> Errors = Meter.CreateCounter<long>("railwatch.feed.errors");
    private static readonly Histogram<double> Latency = Meter.CreateHistogram<double>("railwatch.feed.latency", "ms");
    private readonly HashSet<string> activeAlerts = [];
    private int frame;
    private DateTimeOffset nextSchedule;
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await TickAsync(stoppingToken); }
            catch (Exception e) when (e is not OperationCanceledException) { Errors.Add(1); errors++; log.LogError(e, "Error de ingesta"); }
            try { await Task.Delay(TimeSpan.FromSeconds(config.GetValue("Feed:IntervalSeconds", 25)), stoppingToken); } catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }
    public async Task TickAsync(CancellationToken ct)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RailDb>();
        var watch = Stopwatch.StartNew();
        var mode = config["Feed:Mode"] ?? "Replay";
        string? warning = null;
        byte[] vehicles; byte[] updates;
        if (mode == "Live")
        {
            try
            {
                var client = clients.CreateClient("renfe");
                if (DateTimeOffset.UtcNow >= nextSchedule)
                {
                    await using var zip = new MemoryStream(await client.GetByteArrayAsync(config["Feed:ScheduleUrl"], ct));
                    await scope.ServiceProvider.GetRequiredService<GtfsImporter>().ImportAsync(zip, ct);
                    nextSchedule = DateTimeOffset.UtcNow.AddDays(1);
                }
                vehicles = await client.GetByteArrayAsync(config["Feed:VehiclesUrl"], ct);
                updates = await client.GetByteArrayAsync(config["Feed:UpdatesUrl"], ct);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                Errors.Add(1); errors++; log.LogWarning(e, "Renfe no disponible; reproducción de respaldo");
                warning = "Feed no disponible. Se muestran datos grabados, no circulación actual.";
                mode = "Replay"; (vehicles, updates) = await ReadFrame(ct);
            }
        }
        else (vehicles, updates) = await ReadFrame(ct);
        var snapshot = await scope.ServiceProvider.GetRequiredService<FeedDecoder>().DecodeAsync(vehicles, updates, mode, frame, ct);
        var stale = mode == "Live" && DateTimeOffset.UtcNow - snapshot.ObservedAt > TimeSpan.FromMinutes(2);
        snapshot = snapshot with { Warning = stale ? "El proveedor entrega datos con más de dos minutos de antigüedad." : warning };
        var predictor = scope.ServiceProvider.GetRequiredService<DelayPredictor>();
        snapshot = snapshot with { Trains = snapshot.Trains.Select(t => t with { PredictedDelayMinutes = predictor.Predict(t) }).ToArray() };
        var ids = snapshot.Trains.Select(t => t.Id).ToArray();
        var observations = await db.Observations.Where(x => ids.Contains(x.TrainId) && x.SourceMode == mode && x.ObservedAt >= snapshot.ObservedAt.AddMinutes(-10)).Select(x => new { x.TrainId, x.ObservedAt }).ToListAsync(ct);
        var known = observations.Select(x => (x.TrainId, x.ObservedAt)).ToHashSet();
        db.Observations.AddRange(snapshot.Trains.Where(t => known.Add((t.Id, t.ObservedAt))).Select(t => new DelayObservation { TrainId = t.Id, Line = t.Line, StationId = t.NextStopId, DelaySeconds = t.DelaySeconds, ObservedAt = t.ObservedAt, SourceMode = mode }));
        db.FeedRuns.Add(new() { Mode = mode, Trains = snapshot.Trains.Count, LatencyMs = watch.Elapsed.TotalMilliseconds, Error = warning });
        await db.SaveChangesAsync(ct);
        var mongo = scope.ServiceProvider.GetService<IMongoDatabase>();
        if (mongo is not null)
        {
            var raw = mongo.GetCollection<BsonDocument>("raw_feeds");
            await raw.InsertOneAsync(new BsonDocument { ["capturedAt"] = DateTime.UtcNow, ["mode"] = mode, ["vehiclePositions"] = new BsonBinaryData(vehicles), ["tripUpdates"] = new BsonBinaryData(updates), ["sourceTimestamp"] = snapshot.ObservedAt.UtcDateTime }, cancellationToken: ct);
        }
        snapshot = snapshot with { LatencyMs = watch.Elapsed.TotalMilliseconds, FeedErrors = errors };
        var redis = scope.ServiceProvider.GetService<IConnectionMultiplexer>();
        if (redis is not null)
        {
            var cache = redis.GetDatabase();
            await cache.StringSetAsync("railwatch:snapshot", JsonSerializer.Serialize(snapshot), TimeSpan.FromSeconds(120));
            foreach (var t in snapshot.Trains) await cache.StringSetAsync("railwatch:train:" + t.Id, JsonSerializer.Serialize(t), TimeSpan.FromSeconds(90));
            await redis.GetSubscriber().PublishAsync(RedisChannel.Literal("railwatch:changes"), JsonSerializer.Serialize(snapshot));
        }
        state.Apply(snapshot);
        var rules = await db.AlertRules.Where(x => x.Enabled).AsNoTracking().ToListAsync(ct);
        var currentAlerts = new HashSet<string>();
        foreach (var rule in rules)
            foreach (var train in snapshot.Trains.Where(t => DelayCalculator.IsLate(t.DelaySeconds, rule.ThresholdMinutes) && (rule.Line == null || rule.Line == t.Line) && (rule.StationId == null || rule.StationId == t.NextStopId)))
            {
                var key = rule.Id + ":" + train.Id; currentAlerts.Add(key);
                if (!activeAlerts.Add(key)) continue;
                var alert = new AlertEvent(key, train.Id, train.Line, $"{rule.Name}: {train.Label}, {train.DelaySeconds / 60} min en {train.NextStop}", DateTimeOffset.UtcNow);
                state.Alert(alert);
                if (redis is not null) await redis.GetSubscriber().PublishAsync(RedisChannel.Literal("railwatch:alerts"), JsonSerializer.Serialize(alert));
            }
        activeAlerts.IntersectWith(currentAlerts);
        Trains.Add(snapshot.Trains.Count); Latency.Record(watch.Elapsed.TotalMilliseconds);
        log.LogInformation("{Mode}: {Count} trenes, {Latency:0} ms, fuente {SourceAt}", mode, snapshot.Trains.Count, watch.Elapsed.TotalMilliseconds, snapshot.ObservedAt);
        frame++;
    }
    private async Task<(byte[], byte[])> ReadFrame(CancellationToken ct)
    {
        var directory = config["Feed:RecordingsDirectory"] ?? "samples/recordings";
        var index = frame % Directory.GetFiles(directory, "*-vehicle_positions.pb").Length;
        return (await File.ReadAllBytesAsync(Path.Combine(directory, $"{index:00}-vehicle_positions.pb"), ct), await File.ReadAllBytesAsync(Path.Combine(directory, $"{index:00}-trip_updates.pb"), ct));
    }
}
