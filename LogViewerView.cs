using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using Gtk;
using Microsoft.Data.Sqlite;

namespace NoBOMSuite.Desktop;

public class LogEntry
{
    public int Id { get; set; }
    public string Timestamp { get; set; } = string.Empty;
    public string Level { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? SourceFile { get; set; }
}

public class LogViewerView : Gtk.Box
{
    private static readonly string DbPath = Path.Combine(Environment.CurrentDirectory, "devguard_logs.db");
    private Gtk.DropDown? _logLevelFilter;
    private Gtk.ListBox? _logListBox;
    private bool _isLoaded = false;

    public LogViewerView() : base()
    {
        ((Gtk.Orientable)this).Orientation = Gtk.Orientation.Vertical;
        this.Spacing = 10;
        SetMarginStart(15);
        SetMarginEnd(15);
        SetMarginTop(15);
        SetMarginBottom(15);

        BuildUi();

        _isLoaded = true;
        _ = LoadLogsAsync();
    }

    private void BuildUi()
    {
        // Top Filter Bar
        var filterBox = Gtk.Box.New(Gtk.Orientation.Horizontal, 10);
        filterBox.SetHalign(Gtk.Align.End);

        var lblLevel = Gtk.Label.New("Seviye:");
        filterBox.Append(lblLevel);

        string[] levels = ["Tümü", "INFO", "WARNING", "ERROR"];
        _logLevelFilter = Gtk.DropDown.NewFromStrings(levels);
        _logLevelFilter.OnNotify += (s, e) =>
        {
            if (e.Pspec.GetName() == "selected" && _isLoaded)
            {
                _ = LoadLogsAsync();
            }
        };
        filterBox.Append(_logLevelFilter);

        var btnRefresh = Gtk.Button.NewWithLabel("🔄 Logları Yenile");
        btnRefresh.AddCssClass("suggested-action");
        btnRefresh.OnClicked += (s, e) => _ = LoadLogsAsync();
        filterBox.Append(btnRefresh);

        Append(filterBox);

        // Header Row for Log Table
        var headerGrid = Gtk.Grid.New();
        headerGrid.SetColumnSpacing(10);
        headerGrid.SetMarginStart(10);
        headerGrid.SetMarginEnd(10);

        var hId = Gtk.Label.New("ID");
        hId.SetFontWeight(Pango.Weight.Bold);
        hId.SetSizeRequest(50, -1);
        hId.SetHalign(Gtk.Align.Start);
        headerGrid.Attach(hId, 0, 0, 1, 1);

        var hTime = Gtk.Label.New("Zaman Damgası");
        hTime.SetFontWeight(Pango.Weight.Bold);
        hTime.SetSizeRequest(150, -1);
        hTime.SetHalign(Gtk.Align.Start);
        headerGrid.Attach(hTime, 1, 0, 1, 1);

        var hLevel = Gtk.Label.New("Seviye");
        hLevel.SetFontWeight(Pango.Weight.Bold);
        hLevel.SetSizeRequest(80, -1);
        hLevel.SetHalign(Gtk.Align.Start);
        headerGrid.Attach(hLevel, 2, 0, 1, 1);

        var hMsg = Gtk.Label.New("Mesaj");
        hMsg.SetFontWeight(Pango.Weight.Bold);
        hMsg.SetHexpand(true);
        hMsg.SetHalign(Gtk.Align.Start);
        headerGrid.Attach(hMsg, 3, 0, 1, 1);

        var hSrc = Gtk.Label.New("Kaynak Dosya");
        hSrc.SetFontWeight(Pango.Weight.Bold);
        hSrc.SetSizeRequest(180, -1);
        hSrc.SetHalign(Gtk.Align.Start);
        headerGrid.Attach(hSrc, 4, 0, 1, 1);

        Append(headerGrid);

        // List Area
        _logListBox = Gtk.ListBox.New();
        
        var scroll = Gtk.ScrolledWindow.New();
        scroll.SetVexpand(true);
        scroll.SetChild(_logListBox);
        Append(scroll);

        // Double-click to open source files
        _logListBox.OnRowActivated += (sender, args) =>
        {
            var row = args.Row;
            if (row != null && row.GetName() is string file && !string.IsNullOrEmpty(file))
            {
                OpenFileInVsCode(file);
            }
        };
    }

