using System;
using System.Text.Json;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using SanitizerKit.Core.Patching;

namespace NoBOMSuite.Desktop;

public partial class SolutionReviewWindow : Window
{
    public SolutionReviewWindow()
    {
        InitializeComponent();
    }

    public SolutionReviewWindow(string patchJson) : this(patchJson, string.Empty, string.Empty)
    {
    }

    public SolutionReviewWindow(string patchJson, string filePath, string originalCode) : this()
    {
        var container = this.FindControl<StackPanel>("DiffLinesContainer");
        if (container != null)
        {
            if (string.IsNullOrEmpty(originalCode))
            {
                // Fallback: If no original code, just show the JSON or payload in a single block
                var border = new Border
                {
                    CornerRadius = new CornerRadius(2),
                    Padding = new Thickness(5, 2)
                };
                var textBlock = new TextBlock
                {
                    FontFamily = new FontFamily("Consolas, Courier New"),
                    TextWrapping = TextWrapping.Wrap,
                    Text = patchJson
                };
                border.Child = textBlock;
                container.Children.Add(border);
                return;
            }

            string newCode = GetNewCode(patchJson, originalCode);
            var diffLines = DiffHelper.ComputeDiff(originalCode, newCode);

            bool isDark = true;
            if (Application.Current != null)
            {
                isDark = Application.Current.RequestedThemeVariant == Avalonia.Styling.ThemeVariant.Dark || 
                         Application.Current.RequestedThemeVariant == Avalonia.Styling.ThemeVariant.Default;
            }

            string addedBg = isDark ? "#2e4a2c" : "#e6ffe6";
            string addedFg = isDark ? "#A6E3A1" : "#006600";
            string deletedBg = isDark ? "#4a2c2c" : "#ffe6e6";
            string deletedFg = isDark ? "#F38BA8" : "#990000";

            foreach (var line in diffLines)
            {
                var border = new Border
                {
                    CornerRadius = new CornerRadius(2),
                    Padding = new Thickness(5, 2)
                };

                var textBlock = new TextBlock
                {
                    FontFamily = new FontFamily("Consolas, Courier New"),
                    TextWrapping = TextWrapping.Wrap
                };

                if (line.Type == DiffType.Added)
                {
                    border.Background = Brush.Parse(addedBg);
                    textBlock.Text = "+ " + line.Text;
                    textBlock.Foreground = Brush.Parse(addedFg);
                }
                else if (line.Type == DiffType.Deleted)
                {
                    border.Background = Brush.Parse(deletedBg);
                    textBlock.Text = "- " + line.Text;
                    textBlock.Foreground = Brush.Parse(deletedFg);
                }
                else
                {
                    border.Background = Brushes.Transparent;
                    textBlock.Text = "  " + line.Text;
                }

                border.Child = textBlock;
                container.Children.Add(border);
            }
        }
    }

    private string GetNewCode(string patchJson, string originalCode)
    {
        try
        {
            using var doc = JsonDocument.Parse(patchJson);
            var root = doc.RootElement;
            
            string action = root.TryGetProperty("action", out var actionProp) ? actionProp.GetString() ?? "" : "";
            string payload = root.TryGetProperty("payload", out var payloadProp) ? payloadProp.GetString() ?? "" : "";

            if (action == "replace_code")
            {
                return payload;
            }
            else if (action == "generate_regex")
            {
                if (root.TryGetProperty("suggestedRecipe", out var recipe) && recipe.ValueKind == JsonValueKind.Object)
                {
                    string pattern = recipe.TryGetProperty("regexPattern", out var rp) ? rp.GetString() ?? "" : "";
                    string replacement = recipe.TryGetProperty("replacement", out var rep) ? rep.GetString() ?? "" : "";
                    
                    if (!string.IsNullOrEmpty(pattern))
                    {
                        return Regex.Replace(originalCode, pattern, replacement);
                    }
                }
                return payload;
            }
            return payload;
        }
        catch
        {
            return originalCode;
        }
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close(false);

    private void Apply_Click(object? sender, RoutedEventArgs e) => Close(true);
}