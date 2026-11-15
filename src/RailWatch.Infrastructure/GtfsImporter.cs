using System.IO.Compression;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualBasic.FileIO;
using RailWatch.Shared;

namespace RailWatch.Infrastructure;

public sealed class GtfsImporter(RailDb db)
{
    public async Task ImportAsync(Stream stream, CancellationToken ct)
    {
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        if (zip.Entries.Sum(x => x.Length) > 600_000_000) throw new InvalidDataException("GTFS demasiado grande.");
        var routes = Read(zip, "routes.txt").Select(x => new Route { Id = V(x, "route_id"), Name = V(x, "route_short_name"), LongName = V(x, "route_long_name") }).ToArray();
        var stations = Read(zip, "stops.txt").Select(x => new Station { Id = V(x, "stop_id"), Name = V(x, "stop_name"), Latitude = double.Parse(V(x, "stop_lat"), System.Globalization.CultureInfo.InvariantCulture), Longitude = double.Parse(V(x, "stop_lon"), System.Globalization.CultureInfo.InvariantCulture) }).ToArray();
        var trips = Read(zip, "trips.txt").Select(x => new Trip { Id = V(x, "trip_id"), RouteId = V(x, "route_id"), ServiceId = V(x, "service_id"), Headsign = V(x, "trip_headsign") }).ToArray();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.StopTimes.ExecuteDeleteAsync(ct); await db.Trips.ExecuteDeleteAsync(ct); await db.Routes.ExecuteDeleteAsync(ct); await db.Stations.ExecuteDeleteAsync(ct);
        db.Routes.AddRange(routes); db.Stations.AddRange(stations); await db.SaveChangesAsync(ct); db.ChangeTracker.Clear();
        foreach (var chunk in trips.Chunk(2000)) { db.Trips.AddRange(chunk); await db.SaveChangesAsync(ct); db.ChangeTracker.Clear(); }
        var buffer = new List<StopTime>(5000);
        foreach (var row in Read(zip, "stop_times.txt"))
        {
            ct.ThrowIfCancellationRequested();
            var arrival = V(row, "arrival_time"); var departure = V(row, "departure_time");
            if (arrival.Length == 0 || departure.Length == 0) continue;
            _ = DelayCalculator.ParseGtfsTime(arrival);
            buffer.Add(new() { TripId = V(row, "trip_id"), Sequence = int.Parse(V(row, "stop_sequence"), System.Globalization.CultureInfo.InvariantCulture), StationId = V(row, "stop_id"), Arrival = arrival, Departure = departure });
            if (buffer.Count == 5000) { db.StopTimes.AddRange(buffer); await db.SaveChangesAsync(ct); db.ChangeTracker.Clear(); buffer.Clear(); }
        }
        db.StopTimes.AddRange(buffer); await db.SaveChangesAsync(ct); db.ChangeTracker.Clear(); await tx.CommitAsync(ct);
    }
    private static IEnumerable<Dictionary<string, string>> Read(ZipArchive zip, string file)
    {
        var entry = zip.GetEntry(file) ?? throw new InvalidDataException("Falta " + file);
        using var parser = new TextFieldParser(entry.Open()); parser.SetDelimiters(","); parser.HasFieldsEnclosedInQuotes = true;
        var headers = parser.ReadFields() ?? throw new InvalidDataException("Cabecera vacía.");
        while (!parser.EndOfData)
        {
            var fields = parser.ReadFields();
            if (fields is null) continue;
            if (fields.Length != headers.Length) throw new InvalidDataException("Columnas GTFS inconsistentes.");
            yield return headers.Select((h, i) => (h.Trim(), fields[i].Trim())).ToDictionary(x => x.Item1, x => x.Item2);
        }
    }
    private static string V(Dictionary<string, string> row, string key) => row.GetValueOrDefault(key, "");
}