    private void OpenFileInVsCode(string filePath)
    {
        if (File.Exists(filePath))
        {
            try
            {
                var processInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = OperatingSystem.IsWindows() ? "cmd.exe" : "code",
                    Arguments = OperatingSystem.IsWindows() ? $"/c code \"{filePath}\"" : $"\"{filePath}\"",
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

    private async Task LoadLogsAsync()
    {
        if (_logLevelFilter == null || _logListBox == null) return;

        var selectedIndex = (int)_logLevelFilter.GetSelected();
        string filterLevel = selectedIndex switch
        {
            1 => "INFO",
            2 => "WARNING",
            3 => "ERROR",
            _ => "Tümü"
        };

        var logsToShow = new List<LogEntry>();

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
            catch (Exception ex) 
            { 
                Console.WriteLine($"[HATA] SQLite logları okunamadı: {ex.Message}"); 
            }
        });

        // Clear and reload list view
        GLib.Functions.IdleAdd(0, () =>
        {
            // Clear children
            var firstChild = _logListBox.GetFirstChild();
            while (firstChild != null)
            {
                _logListBox.Remove(firstChild);
                firstChild = _logListBox.GetFirstChild();
            }

            foreach (var log in logsToShow.OrderByDescending(l => l.Id))
            {
                var row = Gtk.ListBoxRow.New();
                if (!string.IsNullOrEmpty(log.SourceFile))
                {
                    row.SetName(log.SourceFile); // Store filePath in row name
                    row.SetTooltipText("VS Code ile açmak için çift tıklayın");
                }

                var grid = Gtk.Grid.New();
                grid.SetColumnSpacing(10);
                grid.SetMarginStart(10);
                grid.SetMarginEnd(10);
                grid.SetMarginTop(5);
                grid.SetMarginBottom(5);

                var lblId = Gtk.Label.New(log.Id.ToString());
                lblId.SetSizeRequest(50, -1);
                lblId.SetHalign(Gtk.Align.Start);
                lblId.AddCssClass("monospace");
                grid.Attach(lblId, 0, 0, 1, 1);

                var lblTime = Gtk.Label.New(log.Timestamp);
                lblTime.SetSizeRequest(150, -1);
                lblTime.SetHalign(Gtk.Align.Start);
                lblTime.AddCssClass("monospace");
                grid.Attach(lblTime, 1, 0, 1, 1);

                var lblLevel = Gtk.Label.New(log.Level);
                lblLevel.SetSizeRequest(80, -1);
                lblLevel.SetHalign(Gtk.Align.Start);
                lblLevel.SetFontWeight(Pango.Weight.Bold);
                if (log.Level == "ERROR")
                {
                    lblLevel.SetMarkup("<span foreground=\"#F38BA8\">ERROR</span>");
                }
                else if (log.Level == "WARNING")
                {
                    lblLevel.SetMarkup("<span foreground=\"#F9E2AF\">WARNING</span>");
                }
                else
                {
                    lblLevel.SetMarkup("<span foreground=\"#A6E3A1\">INFO</span>");
                }
                grid.Attach(lblLevel, 2, 0, 1, 1);

                var lblMsg = Gtk.Label.New(log.Message);
                lblMsg.SetHexpand(true);
                lblMsg.SetHalign(Gtk.Align.Start);
                lblMsg.SetWrap(true);
                grid.Attach(lblMsg, 3, 0, 1, 1);

                var lblSrc = Gtk.Label.New(string.IsNullOrEmpty(log.SourceFile) ? "" : Path.GetFileName(log.SourceFile));
                lblSrc.SetSizeRequest(180, -1);
                lblSrc.SetHalign(Gtk.Align.Start);
                lblSrc.SetWrap(true);
                lblSrc.AddCssClass("monospace");
                grid.Attach(lblSrc, 4, 0, 1, 1);

                row.SetChild(grid);
                _logListBox.Append(row);
            }
            return false;
        });
    }
}
