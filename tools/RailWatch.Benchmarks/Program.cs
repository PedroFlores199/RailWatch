using System.Diagnostics;
using System.Text.Json;
using Dapper;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;
using RailWatch.Infrastructure;

var connection=Environment.GetEnvironmentVariable("BENCHMARK_POSTGRES")??throw new InvalidOperationException("Configura BENCHMARK_POSTGRES apuntando a una base de benchmark vacía.");
await using var db=new RailDb(new DbContextOptionsBuilder<RailDb>().UseNpgsql(connection).Options);await db.Database.EnsureCreatedAsync();if(await db.Observations.AnyAsync())throw new InvalidOperationException("La base debe estar vacía.");
await using var sql=new NpgsqlConnection(connection);await sql.OpenAsync();
await using(var copy=await sql.BeginBinaryImportAsync("COPY \"Observations\" (\"TrainId\",\"Line\",\"StationId\",\"DelaySeconds\",\"ObservedAt\",\"SourceMode\") FROM STDIN (FORMAT BINARY)"))
{
    var now=DateTimeOffset.UtcNow;for(var i=0;i<100000;i++){await copy.StartRowAsync();await copy.WriteAsync("T"+(i%1000),NpgsqlDbType.Text);await copy.WriteAsync("C"+(i%12),NpgsqlDbType.Text);await copy.WriteAsync("S"+(i%100),NpgsqlDbType.Text);await copy.WriteAsync(i%1800,NpgsqlDbType.Integer);await copy.WriteAsync(now.AddSeconds(i),NpgsqlDbType.TimestampTz);await copy.WriteAsync("SyntheticBenchmark",NpgsqlDbType.Text);}await copy.CompleteAsync();
}
var efTimes=new List<double>();var dapperTimes=new List<double>();var watch=new Stopwatch();
for(var i=0;i<31;i++)
{
    watch.Restart();var ef=await db.Observations.AsNoTracking().Where(x=>x.SourceMode=="SyntheticBenchmark").GroupBy(x=>x.Line).Select(g=>new{Line=g.Key,Samples=g.Count(),Average=g.Average(x=>x.DelaySeconds)}).OrderBy(x=>x.Line).ToArrayAsync();watch.Stop();if(i>0)efTimes.Add(watch.Elapsed.TotalMilliseconds);
    watch.Restart();var dp=(await sql.QueryAsync<Aggregate>("SELECT \"Line\", COUNT(*)::int AS \"Samples\", AVG(\"DelaySeconds\")::float8 AS \"Average\" FROM \"Observations\" WHERE \"SourceMode\"=@Mode GROUP BY \"Line\" ORDER BY \"Line\"",new{Mode="SyntheticBenchmark"})).ToArray();watch.Stop();if(i>0)dapperTimes.Add(watch.Elapsed.TotalMilliseconds);
    if(ef.Length!=dp.Length||ef.Where((x,j)=>x.Samples!=dp[j].Samples||Math.Abs((x.Average??0)-(dp[j].Average??0))>.001).Any())throw new InvalidOperationException("Los resultados no coinciden.");
}
Console.WriteLine(JsonSerializer.Serialize(new{rows=100000,repetitions=30,efMedianMs=efTimes.Order().ElementAt(15),dapperMedianMs=dapperTimes.Order().ElementAt(15),resultsVerified=true,at=DateTimeOffset.UtcNow,notes="Datos sintéticos en base aislada. Misma agregación GROUP BY; una ronda de calentamiento excluida. No mide las consultas con percentiles del panel."},new JsonSerializerOptions{WriteIndented=true}));
public sealed record Aggregate(string Line,int Samples,double? Average);
