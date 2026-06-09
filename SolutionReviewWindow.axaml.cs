using System;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace NoBOMSuite.Desktop;

public partial class SolutionReviewWindow : Window
{
    public SolutionReviewWindow()
    {
        InitializeComponent();
    }

    public SolutionReviewWindow(string patchJson) : this()
    {
        var textBlock = this.FindControl<TextBlock>("PatchContent");
        if (textBlock != null)
        {
            try
            {
                // JSON'u daha okunaklı hale getirmek için parse edip tekrar indent ile string yapıyoruz
                using var document = JsonDocument.Parse(patchJson);
                textBlock.Text = JsonSerializer.Serialize(document, new JsonSerializerOptions { WriteIndented = true });
            }
            catch
            {
                textBlock.Text = patchJson; // Parse edilemezse AI'nin gönderdiği ham halini göster
            }
        }
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close(false);

    private void Apply_Click(object? sender, RoutedEventArgs e) => Close(true);
}