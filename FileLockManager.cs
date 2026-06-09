using System;
using System.Collections.Concurrent;

namespace SanitizerKit.Core.Locks;

public class FileLockManager
{
    // Dosya Yolu -> Kilidi Koyan Rol (Örn: "IDE", "DevGuard_Core")
    private readonly ConcurrentDictionary<string, string> _lockedFiles = new();

    public bool TryLock(string filePath, string owner)
    {
        // Eğer dosya başka bir süreç tarafından kilitlenmemişse kilitler ve true döner
        return _lockedFiles.TryAdd(filePath, owner);
    }

    public void Unlock(string filePath, string owner)
    {
        // Sadece kilidi koyan taraf kendi kilidini kaldırabilir (Güvenlik Önlemi)
        if (_lockedFiles.TryGetValue(filePath, out var currentOwner) && currentOwner == owner)
        {
            _lockedFiles.TryRemove(filePath, out _);
        }
    }

    public bool IsLocked(string filePath) => _lockedFiles.ContainsKey(filePath);

    public string? GetLockOwner(string filePath)
    {
        _lockedFiles.TryGetValue(filePath, out var owner);
        return owner;
    }
}
