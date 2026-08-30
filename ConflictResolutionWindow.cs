using System;
using System.IO;
using System.Threading.Tasks;
using Gtk;

namespace NoBOMSuite.Desktop;

public class ConflictResolutionWindow : Gtk.Window
{
    private readonly TaskCompletionSource<bool> _tcs = new();

    public ConflictResolutionWindow(string filePath, DateTime scannedAt, DateTime modifiedAt, Gtk.Window parent)
    {
        SetTitle("Dosya Çakışması Tespit Edildi");
        SetDefaultSize(540, 270);
        SetTransientFor(parent);
        SetModal(true);
        SetResizable(false);

        BuildUi(filePath, scannedAt, modifiedAt);
    }

    public Task<bool> ShowAsync()
    {
        this.Present();
        return _tcs.Task;
    }

    private void BuildUi(string filePath, DateTime scannedAt, DateTime modifiedAt)
    {
        var box = Gtk.Box.New(Gtk.Orientation.Vertical, 15);
        box.SetMarginStart(25);
        box.SetMarginEnd(25);
        box.SetMarginTop(25);
        box.SetMarginBottom(25);

        var title = Gtk.Label.New("");
        title.SetMarkup("<span size=\"14000\" weight=\"bold\" foreground=\"#F9E2AF\">⚠️ Dosya Çakışması</span>");
        title.SetHalign(Gtk.Align.Start);
        box.Append(title);

        var conflictInfo = Gtk.Label.New($"Aşağıdaki dosya tarandıktan sonra dışarıdan değiştirildi:\n{Path.GetFileName(filePath)}\n\nNasıl devam etmek istersiniz?");
        conflictInfo.SetWrap(true);
        conflictInfo.SetHalign(Gtk.Align.Start);
        box.Append(conflictInfo);

        // Frame showing times
        var timeFrame = Gtk.Frame.New(null);
        var timeBox = Gtk.Box.New(Gtk.Orientation.Vertical, 5);
        timeBox.SetMarginStart(12);
        timeBox.SetMarginEnd(12);
        timeBox.SetMarginTop(10);
        timeBox.SetMarginBottom(10);

        var originalTimeLabel = Gtk.Label.New($"Taranma zamanı  : {scannedAt.ToLocalTime():yyyy-MM-dd HH:mm:ss}");
        originalTimeLabel.SetHalign(Gtk.Align.Start);
        originalTimeLabel.AddCssClass("monospace");

        var currentTimeLabel = Gtk.Label.New("");
        currentTimeLabel.SetHalign(Gtk.Align.Start);
        currentTimeLabel.AddCssClass("monospace");
        currentTimeLabel.SetMarkup($"<span foreground=\"#F38BA8\">Son değişiklik  : {modifiedAt.ToLocalTime():yyyy-MM-dd HH:mm:ss}  ← Dışarıdan değişti!</span>");

        timeBox.Append(originalTimeLabel);
        timeBox.Append(currentTimeLabel);
        timeFrame.SetChild(timeBox);
        box.Append(timeFrame);

        // Buttons
        var btnBox = Gtk.Box.New(Gtk.Orientation.Horizontal, 10);
        btnBox.SetHalign(Gtk.Align.End);

        var btnSkip = Gtk.Button.NewWithLabel("Bu Dosyayı Atla");
        btnSkip.OnClicked += (s, e) =>
        {
            _tcs.TrySetResult(false);
            this.Close();
        };

        var btnFix = Gtk.Button.NewWithLabel("Yine de Onar (Yeni İçerikle)");
        btnFix.AddCssClass("suggested-action");
        btnFix.OnClicked += (s, e) =>
        {
            _tcs.TrySetResult(true);
            this.Close();
        };

        btnBox.Append(btnSkip);
        btnBox.Append(btnFix);
        box.Append(btnBox);

        SetChild(box);

        // Handle window close by user (e.g. clicking X button)
        this.OnCloseRequest += (s, e) =>
        {
            _tcs.TrySetResult(false);
            return false;
        };
    }
}
