using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.IO;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RailWatch.Shared;

namespace RailWatch.Desktop.Wpf;

public partial class App : Application
{
    private IHost? host;
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        host = Host.CreateDefaultBuilder().ConfigureServices(s => { s.AddSingleton<ControlRoomViewModel>(); s.AddSingleton<DesktopFeedClient>(); s.AddSingleton<MainWindow>(); }).Build();
        await host.StartAsync();
        var window = host.Services.GetRequiredService<MainWindow>();
        window.Show();
        if (e.Args.Contains("--smoke-test"))
        {
            var model = host.Services.GetRequiredService<ControlRoomViewModel>();
            for (var i = 0; i < 100 && model.TrainCount == 0; i++) await Task.Delay(200);
            if (model.TrainCount == 0) { Shutdown(1); return; }
            window.UpdateLayout();
            var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(window);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            var path = Environment.GetEnvironmentVariable("RAILWATCH_SCREENSHOT") ?? "wpf-smoke.png";
            using (var output = File.Create(path)) encoder.Save(output);
            Shutdown(0);
        }
    }
    protected override async void OnExit(ExitEventArgs e)
    {
        if (host is not null) { await host.StopAsync(); host.Dispose(); }
        base.OnExit(e);
    }
}
