using System;
using System.IO;
using System.Collections.Concurrent;
namespace NoBOMSuite.Desktop;

public class BackgroundWatcher
{
    private FileSystemWatcher? _watcher;
    private readonly Action<string> _onFileChanged;
    
    private readonly ConcurrentDictionary<string, DateTime> _lastEventTimes = new(StringComparer.OrdinalIgnoreCase);

    public BackgroundWatcher(Action<string> onFileChanged)
    {
        _onFileChanged = onFileChanged;
    }

    public void StartWatching(string path)
    {
        if (!Directory.Exists(path)) return;

        StopWatching();
        _lastEventTimes.Clear();

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
        var now = DateTime.UtcNow;

        // Her dosya için bağımsız debounce süresi uygulayarak yarış durumlarını ve dosya atlamalarını engelliyoruz
        if (_lastEventTimes.TryGetValue(e.FullPath, out var lastTime) && (now - lastTime).TotalMilliseconds < 500)
        {
            return;
        }

        _lastEventTimes[e.FullPath] = now;

        // Tam silme yerine 5 dakikadan eski girişleri temizle; bu sayede yeni olaylar debounce kaçırılmaz
        if (_lastEventTimes.Count > 1000)
        {
            var cutoff = now.AddMinutes(-5);
            foreach (var key in _lastEventTimes.Keys)
            {
                if (_lastEventTimes.TryGetValue(key, out var t) && t < cutoff)
                    _lastEventTimes.TryRemove(key, out _);
            }
        }

        Dispatcher.UIThread.Post(() => _onFileChanged(e.FullPath));
    }
}
