using System;
using System.IO;
using Avalonia.Threading;

namespace NoBOMSuite.Desktop;

public class BackgroundWatcher
{
    private FileSystemWatcher? _watcher;
    private readonly Action<string> _onFileChanged;
    private DateTime _lastEventTime = DateTime.MinValue;

    public BackgroundWatcher(Action<string> onFileChanged)
    {
        _onFileChanged = onFileChanged;
    }

    public void StartWatching(string path)
    {
        if (!Directory.Exists(path)) return;

        StopWatching();

        _watcher = new FileSystemWatcher(path)
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName,
            IncludeSubdirectories = true,
            EnableRaisingEvents = true
        };

        _watcher.Changed += OnChanged;
    }

    public void StopWatching()
    {
        if (_watcher != null)
        {
            _watcher.Changed -= OnChanged;
            _watcher.Dispose();
            _watcher = null;
        }
    }

    private void OnChanged(object sender, FileSystemEventArgs e)
    {
        // İşletim sistemleri bazen aynı kayıt işlemi için art arda 2-3 event fırlatabilir. (Debounce mekanizması)
        if ((DateTime.Now - _lastEventTime).TotalMilliseconds < 500) return;
        _lastEventTime = DateTime.Now;

        Dispatcher.UIThread.Post(() => _onFileChanged(e.FullPath));
    }
}
