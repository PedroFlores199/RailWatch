using System.Media;
using System.Windows;
using ClosedXML.Excel;
using Microsoft.Win32;
using RailWatch.Shared;

namespace RailWatch.Desktop.Wpf;

public partial class MainWindow : Window
{
    private readonly ControlRoomViewModel model;
    private readonly DesktopFeedClient client;
    public MainWindow(ControlRoomViewModel model, DesktopFeedClient client)
    {
        InitializeComponent(); this.model = model; this.client = client; DataContext = model;
        model.SoundRequested += () => SystemSounds.Exclamation.Play();
        Loaded += async (_, _) => await client.ConnectAsync();
        Closed += async (_, _) => await client.DisposeAsync();
    }
    private void Export_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { Filter = "Excel (*.xlsx)|*.xlsx", FileName = "RailWatch-control.xlsx" };
        if (dialog.ShowDialog() != true) return;
        try
        {
            using var book = new XLWorkbook(); var sheet = book.AddWorksheet("Control"); string[] headers = ["Tren", "Línea", "Estación", "Retraso (segundos)", "Fecha del dato UTC", "Predicción (min)", "Fuente"];
            for (var c = 0; c < headers.Length; c++) sheet.Cell(1, c + 1).Value = headers[c];
            var row = 2; foreach (var t in model.Trains) { sheet.Cell(row, 1).Value = t.Id; sheet.Cell(row, 2).Value = t.Line; sheet.Cell(row, 3).Value = t.NextStop; if (t.DelaySeconds.HasValue) sheet.Cell(row, 4).Value = t.DelaySeconds.Value; sheet.Cell(row, 5).Value = t.ObservedAt.UtcDateTime; if (t.PredictedDelayMinutes.HasValue) sheet.Cell(row, 6).Value = t.PredictedDelayMinutes.Value; sheet.Cell(row, 7).Value = model.SourceMode; row++; }
            sheet.Range(1, 1, Math.Max(2, row - 1), 7).CreateTable(); sheet.Columns().AdjustToContents(); sheet.Column(5).Style.DateFormat.Format = "yyyy-mm-dd hh:mm:ss"; book.SaveAs(dialog.FileName);
        }
        catch (Exception ex) { MessageBox.Show(this, "No se pudo exportar: " + ex.Message, "RailWatch", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }
}
