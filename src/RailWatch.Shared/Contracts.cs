using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace RailWatch.Shared;

public sealed record TrainPosition(string Id, string TripId, string Line, string Category, string Label, double Latitude, double Longitude, int? DelaySeconds, string? NextStopId, string NextStop, DateTimeOffset ObservedAt, DateTimeOffset? Arrival, float? PredictedDelayMinutes = null);
public sealed record FeedSnapshot(string Mode, string Source, DateTimeOffset ObservedAt, DateTimeOffset PublishedAt, IReadOnlyList<TrainPosition> Trains, string? Warning = null, int Frame = 0, double LatencyMs = 0, long FeedErrors = 0);
public sealed record StationDto(string Id, string Name, double Latitude, double Longitude);
public sealed record LineStats(string Line, int Samples, double? Punctuality, double? AverageDelay, double? P95Delay);
public sealed record TrendPoint(string Period, int Samples, double? Punctuality, double? AverageDelay);
public sealed record AlertRuleDto(Guid Id, string Name, string? Line, string? StationId, int ThresholdMinutes, bool Enabled);
public sealed record AlertEvent(string Key, string TrainId, string Line, string Message, DateTimeOffset At);
public sealed record DashboardSummary(int Trains, int Lines, int KnownDelays, double? Punctuality, int Delayed, DateTimeOffset UpdatedAt);
public static class DelayCalculator
{
    public static int? Calculate(int? explicitDelay, DateTimeOffset? actual, DateTimeOffset? scheduled) => explicitDelay ?? (actual.HasValue && scheduled.HasValue ? checked((int)Math.Round((actual.Value - scheduled.Value).TotalSeconds)) : null);
    public static bool IsLate(int? delaySeconds, int thresholdMinutes = 15) => delaySeconds.HasValue && delaySeconds.Value > thresholdMinutes * 60;
    public static TimeSpan ParseGtfsTime(string value)
    {
        var p = value.Split(':');
        if (p.Length != 3 || !int.TryParse(p[0], out var h) || !int.TryParse(p[1], out var m) || !int.TryParse(p[2], out var s) || h is < 0 or > 72 || m is < 0 or > 59 || s is < 0 or > 59) throw new FormatException("Hora GTFS inválida.");
        return TimeSpan.FromSeconds(h * 3600 + m * 60 + s);
    }
    public static DateTimeOffset ScheduledUtc(DateOnly date, string time)
    {
        var local = date.ToDateTime(TimeOnly.MinValue).Add(ParseGtfsTime(time));
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Madrid");
        return new DateTimeOffset(local, zone.GetUtcOffset(local)).ToUniversalTime();
    }
}
public sealed partial class ControlRoomViewModel : ObservableObject
{
    [ObservableProperty] private string status = "Conectando…";
    [ObservableProperty] private string filter = "";
    [ObservableProperty] private bool soundEnabled;
    [ObservableProperty] private double? punctuality;
    [ObservableProperty] private int trainCount;
    public ObservableCollection<TrainPosition> Trains { get; } = [];
    public ObservableCollection<AlertEvent> Alerts { get; } = [];
    public ObservableCollection<LineStats> Lines { get; } = [];
    public event Action? SoundRequested;
    private readonly HashSet<string> alerted = [];
    private FeedSnapshot? last;
    public void Apply(FeedSnapshot snapshot)
    {
        last = snapshot;
        Status = $"{snapshot.Mode} · {snapshot.PublishedAt.ToLocalTime():HH:mm:ss} · {snapshot.Trains.Count} trenes";
        TrainCount = snapshot.Trains.Count;
        var known = snapshot.Trains.Where(x => x.DelaySeconds.HasValue).ToArray();
        Punctuality = known.Length == 0 ? null : Math.Round(100d * known.Count(x => x.DelaySeconds <= 300) / known.Length, 1);
        Trains.Clear();
        foreach (var train in snapshot.Trains.Where(x => string.IsNullOrWhiteSpace(Filter) || (x.Line + " " + x.Label + " " + x.NextStop).Contains(Filter, StringComparison.OrdinalIgnoreCase)).OrderByDescending(x => x.DelaySeconds)) Trains.Add(train);
        foreach (var t in snapshot.Trains.Where(t => DelayCalculator.IsLate(t.DelaySeconds)))
        {
            if (!alerted.Add(t.Id)) continue;
            Alerts.Insert(0, new(t.Id, t.Id, t.Line, $"{t.Label}: {t.DelaySeconds / 60} min de retraso", snapshot.PublishedAt));
            if (SoundEnabled) SoundRequested?.Invoke();
        }
        alerted.RemoveWhere(id => snapshot.Trains.All(t => t.Id != id || !DelayCalculator.IsLate(t.DelaySeconds)));
        while (Alerts.Count > 100) Alerts.RemoveAt(Alerts.Count - 1);
    }
    partial void OnFilterChanged(string value) { if (last is not null) Apply(last); }
}
