using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using Gtk;
using Microsoft.Data.Sqlite;

namespace NoBOMSuite.Desktop;

public class ScanHistoryItem
{
    public string FileName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Timestamp { get; set; } = string.Empty;
    public string StatusColorHex { get; set; } = "#9399B2";
}

public class ActivityChartArea : Gtk.DrawingArea
{
    private int[] _data = new int[24];

    public ActivityChartArea()
    {
        SetDrawFunc(DrawChart);
    }

    public void SetData(int[] data)
    {
        _data = data;
        QueueDraw();
    }

    private void DrawChart(Gtk.DrawingArea area, Cairo.Context cr, int width, int height)
    {
        double marginX = 20;
        double marginY = 15;
        double chartWidth = width - 2 * marginX;
        double chartHeight = height - 2 * marginY;

        if (chartWidth <= 0 || chartHeight <= 0) return;

        // Find max value
        int maxVal = 1;
        foreach (var val in _data)
        {
            if (val > maxVal) maxVal = val;
        }

        // Draw grid
        cr.SetSourceRgba(0.19, 0.20, 0.27, 0.3); // Grid lines #313244
        cr.LineWidth = 1.0;
        for (int i = 0; i <= 4; i++)
        {
            double y = marginY + chartHeight * (i / 4.0);
            cr.MoveTo(marginX, y);
            cr.LineTo(width - marginX, y);
            cr.Stroke();
        }

        var points = new (double x, double y)[24];
        for (int i = 0; i < 24; i++)
        {
            double x = marginX + i * (chartWidth / 23.0);
            double y = (marginY + chartHeight) - (_data[i] * (chartHeight / maxVal));
            points[i] = (x, y);
        }

        // Gradient Fill
        var gradient = new Cairo.LinearGradient(0, marginY, 0, height - marginY);
        gradient.AddColorStopRgba(0.0, 0.54, 0.70, 0.98, 0.3); // #89B4FA
        gradient.AddColorStopRgba(1.0, 0.54, 0.70, 0.98, 0.0);

        cr.MoveTo(points[0].x, height - marginY);
        for (int i = 0; i < 24; i++)
        {
            cr.LineTo(points[i].x, points[i].y);
        }
        cr.LineTo(points[23].x, height - marginY);
        cr.ClosePath();
        cr.SetSource(gradient);
        cr.Fill();
        gradient.Dispose();

        // Stroke Line
        cr.SetSourceRgba(0.54, 0.70, 0.98, 1.0); // #89B4FA
        cr.LineWidth = 2.0;
        cr.MoveTo(points[0].x, points[0].y);
        for (int i = 1; i < 24; i++)
        {
            cr.LineTo(points[i].x, points[i].y);
        }
        cr.Stroke();

        // Dots
        cr.SetSourceRgba(1.0, 1.0, 1.0, 1.0);
        for (int i = 0; i < 24; i++)
        {
            if (_data[i] > 0)
            {
                cr.Arc(points[i].x, points[i].y, 3.5, 0, 2 * Math.PI);
                cr.Fill();
                cr.SetSourceRgba(0.54, 0.70, 0.98, 1.0);
                cr.Arc(points[i].x, points[i].y, 3.5, 0, 2 * Math.PI);
                cr.Stroke();
                cr.SetSourceRgba(1.0, 1.0, 1.0, 1.0);
            }
        }
    }
}

public class DashboardView : Gtk.Box
{
    private int _totalFilesScanned = 0;
    private int _totalIssuesFound = 0;
    
    private Gtk.Label? _totalScannedText;
    private Gtk.Label? _totalIssuesText;
    private Gtk.ListBox? _historyListBox;
    private ActivityChartArea? _chartArea;
    private readonly List<Gtk.ListBoxRow> _historyRows = new();

    public string? SelectedFilePath { get; private set; }

    public DashboardView() : base()
    {
        ((Gtk.Orientable)this).Orientation = Gtk.Orientation.Vertical;
        this.Spacing = 15;
        SetMarginStart(15);
        SetMarginEnd(15);
        SetMarginTop(15);
        SetMarginBottom(15);

        BuildUi();
        _ = LoadChartDataAsync();
    }

