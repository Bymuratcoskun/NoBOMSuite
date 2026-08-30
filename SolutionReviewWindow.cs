using System;
using System.Text.Json;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Gtk;
using SanitizerKit.Core.Patching;

namespace NoBOMSuite.Desktop;

public class SolutionReviewWindow : Gtk.Window
{
    private readonly TaskCompletionSource<bool> _tcs = new();

    public SolutionReviewWindow(string patchJson, string filePath, string originalCode, Gtk.Window parent)
    {
        SetTitle("AI Çözüm İnceleme Penceresi");
        SetDefaultSize(800, 600);
        SetTransientFor(parent);
        SetModal(true);

        BuildUi(patchJson, filePath, originalCode);
    }

    public Task<bool> ShowAsync()
    {
        this.Present();
        return _tcs.Task;
    }

    private void BuildUi(string patchJson, string filePath, string originalCode)
    {
        var box = Gtk.Box.New(Gtk.Orientation.Vertical, 15);
        box.SetMarginStart(20);
        box.SetMarginEnd(20);
        box.SetMarginTop(20);
        box.SetMarginBottom(20);

        // Header Title
        var headerLabel = Gtk.Label.New("");
        headerLabel.SetMarkup("<span size=\"14000\" weight=\"bold\" foreground=\"#89B4FA\">🤖 Ajan 2 Tarafından Üretilen Güvenli Yama (Fark Görünümü)</span>");
        headerLabel.SetHalign(Gtk.Align.Start);
        box.Append(headerLabel);

        // Scrolled view for diff content
        var scroll = Gtk.ScrolledWindow.New();
        scroll.SetVexpand(true);
        scroll.SetHexpand(true);

        var diffContainer = Gtk.Box.New(Gtk.Orientation.Vertical, 2);
        diffContainer.SetMarginStart(15);
        diffContainer.SetMarginEnd(15);
        diffContainer.SetMarginTop(15);
        diffContainer.SetMarginBottom(15);

        if (string.IsNullOrEmpty(originalCode))
        {
            var textBlock = Gtk.Label.New(patchJson);
            textBlock.SetWrap(true);
            textBlock.SetHalign(Gtk.Align.Start);
            textBlock.AddCssClass("monospace");
            diffContainer.Append(textBlock);
        }
        else
        {
            string newCode = GetNewCode(patchJson, originalCode);
            var diffLines = DiffHelper.ComputeDiff(originalCode, newCode);

            foreach (var line in diffLines)
            {
                var label = Gtk.Label.New("");
                label.SetHalign(Gtk.Align.Start);
                label.SetWrap(true);
                label.AddCssClass("monospace");

                string escapedText = GLib.Markup.EscapeText(line.Text);

                if (line.Type == DiffType.Added)
                {
                    label.SetMarkup($"<span foreground=\"#A6E3A1\">+ {escapedText}</span>");
                }
                else if (line.Type == DiffType.Deleted)
                {
                    label.SetMarkup($"<span foreground=\"#F38BA8\">- {escapedText}</span>");
                }
                else
                {
                    label.SetText($"  {line.Text}");
                }

                diffContainer.Append(label);
            }
        }

        var frame = Gtk.Frame.New(null);
        scroll.SetChild(diffContainer);
        frame.SetChild(scroll);
        box.Append(frame);

        // Buttons
        var btnBox = Gtk.Box.New(Gtk.Orientation.Horizontal, 15);
        btnBox.SetHalign(Gtk.Align.End);

        var btnCancel = Gtk.Button.NewWithLabel("İptal Et");
        btnCancel.OnClicked += (s, e) =>
        {
            _tcs.TrySetResult(false);
            this.Close();
        };

        var btnApply = Gtk.Button.NewWithLabel("Yamayı Uygula");
        btnApply.AddCssClass("suggested-action");
        btnApply.OnClicked += (s, e) =>
        {
            _tcs.TrySetResult(true);
            this.Close();
        };

        btnBox.Append(btnCancel);
        btnBox.Append(btnApply);
        box.Append(btnBox);

        SetChild(box);

        this.OnCloseRequest += (s, e) =>
        {
            _tcs.TrySetResult(false);
            return false;
        };
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
}
