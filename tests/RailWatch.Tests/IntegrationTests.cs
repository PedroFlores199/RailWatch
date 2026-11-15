using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using RailWatch.Infrastructure;
using Testcontainers.PostgreSql;
using Xunit;

namespace RailWatch.Tests;

public static class Samples
{
    public static string DirectoryPath
    {
        get { var p = new DirectoryInfo(AppContext.BaseDirectory); while (p is not null && !Directory.Exists(Path.Combine(p.FullName, "samples"))) p = p.Parent; return Path.Combine(p?.FullName ?? throw new DirectoryNotFoundException(), "samples"); }
    }
}
public sealed class RailFactory : WebApplicationFactory<Program>
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "railwatch-tests-" + Guid.NewGuid());
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        Directory.CreateDirectory(directory);
        builder.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?> { { "Storage:Mode", "Local" }, { "ConnectionStrings:Sqlite", "Data Source=" + Path.Combine(directory, "test.db") + ";Pooling=False" }, { "Feed:SampleSchedule", Path.Combine(Samples.DirectoryPath, "schedule.zip") }, { "Feed:RecordingsDirectory", Path.Combine(Samples.DirectoryPath, "recordings") }, { "Feed:EmbeddedWorker", "true" } }));
    }
    protected override void Dispose(bool disposing) { base.Dispose(disposing); if (disposing && Directory.Exists(directory)) Directory.Delete(directory, true); }
}
public sealed class ApiTests(RailFactory factory) : IClassFixture<RailFactory>
{
    [Fact] public async Task SnapshotEndpointLabelsSource() { var c = factory.CreateClient(); for (var i = 0; i < 50; i++) { var json = JsonDocument.Parse(await c.GetStringAsync("/api/snapshot")); if (json.RootElement.GetProperty("mode").GetString() == "Replay") { json.RootElement.GetProperty("trains").GetArrayLength().Should().BeGreaterThan(0); return; } await Task.Delay(100); } throw new TimeoutException("No se recibió el snapshot."); }
    [Fact] public async Task ExportIsAnExcelArchive() { var bytes = await factory.CreateClient().GetByteArrayAsync("/api/export.xlsx"); bytes.Take(2).Should().Equal((byte)'P', (byte)'K'); }
    [Fact] public async Task InvalidAlertRuleIsRejected() { var r = await factory.CreateClient().PostAsync("/api/alerts/rules", new StringContent("{\"id\":\"00000000-0000-0000-0000-000000000000\",\"name\":\"\",\"thresholdMinutes\":0,\"enabled\":true}", System.Text.Encoding.UTF8, "application/json")); r.StatusCode.Should().Be(System.Net.HttpStatusCode.BadRequest); }
}
public sealed class DecoderTests
{
    [Fact]
    public async Task RecordedProtobufDecodesAndJoinsSchedule()
    {
        await using var db = new RailDb(new DbContextOptionsBuilder<RailDb>().UseSqlite("Data Source=:memory:").Options); await db.Database.OpenConnectionAsync(); await db.Database.EnsureCreatedAsync();
        await using var zip = File.OpenRead(Path.Combine(Samples.DirectoryPath, "schedule.zip")); await new GtfsImporter(db).ImportAsync(zip, default);
        var vehicles = await File.ReadAllBytesAsync(Path.Combine(Samples.DirectoryPath, "recordings", "00-vehicle_positions.pb")); var updates = await File.ReadAllBytesAsync(Path.Combine(Samples.DirectoryPath, "recordings", "00-trip_updates.pb"));
        var snapshot = await new FeedDecoder(db).DecodeAsync(vehicles, updates, "Replay", 0, default);
        snapshot.Mode.Should().Be("Replay"); snapshot.Trains.Count.Should().BeGreaterThan(100); snapshot.Trains.Should().OnlyContain(t => t.Latitude >= -90 && t.Latitude <= 90); snapshot.Trains.Should().Contain(t => t.NextStop.Contains("Atocha"));
    }
}
public sealed class ContainerFactAttribute : FactAttribute
{
    public ContainerFactAttribute() { if (Environment.GetEnvironmentVariable("RUN_CONTAINER_TESTS") != "1") Skip = "Activa RUN_CONTAINER_TESTS=1 para probar PostgreSQL en Docker."; }
}
public sealed class PostgresTests
{
    [ContainerFact]
    public async Task DapperAggregatesAndWindowFunctionsMatchKnownObservations()
    {
        await using var server = new PostgreSqlBuilder("postgres:17-alpine").Build(); await server.StartAsync();
        await using var db = new RailDb(new DbContextOptionsBuilder<RailDb>().UseNpgsql(server.GetConnectionString()).Options); await db.Database.EnsureCreatedAsync(); var now = DateTimeOffset.UtcNow;
        db.Observations.AddRange(new DelayObservation { TrainId = "T1", Line = "C1", DelaySeconds = 0, SourceMode = "Live", ObservedAt = now.AddMinutes(-1) }, new DelayObservation { TrainId = "T1", Line = "C1", DelaySeconds = 1200, SourceMode = "Live", ObservedAt = now }, new DelayObservation { TrainId = "T2", Line = "C1", DelaySeconds = 0, SourceMode = "Replay", ObservedAt = now }); await db.SaveChangesAsync();
        var analytics = new Analytics(db); var result = await analytics.LinesAsync("Live", now.AddHours(-1), now.AddHours(1), default); result.Should().ContainSingle(); result[0].Punctuality.Should().Be(50); result[0].AverageDelay.Should().Be(10); result[0].Samples.Should().Be(2);
        var trend = await analytics.TrendAsync("Live", now.AddHours(-1), now.AddHours(1), true, default); trend.Should().NotBeEmpty(); var worsening = await analytics.WorseningAsync("Live", default); worsening.Should().NotBeNull();
    }
}