    private void BuildUi()
    {
        // 1. Stats row
        var statsGrid = Gtk.Grid.New();
        statsGrid.SetColumnSpacing(20);
        statsGrid.SetColumnHomogeneous(true);

        // Scanned stats card
        var cardScanned = Gtk.Frame.New(null);
        var boxScanned = Gtk.Box.New(Gtk.Orientation.Vertical, 5);
        boxScanned.SetMarginStart(20);
        boxScanned.SetMarginEnd(20);
        boxScanned.SetMarginTop(15);
        boxScanned.SetMarginBottom(15);
        
        var lblScannedTitle = Gtk.Label.New("Toplam Taranan Dosya");
        _totalScannedText = Gtk.Label.New("0");
        _totalScannedText.SetFontSize(28);
        _totalScannedText.SetFontWeight(Pango.Weight.Bold);
        _totalScannedText.SetMarkup("<span foreground=\"#89B4FA\">0</span>");
        
        boxScanned.Append(lblScannedTitle);
        boxScanned.Append(_totalScannedText);
        cardScanned.SetChild(boxScanned);
        statsGrid.Attach(cardScanned, 0, 0, 1, 1);

        // Issues stats card
        var cardIssues = Gtk.Frame.New(null);
        var boxIssues = Gtk.Box.New(Gtk.Orientation.Vertical, 5);
        boxIssues.SetMarginStart(20);
        boxIssues.SetMarginEnd(20);
        boxIssues.SetMarginTop(15);
        boxIssues.SetMarginBottom(15);
        
        var lblIssuesTitle = Gtk.Label.New("Toplam Bulunan Sorun");
        _totalIssuesText = Gtk.Label.New("0");
        _totalIssuesText.SetFontSize(28);
        _totalIssuesText.SetFontWeight(Pango.Weight.Bold);
        _totalIssuesText.SetMarkup("<span foreground=\"#F38BA8\">0</span>");

        boxIssues.Append(lblIssuesTitle);
        boxIssues.Append(_totalIssuesText);
        cardIssues.SetChild(boxIssues);
        statsGrid.Attach(cardIssues, 1, 0, 1, 1);

        Append(statsGrid);

        // 2. Tabs for History and Chart
        var notebook = Gtk.Notebook.New();
        notebook.SetVexpand(true);

        // Tab 1: History
        _historyListBox = Gtk.ListBox.New();
        _historyListBox.OnRowSelected += (sender, args) =>
        {
            var row = args.Row;
            SelectedFilePath = row?.GetName();
        };

        var scrollHistory = Gtk.ScrolledWindow.New();
        scrollHistory.SetChild(_historyListBox);
        notebook.AppendPage(scrollHistory, Gtk.Label.New("Son Aktiviteler"));

        // Tab 2: Activity Chart
        var chartBox = Gtk.Box.New(Gtk.Orientation.Vertical, 5);
        chartBox.SetMarginStart(10);
        chartBox.SetMarginEnd(10);
        chartBox.SetMarginTop(10);
        chartBox.SetMarginBottom(10);

        _chartArea = new ActivityChartArea();
        _chartArea.SetSizeRequest(-1, 150);
        _chartArea.SetVexpand(true);
        _chartArea.SetHexpand(true);
        chartBox.Append(_chartArea);

        notebook.AppendPage(chartBox, Gtk.Label.New("Son 24 Saatlik Aktivite Grafiği"));

        var notebookFrame = Gtk.Frame.New(null);
        notebookFrame.SetChild(notebook);
        Append(notebookFrame);
    }

    public void UpdateDashboard(string filePath, string status, string colorHex)
    {
        _totalFilesScanned++;
        if (status is "Sorunlu" or "Onarıldı")
        {
            _totalIssuesFound++;
        }

        // Add to history list box
        var row = Gtk.ListBoxRow.New();
        row.SetName(filePath); // Store path in name for retrieval

        var rowGrid = Gtk.Grid.New();
        rowGrid.SetColumnSpacing(15);
        rowGrid.SetMarginStart(10);
        rowGrid.SetMarginEnd(10);
        rowGrid.SetMarginTop(5);
        rowGrid.SetMarginBottom(5);

        // Colored status dot
        var dot = Gtk.Label.New("●");
        dot.SetMarkup($"<span foreground=\"{colorHex}\">●</span>");
        dot.SetSizeRequest(20, -1);
        rowGrid.Attach(dot, 0, 0, 1, 1);

        var lblName = Gtk.Label.New(Path.GetFileName(filePath));
        lblName.SetHalign(Gtk.Align.Start);
        lblName.SetHexpand(true);
        lblName.SetFontWeight(Pango.Weight.Medium);
        rowGrid.Attach(lblName, 1, 0, 1, 1);

        var lblTime = Gtk.Label.New(DateTime.Now.ToString("HH:mm:ss"));
        lblTime.SetHalign(Gtk.Align.End);
        lblTime.AddCssClass("monospace");
        rowGrid.Attach(lblTime, 2, 0, 1, 1);

        row.SetChild(rowGrid);

        _historyRows.Insert(0, row);
        _historyListBox?.Prepend(row);

        // Keep last 100
        if (_historyRows.Count > 100)
        {
            var oldRow = _historyRows[100];
            _historyListBox?.Remove(oldRow);
            _historyRows.RemoveAt(100);
        }

        if (_totalScannedText != null) 
            _totalScannedText.SetMarkup($"<span foreground=\"#89B4FA\">{_totalFilesScanned}</span>");
        if (_totalIssuesText != null) 
            _totalIssuesText.SetMarkup($"<span foreground=\"#F38BA8\">{_totalIssuesFound}</span>");
    }

    private async Task LoadChartDataAsync()
    {
        var hourlyCounts = new int[24];
        
        await Task.Run(() =>
        {
            var dbPath = Path.Combine(Environment.CurrentDirectory, "devguard_logs.db");
            if (!File.Exists(dbPath)) return;

            try
            {
                using var connection = new SqliteConnection($"Data Source={dbPath}");
                connection.Open();

                var command = connection.CreateCommand();
                var twentyFourHoursAgo = DateTime.UtcNow.AddHours(-24).ToString("o");

                command.CommandText = @"
                    SELECT Timestamp FROM Logs
                    WHERE Timestamp >= $since AND (Message LIKE '⚠️%' OR Message LIKE '🛠️%' OR Message LIKE '%')
                ";
                command.Parameters.AddWithValue("$since", twentyFourHoursAgo);

                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    var timestampStr = reader.GetString(0);
                    if (DateTime.TryParse(timestampStr, null, System.Globalization.DateTimeStyles.RoundtripKind, out var timestamp))
                    {
                        var hoursAgo = (int)(DateTime.UtcNow - timestamp).TotalHours;
                        if (hoursAgo >= 0 && hoursAgo < 24)
                        {
                            hourlyCounts[23 - hoursAgo]++;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[HATA] Grafik verisi okunurken hata: {ex.Message}");
            }
        });

        GLib.Functions.IdleAdd(0, () =>
        {
            _chartArea?.SetData(hourlyCounts);
            return false;
        });
    }
}
