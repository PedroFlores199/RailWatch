using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using MongoDB.Driver;
using StackExchange.Redis;

namespace RailWatch.Infrastructure;

public static class Setup
{
    public static IServiceCollection AddRailWatch(this IServiceCollection services, IConfiguration configuration)
    {
        var local = configuration["Storage:Mode"] == "Local";
        services.AddDbContext<RailDb>(o => { if (local) o.UseSqlite(configuration.GetConnectionString("Sqlite") ?? "Data Source=railwatch.db"); else o.UseNpgsql(configuration.GetConnectionString("Postgres")); });
        services.AddSingleton<LiveState>(); services.AddSingleton<DelayPredictor>();
        services.AddScoped<GtfsImporter>(); services.AddScoped<FeedDecoder>(); services.AddScoped<Analytics>();
        services.AddHttpClient("renfe", c => { c.Timeout = TimeSpan.FromMinutes(3); c.DefaultRequestHeaders.UserAgent.ParseAdd("RailWatch-Portfolio/1.0"); }).AddStandardResilienceHandler(o => { o.TotalRequestTimeout.Timeout = TimeSpan.FromMinutes(3); o.AttemptTimeout.Timeout = TimeSpan.FromSeconds(50); o.Retry.MaxRetryAttempts = 2; o.CircuitBreaker.SamplingDuration = TimeSpan.FromMinutes(2); });
        if (!local)
        {
            services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(configuration.GetConnectionString("Redis")!));
            services.AddSingleton<IMongoDatabase>(_ => new MongoClient(configuration.GetConnectionString("Mongo")).GetDatabase("railwatch"));
        }
        return services;
    }
    public static async Task InitializeAsync(IServiceProvider services, IConfiguration config, CancellationToken ct = default)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RailDb>();
        await db.Database.EnsureCreatedAsync(ct);
        if (!await db.Stations.AnyAsync(ct))
        {
            var sample = config["Feed:SampleSchedule"] ?? "samples/schedule.zip";
            await using var stream = File.OpenRead(sample);
            await scope.ServiceProvider.GetRequiredService<GtfsImporter>().ImportAsync(stream, ct);
        }
        if (!await db.AlertRules.AnyAsync(ct)) { db.AlertRules.Add(new() { Name = "Retraso superior a 15 minutos", ThresholdMinutes = 15 }); await db.SaveChangesAsync(ct); }
        var mongo = scope.ServiceProvider.GetService<IMongoDatabase>();
        if (mongo is not null) await mongo.GetCollection<BsonDocument>("raw_feeds").Indexes.CreateOneAsync(new CreateIndexModel<BsonDocument>(Builders<BsonDocument>.IndexKeys.Ascending("capturedAt"), new CreateIndexOptions { ExpireAfter = TimeSpan.FromDays(7) }), cancellationToken: ct);
    }
}
