using Dapper;
using Microsoft.EntityFrameworkCore;
using RailWatch.Shared;

namespace RailWatch.Infrastructure;

public sealed class Analytics(RailDb db)
{
    public async Task<IReadOnlyList<LineStats>> LinesAsync(string mode, DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        if (db.Database.IsNpgsql())
        {
            const string sql = """
                SELECT "Line", COUNT(*)::int AS "Samples",
                  (100.0 * COUNT(*) FILTER (WHERE "DelaySeconds" <= 300) / NULLIF(COUNT("DelaySeconds"),0))::float8 AS "Punctuality",
                  (AVG("DelaySeconds") / 60.0)::float8 AS "AverageDelay",
                  (percentile_cont(0.95) WITHIN GROUP (ORDER BY "DelaySeconds") / 60.0)::float8 AS "P95Delay"
                FROM "Observations"
                WHERE "SourceMode"=@Mode AND "ObservedAt">=@From AND "ObservedAt"<@To AND "DelaySeconds" IS NOT NULL
                GROUP BY "Line" ORDER BY "Punctuality" DESC
                """;
            var connection = db.Database.GetDbConnection();
            return (await connection.QueryAsync<LineStats>(new CommandDefinition(sql, new { Mode = mode, From = from, To = to }, cancellationToken: ct))).ToArray();
        }
        var rows = await db.Observations.AsNoTracking().Where(x => x.SourceMode == mode && x.ObservedAt >= from && x.ObservedAt < to && x.DelaySeconds != null).ToListAsync(ct);
        return rows.GroupBy(x => x.Line).Select(g => new LineStats(g.Key, g.Count(), 100d * g.Count(x => x.DelaySeconds <= 300) / g.Count(), g.Average(x => x.DelaySeconds) / 60, g.OrderBy(x => x.DelaySeconds).ElementAt(Math.Max(0, (int)Math.Ceiling(g.Count() * .95) - 1)).DelaySeconds / 60d)).OrderByDescending(x => x.Punctuality).ToArray();
    }
    public async Task<IReadOnlyList<TrendPoint>> TrendAsync(string mode, DateTimeOffset from, DateTimeOffset to, bool hourly, CancellationToken ct)
    {
        if (db.Database.IsNpgsql())
        {
            var format = hourly ? "HH24" : "YYYY-MM-DD";
            var sql = $"""
                SELECT to_char("ObservedAt" AT TIME ZONE 'Europe/Madrid', '{format}') AS "Period",
                  COUNT(*)::int AS "Samples",
                  (100.0*COUNT(*) FILTER(WHERE "DelaySeconds"<=300)/NULLIF(COUNT("DelaySeconds"),0))::float8 AS "Punctuality",
                  (AVG("DelaySeconds")/60.0)::float8 AS "AverageDelay"
                FROM "Observations" WHERE "SourceMode"=@Mode AND "ObservedAt">=@From AND "ObservedAt"<@To AND "DelaySeconds" IS NOT NULL
                GROUP BY "Period" ORDER BY "Period"
                """;
            return (await db.Database.GetDbConnection().QueryAsync<TrendPoint>(new CommandDefinition(sql, new { Mode = mode, From = from, To = to }, cancellationToken: ct))).ToArray();
        }
        var rows = await db.Observations.Where(x => x.SourceMode == mode && x.ObservedAt >= from && x.ObservedAt < to && x.DelaySeconds != null).AsNoTracking().ToListAsync(ct);
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Madrid");
        return rows.GroupBy(x => TimeZoneInfo.ConvertTime(x.ObservedAt, zone).ToString(hourly ? "HH" : "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)).OrderBy(x => x.Key).Select(g => new TrendPoint(g.Key, g.Count(), 100d * g.Count(x => x.DelaySeconds <= 300) / g.Count(), g.Average(x => x.DelaySeconds) / 60)).ToArray();
    }
    public async Task<object> StationsAsync(string mode, DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        var rows = await db.Observations.Where(x => x.SourceMode == mode && x.ObservedAt >= from && x.ObservedAt < to && x.DelaySeconds != null && x.StationId != null).AsNoTracking().ToListAsync(ct);
        var names = await db.Stations.ToDictionaryAsync(x => x.Id, x => x.Name, ct);
        return rows.GroupBy(x => x.StationId!).Select(g => new { id = g.Key, name = names.GetValueOrDefault(g.Key, g.Key), samples = g.Count(), punctuality = Math.Round(100d * g.Count(x => x.DelaySeconds <= 300) / g.Count(), 1), averageDelay = g.Average(x => x.DelaySeconds) / 60 }).OrderByDescending(x => x.samples).Take(30).ToArray();
    }
    public async Task<object> WorseningAsync(string mode, CancellationToken ct)
    {
        if (!db.Database.IsNpgsql()) return Array.Empty<object>();
        const string sql = """
            WITH changes AS (
              SELECT "TrainId", "Line", "ObservedAt", "DelaySeconds",
                "DelaySeconds" - LAG("DelaySeconds") OVER (PARTITION BY "TrainId" ORDER BY "ObservedAt") AS increase,
                ROW_NUMBER() OVER (PARTITION BY "TrainId" ORDER BY "ObservedAt" DESC) AS rn
              FROM "Observations" WHERE "SourceMode"=@Mode AND "DelaySeconds" IS NOT NULL)
            SELECT "TrainId", "Line", "DelaySeconds", increase FROM changes WHERE rn=1 AND increase>0 ORDER BY increase DESC LIMIT 20
            """;
        return (await db.Database.GetDbConnection().QueryAsync(new CommandDefinition(sql, new { Mode = mode }, cancellationToken: ct))).ToArray();
    }
}
