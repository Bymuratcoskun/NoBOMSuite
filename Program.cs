using System;
using Gtk;
using Adw;
using Gio;

namespace NoBOMSuite.Desktop;

class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        var app = Adw.Application.New("com.nobomsuite.desktop", Gio.ApplicationFlags.FlagsNone);
        
        app.OnActivate += (sender, e) =>
        {
            var adwApp = (Adw.Application)sender;
            var mainWindow = new MainWindow();
            mainWindow.SetApplication(adwApp);
            mainWindow.Present();
        };

        return app.Run(args);
    }
}
