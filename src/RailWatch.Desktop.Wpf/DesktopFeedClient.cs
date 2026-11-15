using System.Net.Http;
using System.Net.Http.Json;
using System.Windows;
using Microsoft.AspNetCore.SignalR.Client;
using RailWatch.Shared;

namespace RailWatch.Desktop.Wpf;

public sealed class DesktopFeedClient(ControlRoomViewModel model) : IAsyncDisposable
{
    private HubConnection? connection;
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
        var s = await http.GetFromJsonAsync<FeedSnapshot>("/api/snapshot"); if (s is not null) model.Apply(s);
        var lines = await http.GetFromJsonAsync<LineStats[]>("/api/statistics/lines"); model.Lines.Clear(); foreach (var l in lines ?? []) model.Lines.Add(l);
    }
    public async ValueTask DisposeAsync() { if (connection is not null) await connection.DisposeAsync(); http.Dispose(); }
}
