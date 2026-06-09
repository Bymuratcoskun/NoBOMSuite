using Avalonia.Controls;
using Avalonia.Media;
using System;
using System.Collections.ObjectModel;
using System.IO;

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
    private readonly ObservableCollection<ScanHistoryItem> _scanHistory = new();

    public DashboardView()
    {
        InitializeComponent();
        var historyListBox = this.FindControl<ListBox>("ScanHistoryListBox");
        if (historyListBox != null)
        {
            historyListBox.ItemsSource = _scanHistory;
        }
    }

    public void UpdateDashboard(string filePath, string status, IBrush color)
    {
        _totalFilesScanned++;
        if (status is "Sorunlu" or "Onarıldı")
        {
            _totalIssuesFound++;
        }

        _scanHistory.Insert(0, new ScanHistoryItem
        {
            FileName = Path.GetFileName(filePath),
            Status = status,
            Timestamp = DateTime.Now.ToString("HH:mm:ss"),
            StatusColor = color
        });

        if (_scanHistory.Count > 100) _scanHistory.RemoveAt(100);

        var totalScannedText = this.FindControl<TextBlock>("TotalScannedText");
        var totalIssuesText = this.FindControl<TextBlock>("TotalIssuesText");
        if (totalScannedText != null) totalScannedText.Text = _totalFilesScanned.ToString();
        if (totalIssuesText != null) totalIssuesText.Text = _totalIssuesFound.ToString();
    }
}