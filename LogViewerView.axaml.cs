using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia;
using Avalonia.VisualTree;
using Avalonia.Input;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System;
using System.IO;
using Microsoft.Data.Sqlite;
using System.Collections.Generic;
using System.Linq;

namespace NoBOMSuite.Desktop;

public class LogEntry
{
    public int Id { get; set; }
    public string Timestamp { get; set; } = string.Empty;
    public string Level { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? SourceFile { get; set; }
}

public partial class LogViewerView : UserControl
{
    private static readonly string DbPath = Path.Combine(Environment.CurrentDirectory, "devguard_logs.db");
    public ObservableCollection<LogEntry> Logs { get; } = new();

    private bool _isLoaded = false;

    public LogViewerView()
    {
        InitializeComponent();
        this.DataContext = this; // Veri bağlamını kendisi olarak ayarla
        this.Loaded += async (s, e) => 
        {
            _isLoaded = true;
            await LoadLogsAsync();
        };
    }

    private async void LogLevelFilter_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_isLoaded)
        {
            await LoadLogsAsync();
        }
    }

    private async void RefreshButton_Click(object? sender, RoutedEventArgs e)
    {
        await LoadLogsAsync();
    }

    private void DataGrid_PointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (sender is not DataGrid grid) return;

        // DataGrid'in içindeki ScrollViewer'ı bul
        var scrollViewer = grid.FindDescendantOfType<ScrollViewer>();
        if (scrollViewer != null)
        {
            const double scrollSpeedMultiplier = 5.0; // Kaydırma hızını 5 kat artır
            var newOffset = new Vector(
                scrollViewer.Offset.X,
                scrollViewer.Offset.Y - (e.Delta.Y * scrollSpeedMultiplier)
            );
            scrollViewer.Offset = newOffset;
            e.Handled = true; // Varsayılan yavaş kaydırmayı engelle
        }
    }

    private void DataGrid_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is DataGrid grid && grid.SelectedItem is LogEntry log)
        {
            if (!string.IsNullOrEmpty(log.SourceFile) && File.Exists(log.SourceFile))
            {
                try
                {
                    var processInfo = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = OperatingSystem.IsWindows() ? "cmd.exe" : "code",
                        Arguments = OperatingSystem.IsWindows() ? $"/c code \"{log.SourceFile}\"" : $"\"{log.SourceFile}\"",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    System.Diagnostics.Process.Start(processInfo);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[HATA] VS Code açılamadı: {ex.Message}");
                }
            }
        }
    }

    private async Task LoadLogsAsync()
    {
        var logsToShow = new List<LogEntry>();
        string filterLevel = "Tümü";
        
        var filterCombo = this.FindControl<ComboBox>("LogLevelFilter");
        if (filterCombo != null && filterCombo.SelectedItem is ComboBoxItem item)
        {
            filterLevel = item.Content?.ToString() ?? "Tümü";
        }
        
        await Task.Run(() =>
        {
            if (!File.Exists(DbPath)) return;

            try
            {
                using var connection = new SqliteConnection($"Data Source={DbPath}");
                connection.Open();

                var command = connection.CreateCommand();
                string whereClause = filterLevel == "Tümü" ? "" : "WHERE Level = $level";
                
                command.CommandText = $@"
                    SELECT Id, Timestamp, Level, Message, SourceFile 
                    FROM Logs 
                    {whereClause}
                    ORDER BY Id DESC 
                    LIMIT 200";

                if (filterLevel != "Tümü")
                {
                    command.Parameters.AddWithValue("$level", filterLevel);
                }

                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    logsToShow.Add(new LogEntry
                    {
                        Id = reader.GetInt32(0),
                        Timestamp = reader.GetString(1),
                        Level = reader.GetString(2),
                        Message = reader.GetString(3),
                        SourceFile = reader.IsDBNull(4) ? null : reader.GetString(4)
                    });
                }
            }
            catch (Exception ex) { Console.WriteLine($"[HATA] SQLite logları okunamadı: {ex.Message}"); }
        });

        Logs.Clear();
        foreach (var log in logsToShow.OrderByDescending(l => l.Id)) { Logs.Add(log); }
    }
}