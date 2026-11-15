using System.Net.Http;
using System.Net.Http.Json;
using System.Windows;
using Microsoft.AspNetCore.SignalR.Client;
using RailWatch.Shared;

namespace RailWatch.Desktop.Wpf;

public sealed class DesktopFeedClient(ControlRoomViewModel model) : IAsyncDisposable
{
    private HubConnection? connection;
    private int disposed;
    private readonly HttpClient http = new() { BaseAddress = new Uri(Environment.GetEnvironmentVariable("RAILWATCH_URL") ?? "http://localhost:5090") };
    public async Task ConnectAsync()
    {
        connection = new HubConnectionBuilder().WithUrl(new Uri(http.BaseAddress!, "/hubs/trains")).WithAutomaticReconnect([TimeSpan.Zero, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30)]).Build();
        connection.On<FeedSnapshot>("snapshot", s => Application.Current.Dispatcher.InvokeAsync(() => model.Apply(s)));
        connection.On<AlertEvent>("alert", a => Application.Current.Dispatcher.InvokeAsync(() => { if (model.Alerts.All(x => x.TrainId != a.TrainId)) model.Alerts.Insert(0, a); }));
        connection.Reconnecting += _ => { model.Status = "Reconectando…"; return Task.CompletedTask; };
        connection.Reconnected += async _ => await ReloadAsync();
        connection.Closed += _ => { model.Status = "Sin conexión. Cierra y abre el cliente para reintentar."; return Task.CompletedTask; };
        try { await connection.StartAsync(); await ReloadAsync(); }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or TaskCanceledException) { model.Status = "API no disponible. Comprueba RAILWATCH_URL y reinicia el cliente."; }
    }
    private async Task ReloadAsync()
    {
        var snapshot = await http.GetFromJsonAsync<FeedSnapshot>("/api/snapshot");
        if (snapshot is null) return;
        var date = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(snapshot.ObservedAt, TimeZoneInfo.FindSystemTimeZoneById("Europe/Madrid")).DateTime);
        var from = DelayCalculator.ScheduledUtc(date, "00:00:00");
        var to = DelayCalculator.ScheduledUtc(date.AddDays(1), "00:00:00");
        var query = "/api/statistics/lines?mode=" + Uri.EscapeDataString(snapshot.Mode) + "&from=" + Uri.EscapeDataString(from.ToString("O")) + "&to=" + Uri.EscapeDataString(to.ToString("O"));
        var lines = await http.GetFromJsonAsync<LineStats[]>(query);
        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            model.Apply(snapshot);
            model.Lines.Clear();
            foreach (var line in (lines ?? []).OrderBy(x => x.Punctuality).Take(6)) model.Lines.Add(line);
        });
    }
    public async ValueTask DisposeAsync() { if (Interlocked.Exchange(ref disposed, 1) != 0) return; if (connection is not null) await connection.DisposeAsync(); http.Dispose(); }
}
