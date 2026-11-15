using Microsoft.EntityFrameworkCore;

namespace RailWatch.Infrastructure;

public sealed class Route
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string LongName { get; set; } = "";
    public string Category { get; set; } = "Cercanías";
}
public sealed class Station
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public double Latitude { get; set; }
    public double Longitude { get; set; }
}
public sealed class Trip
{
    public string Id { get; set; } = "";
    public string RouteId { get; set; } = "";
    public Route Route { get; set; } = null!;
    public string ServiceId { get; set; } = "";
    public string Headsign { get; set; } = "";
}
public sealed class StopTime
{
    public string TripId { get; set; } = "";
    public Trip Trip { get; set; } = null!;
    public int Sequence { get; set; }
    public string StationId { get; set; } = "";
    public Station Station { get; set; } = null!;
    public string Arrival { get; set; } = "";
    public string Departure { get; set; } = "";
}
public sealed class DelayObservation
{
    public long Id { get; set; }
    public string TrainId { get; set; } = "";
    public string Line { get; set; } = "";
    public string? StationId { get; set; }
    public int? DelaySeconds { get; set; }
    public DateTimeOffset ObservedAt { get; set; }
    public string SourceMode { get; set; } = "";
}
public sealed class AlertRule
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string? Line { get; set; }
    public string? StationId { get; set; }
    public int ThresholdMinutes { get; set; } = 15;
    public bool Enabled { get; set; } = true;
}
public sealed class FeedRun
{
    public long Id { get; set; }
    public string Mode { get; set; } = "";
    public DateTimeOffset At { get; set; } = DateTimeOffset.UtcNow;
    public int Trains { get; set; }
    public double LatencyMs { get; set; }
    public string? Error { get; set; }
}
public sealed class RailDb(DbContextOptions<RailDb> options) : DbContext(options)
{
    public DbSet<Route> Routes => Set<Route>();
    public DbSet<Station> Stations => Set<Station>();
    public DbSet<Trip> Trips => Set<Trip>();
    public DbSet<StopTime> StopTimes => Set<StopTime>();
    public DbSet<DelayObservation> Observations => Set<DelayObservation>();
    public DbSet<AlertRule> AlertRules => Set<AlertRule>();
    public DbSet<FeedRun> FeedRuns => Set<FeedRun>();
    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<StopTime>().HasKey(x => new { x.TripId, x.Sequence });
        b.Entity<DelayObservation>().HasIndex(x => new { x.TrainId, x.ObservedAt, x.SourceMode }).IsUnique();
        b.Entity<DelayObservation>().HasIndex(x => new { x.SourceMode, x.Line, x.ObservedAt });
        b.Entity<DelayObservation>().HasIndex(x => new { x.SourceMode, x.StationId, x.ObservedAt });
        if (Database.IsSqlite()) foreach (var e in b.Model.GetEntityTypes()) foreach (var p in e.GetProperties().Where(p => p.ClrType == typeof(DateTimeOffset))) p.SetValueConverter(new Microsoft.EntityFrameworkCore.Storage.ValueConversion.DateTimeOffsetToBinaryConverter());
    }
}
