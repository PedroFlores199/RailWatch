using ClosedXML.Excel;
using System.Diagnostics.Metrics;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using OpenTelemetry.Metrics;
using RailWatch.Infrastructure;
using RailWatch.Shared;
using RailWatch.Web.Blazor;
using Serilog;

System.Globalization.CultureInfo.DefaultThreadCurrentCulture = new("es-ES");
System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = new("es-ES");
var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseStaticWebAssets();
builder.Host.UseSerilog((_, c) => c.MinimumLevel.Information().MinimumLevel.Override("Microsoft", Serilog.Events.LogEventLevel.Warning).WriteTo.Console());
builder.Services.AddRailWatch(builder.Configuration);
builder.Services.AddSignalR(); builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddEndpointsApiExplorer(); builder.Services.AddSwaggerGen(); builder.Services.AddProblemDetails();
builder.Services.AddOpenTelemetry().WithMetrics(m => m.AddMeter("RailWatch").AddAspNetCoreInstrumentation().AddHttpClientInstrumentation().AddPrometheusExporter());
if (builder.Configuration.GetValue("Feed:EmbeddedWorker", true)) builder.Services.AddHostedService<RailWorker>();
else builder.Services.AddHostedService<RedisRelay>();
builder.Services.AddHostedService<HubRelay>();
var app = builder.Build();
app.UseExceptionHandler(); app.UseSerilogRequestLogging(); app.UseStaticFiles(); app.UseAntiforgery(); app.UseSwagger(); app.UseSwaggerUI();
app.MapPrometheusScrapingEndpoint();
app.MapGet("/health/live", () => new { status = "healthy" });
app.MapGet("/health/ready", async (RailDb db, LiveState live, CancellationToken ct) => await db.Database.CanConnectAsync(ct) && live.Snapshot.Mode != "Waiting" && DateTimeOffset.UtcNow - live.Snapshot.PublishedAt < TimeSpan.FromSeconds(90) ? Results.Ok(new { status = "ready", live.Snapshot.Mode, live.Snapshot.ObservedAt }) : Results.StatusCode(503));
app.MapGet("/api/snapshot", (LiveState state, string? line, string? category) => state.Snapshot with { Trains = state.Snapshot.Trains.Where(t => (line == null || t.Line == line) && (category == null || t.Category == category)).ToArray() });
app.MapGet("/api/trains", (LiveState state) => state.Snapshot.Trains);
app.MapGet("/api/trains/{id}/history", async (string id, string? mode, int? limit, RailDb db, CancellationToken ct) => await db.Observations.Where(x => x.TrainId == id && x.SourceMode == (mode ?? "Live")).OrderByDescending(x => x.ObservedAt).Take(Math.Clamp(limit ?? 100, 1, 1000)).ToListAsync(ct));
app.MapGet("/api/stations", async (RailDb db, CancellationToken ct) => await db.Stations.OrderBy(x => x.Name).Select(x => new StationDto(x.Id, x.Name, x.Latitude, x.Longitude)).ToListAsync(ct));
app.MapGet("/api/stations/{id}/arrivals", (string id, LiveState state) => state.Snapshot.Trains.Where(x => x.NextStopId == id).OrderBy(x => x.Arrival));
app.MapGet("/api/statistics/lines", (string? mode, DateTimeOffset? from, DateTimeOffset? to, Analytics analytics, LiveState state, CancellationToken ct) => analytics.LinesAsync(mode ?? state.Snapshot.Mode, from ?? state.Snapshot.ObservedAt.AddDays(-7), to ?? state.Snapshot.ObservedAt.AddHours(1), ct));
app.MapGet("/api/statistics/stations", (string? mode, Analytics analytics, LiveState state, CancellationToken ct) => analytics.StationsAsync(mode ?? state.Snapshot.Mode, state.Snapshot.ObservedAt.AddDays(-7), state.Snapshot.ObservedAt.AddHours(1), ct));
app.MapGet("/api/statistics/trend", (string? mode, bool? hourly, Analytics analytics, LiveState state, CancellationToken ct) => analytics.TrendAsync(mode ?? state.Snapshot.Mode, state.Snapshot.ObservedAt.AddDays(-7), state.Snapshot.ObservedAt.AddHours(1), hourly ?? false, ct));
app.MapGet("/api/statistics/worsening", (string? mode, Analytics analytics, LiveState state, CancellationToken ct) => analytics.WorseningAsync(mode ?? state.Snapshot.Mode, ct));
app.MapGet("/api/alerts/rules", async (RailDb db, CancellationToken ct) => await db.AlertRules.ToListAsync(ct));
app.MapPost("/api/alerts/rules", async (AlertRuleDto r, RailDb db, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(r.Name) || r.Name.Length > 120 || r.ThresholdMinutes is < 1 or > 180) return Results.ValidationProblem(new Dictionary<string, string[]> { { "rule", ["Nombre requerido y umbral entre 1 y 180 minutos."] } });
    if (r.StationId is not null && !await db.Stations.AnyAsync(x => x.Id == r.StationId, ct)) return Results.BadRequest(new { error = "Estación desconocida" });
    var row = new AlertRule { Name = r.Name.Trim(), Line = r.Line, StationId = r.StationId, ThresholdMinutes = r.ThresholdMinutes, Enabled = r.Enabled }; db.AlertRules.Add(row); await db.SaveChangesAsync(ct); return Results.Created("/api/alerts/rules/" + row.Id, row);
});
app.MapPut("/api/alerts/rules/{id:guid}", async (Guid id, AlertRuleDto r, RailDb db, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(r.Name) || r.Name.Length > 120 || r.ThresholdMinutes is < 1 or > 180) return Results.BadRequest();
    var row = await db.AlertRules.FindAsync([id], ct); if (row is null) return Results.NotFound(); row.Name = r.Name.Trim(); row.Line = r.Line; row.StationId = r.StationId; row.ThresholdMinutes = r.ThresholdMinutes; row.Enabled = r.Enabled; await db.SaveChangesAsync(ct); return Results.Ok(row);
});
app.MapDelete("/api/alerts/rules/{id:guid}", async (Guid id, RailDb db, CancellationToken ct) => await db.AlertRules.Where(x => x.Id == id).ExecuteDeleteAsync(ct) > 0 ? Results.NoContent() : Results.NotFound());
app.MapGet("/api/model", (DelayPredictor model) => new { model.Status, model.Samples, model.MeanAbsoluteError });
app.MapPost("/api/model/train", async (DelayPredictor model, RailDb db, LiveState state, CancellationToken ct) =>
{
    var trained = await model.TrainAsync(db, state.Snapshot.Mode, ct);
    if (trained) state.Apply(state.Snapshot);
    return new { trained, model.Status, model.MeanAbsoluteError };
});
app.MapGet("/api/export.xlsx", (LiveState state) =>
{
    using var workbook = new XLWorkbook(); var sheet = workbook.AddWorksheet("Seguimiento"); string[] headers = ["Tren", "Línea", "Servicio", "Próxima estación", "Retraso (min)", "Latitud", "Longitud", "Fuente", "Fecha del dato UTC", "Predicción (min)"];
    for (var i = 0; i < headers.Length; i++) sheet.Cell(1, i + 1).Value = headers[i];
    var row = 2; foreach (var t in state.Snapshot.Trains) { sheet.Cell(row, 1).Value = t.Id; sheet.Cell(row, 2).Value = t.Line; sheet.Cell(row, 3).Value = t.Category; sheet.Cell(row, 4).Value = t.NextStop; if (t.DelaySeconds.HasValue) sheet.Cell(row, 5).Value = t.DelaySeconds.Value / 60d; sheet.Cell(row, 6).Value = t.Latitude; sheet.Cell(row, 7).Value = t.Longitude; sheet.Cell(row, 8).Value = state.Snapshot.Mode; sheet.Cell(row, 9).Value = t.ObservedAt.UtcDateTime; if (t.PredictedDelayMinutes.HasValue) sheet.Cell(row, 10).Value = t.PredictedDelayMinutes.Value; row++; }
    sheet.Range(1, 1, Math.Max(2, row - 1), headers.Length).CreateTable(); sheet.Columns().AdjustToContents(); sheet.Column(9).Style.DateFormat.Format = "yyyy-mm-dd hh:mm:ss"; using var stream = new MemoryStream(); workbook.SaveAs(stream); return Results.File(stream.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "RailWatch-seguimiento.xlsx");
});
app.MapHub<TrainHub>("/hubs/trains");
app.MapStaticAssets();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
await Setup.InitializeAsync(app.Services, builder.Configuration);
app.Run();
public sealed class TrainHub(LiveState state) : Hub
{
    public override async Task OnConnectedAsync() { await Clients.Caller.SendAsync("snapshot", state.Snapshot); await base.OnConnectedAsync(); }
}
public sealed class HubRelay(LiveState state, IHubContext<TrainHub> hub) : IHostedService
{
    private static readonly Meter Meter = new("RailWatch");
    private static readonly Counter<long> Trains = Meter.CreateCounter<long>("railwatch.trains.processed");
    private static readonly Counter<long> Errors = Meter.CreateCounter<long>("railwatch.feed.errors");
    private static readonly Histogram<double> Latency = Meter.CreateHistogram<double>("railwatch.feed.latency", "ms");
    private long lastErrors;
    public Task StartAsync(CancellationToken ct) { state.Updated += OnUpdate; state.AlertRaised += OnAlert; return Task.CompletedTask; }
    public Task StopAsync(CancellationToken ct) { state.Updated -= OnUpdate; state.AlertRaised -= OnAlert; return Task.CompletedTask; }
    private void OnUpdate(FeedSnapshot s)
    {
        Trains.Add(s.Trains.Count); Latency.Record(s.LatencyMs); Errors.Add(Math.Max(0, s.FeedErrors - lastErrors)); lastErrors = s.FeedErrors;
        _ = hub.Clients.All.SendAsync("snapshot", s);
    }
    private void OnAlert(AlertEvent a) => _ = hub.Clients.All.SendAsync("alert", a);
}
public partial class Program;
