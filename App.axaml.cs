using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace NoBOMSuite.Desktop;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow();

            // Programmatik Sistem Çekmecesi (System Tray) Oluşturma
            var trayMenu = new NativeMenu();
            var showItem = new NativeMenuItem("Kumanda Merkezini Aç");
            showItem.Click += TrayIcon_Show_Click;
            
            var exitItem = new NativeMenuItem("DevGuard'ı Kapat (Çıkış)");
            exitItem.Click += TrayIcon_Exit_Click;

            trayMenu.Items.Add(showItem);
            trayMenu.Items.Add(exitItem);

            var trayIcon = new TrayIcon
            {
                ToolTipText = "DevGuard Arka Plan Muhafızı",
                IsVisible = true,
                Menu = trayMenu
            };

            TrayIcon.SetIcons(this, new TrayIcons { trayIcon });
        }

        base.OnFrameworkInitializationCompleted();
    }

    public void TrayIcon_Show_Click(object? sender, EventArgs e)
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow?.Show();
            desktop.MainWindow?.Activate(); // Pencereyi en öne getir
        }
    }

    public void TrayIcon_Exit_Click(object? sender, EventArgs e)
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Sistem tepsisi üzerinden çıkış dendiğinde MainWindow üzerindeki (e.Cancel = true) engeline takılmamak için zorla kapatır.
            Environment.Exit(0);
        }
    }
}
