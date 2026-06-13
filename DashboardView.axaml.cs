using Avalonia.Controls;
using Avalonia.Media;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using Microsoft.Data.Sqlite;
using SkiaSharp;


namespace NoBOMSuite.Desktop;

public class ScanHistoryItem
{
    public string FileName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Timestamp { get; set; } = string.Empty;
    public IBrush StatusColor { get; set; } = Brushes.Gray;
}

public partial class DashboardView : UserControl
{
    private int _totalFilesScanned = 0;
    private int _totalIssuesFound = 0;
    public ObservableCollection<ScanHistoryItem> ScanHistory { get; } = new();
    
    private TextBlock? _totalScannedText;
    private TextBlock? _totalIssuesText;

    public ISeries[] ActivitySeries { get; set; }
    public Axis[] XAxes { get; set; }
    public Axis[] YAxes { get; set; }

    public DashboardView()
    {
        InitializeComponent();

        // Grafik için serileri ve eksenleri hazırla
        ActivitySeries = new ISeries[]
        {
            new LineSeries<int>
            {
                Values = new int[24],
                Fill = new SolidColorPaint(SKColor.Parse("#89B4FA").WithAlpha(90)),
                Stroke = new SolidColorPaint(SKColor.Parse("#89B4FA")) { StrokeThickness = 2 },
                GeometryFill = null,
                GeometryStroke = null,
                Name = "Bulunan Sorunlar"
            }
        };

        XAxes = new Axis[]
        {
            new Axis { IsVisible = false, SeparatorsPaint = new SolidColorPaint(SKColors.Transparent) }
        };

        YAxes = new Axis[]
        {
            new Axis { IsVisible = false, SeparatorsPaint = new SolidColorPaint(SKColors.Transparent) }
        };

        this.DataContext = this;
        
        // Kontrol yüklendiğinde grafik verilerini asenkron olarak çek
        this.Loaded += async (s, e) => await LoadChartDataAsync();
        
        // Arayüz bileşenlerini 1 kere bularak önbelleğe alıyoruz (Performans darboğazını önler)
        _totalScannedText = this.FindControl<TextBlock>("TotalScannedText");
        _totalIssuesText = this.FindControl<TextBlock>("TotalIssuesText");
    }

    public void UpdateDashboard(string filePath, string status, IBrush color)
    {
        _totalFilesScanned++;
        if (status is "Sorunlu" or "Onarıldı")
        {
            _totalIssuesFound++;
        }

        // PRE-REMOVE TAKTİĞİ: Yeni elemanı eklemeden ÖNCE listeyi 99 elemana düşürüyoruz.
        // Bu sayede liste kapasitesi asla 100'ü aşıp arayüzde anlık "genişleme-daralma" (Layout Thrashing) yaratmaz.
        while (ScanHistory.Count >= 100)
        {
            ScanHistory.RemoveAt(ScanHistory.Count - 1); // RemoveAt(100) yerine her zaman son elemanı silmek daha güvenlidir.
        }

        ScanHistory.Insert(0, new ScanHistoryItem
        {
            FileName = Path.GetFileName(filePath),
            Status = status,
            Timestamp = DateTime.Now.ToString("HH:mm:ss"),
            StatusColor = color
        });


        if (_totalScannedText != null) _totalScannedText.Text = _totalFilesScanned.ToString();
        if (_totalIssuesText != null) _totalIssuesText.Text = _totalIssuesFound.ToString();
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
                    WHERE Timestamp >= $since AND (Message LIKE '⚠️%' OR Message LIKE '🛠️%' OR Message LIKE '✨%')
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
                            // Diziyi sondan başa doğru dolduruyoruz ki en güncel saat en sağda olsun
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

        // Grafik serisini UI thread üzerinde güncelle
        if (ActivitySeries.FirstOrDefault() is LineSeries<int> series)
        {
            series.Values = hourlyCounts;
        }
    }
}