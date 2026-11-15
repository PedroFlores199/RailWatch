using System.Globalization;
using Microsoft.EntityFrameworkCore;
using RailWatch.Shared;
using TransitRealtime;

namespace RailWatch.Infrastructure;

public sealed class FeedDecoder(RailDb db)
{
    public async Task<FeedSnapshot> DecodeAsync(byte[] vehicles, byte[] updates, string mode, int frame, CancellationToken ct)
    {
        var positions = FeedMessage.Parser.ParseFrom(vehicles);
        var changes = FeedMessage.Parser.ParseFrom(updates);
        var tripUpdates = changes.Entity.Where(x => x.TripUpdate is not null).Select(x => x.TripUpdate).GroupBy(x => x.Trip.TripId).ToDictionary(x => x.Key, x => x.First());
        var tripIds = positions.Entity.Where(x => x.Vehicle?.Trip is not null).Select(x => x.Vehicle.Trip.TripId).Distinct().ToArray();
        var trips = await db.Trips.Include(x => x.Route).Where(x => tripIds.Contains(x.Id)).AsNoTracking().ToDictionaryAsync(x => x.Id, ct);
        var stops = await db.Stations.AsNoTracking().ToDictionaryAsync(x => x.Id, ct);
        var times = await db.StopTimes.Where(x => tripIds.Contains(x.TripId)).AsNoTracking().ToListAsync(ct);
        var result = new List<TrainPosition>();
        foreach (var e in positions.Entity.Where(x => x.Vehicle?.Position is not null))
        {
            var v = e.Vehicle;
            if (v.Position.Latitude is < -90 or > 90 || v.Position.Longitude is < -180 or > 180 || !float.IsFinite(v.Position.Latitude) || !float.IsFinite(v.Position.Longitude)) continue;
            var tripId = v.Trip?.TripId ?? "";
            tripUpdates.TryGetValue(tripId, out var update);
            if (update?.Trip.ScheduleRelationship == TripDescriptor.Types.ScheduleRelationship.Canceled) continue;
            trips.TryGetValue(tripId, out var trip);
            var stopUpdate = update?.StopTimeUpdate.FirstOrDefault(x => x.StopId == v.StopId);
            var timing = stopUpdate?.Arrival ?? stopUpdate?.Departure;
            int? delay = timing?.HasDelay == true ? timing.Delay : update?.HasDelay == true ? update.Delay : null;
            DateTimeOffset? arrival = timing?.HasTime == true ? DateTimeOffset.FromUnixTimeSeconds(timing.Time) : null;
            DateTimeOffset? planned = null;
            var startDate = v.Trip?.StartDate ?? update?.Trip.StartDate;
            var scheduled = times.FirstOrDefault(x => x.TripId == tripId && x.StationId == v.StopId);
            if (scheduled is not null && DateOnly.TryParseExact(startDate, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)) planned = DelayCalculator.ScheduledUtc(day, scheduled.Arrival);
            delay = DelayCalculator.Calculate(delay, arrival, planned);
            var observed = DateTimeOffset.FromUnixTimeSeconds((long)(v.HasTimestamp ? v.Timestamp : positions.Header.Timestamp));
            var label = v.Vehicle?.Label ?? e.Id;
            var line = trip?.Route.Name.Trim() ?? label.Split('-')[0];
            var stopName = stops.TryGetValue(v.StopId, out var station) ? station.Name : "Estación " + v.StopId;
            result.Add(new(v.Vehicle?.Id ?? e.Id, tripId, line, trip?.Route.Category ?? "Cercanías", label, v.Position.Latitude, v.Position.Longitude, delay, v.StopId, stopName, observed, arrival));
        }
        var sourceAt = DateTimeOffset.FromUnixTimeSeconds((long)positions.Header.Timestamp);
        return new(mode, "Renfe Open Data · Cercanías", sourceAt, DateTimeOffset.UtcNow, result, null, frame);
    }
}
