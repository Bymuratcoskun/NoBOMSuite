using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;

namespace SanitizerKit.Core.Locks;

public class FileLockManager
{
    private class LockInfo
    {
        public string Owner { get; }
        public DateTime ExpiresAt { get; }
        
        public LockInfo(string owner, DateTime expiresAt)
        {
            Owner = owner;
            ExpiresAt = expiresAt;
        }
    }

    // Dosya Yolu -> Kilidi Koyan Rol (Örn: "IDE", "DevGuard_Core")
    private readonly ConcurrentDictionary<string, LockInfo> _lockedFiles = new();

    // Yolları standartlaştırarak işletim sistemi bazlı (büyük/küçük harf, relative path) hatalarını önler
    private string NormalizePath(string filePath) => Path.GetFullPath(filePath).ToLowerInvariant();

    public bool TryLock(string filePath, string owner, TimeSpan? duration = null)
    {
        var normalizedPath = NormalizePath(filePath);
        // Varsayılan kilit süresi 5 dakikadır. Bu sayede işlemi yapan araç çökerse kilit sonsuza dek asılı kalmaz.
        var expiration = DateTime.UtcNow.Add(duration ?? TimeSpan.FromMinutes(5));
        var newLock = new LockInfo(owner, expiration);

        // Atomic (bölünemez) işlem: Eğer dosya kilitliyse süresine bak, süresi geçmişse eski kilidi ez.
        return _lockedFiles.AddOrUpdate(normalizedPath, newLock, (key, existingLock) => 
            existingLock.ExpiresAt < DateTime.UtcNow ? newLock : existingLock) == newLock;
    }

    public void Unlock(string filePath, string owner)
    {
        var normalizedPath = NormalizePath(filePath);
        if (_lockedFiles.TryGetValue(normalizedPath, out var currentLock) && currentLock.Owner == owner)
        {
            // Thread-safe bir şekilde sadece belirli kilit objesini (referansını) kaldırıyoruz
            var dict = (ICollection<KeyValuePair<string, LockInfo>>)_lockedFiles;
            dict.Remove(new KeyValuePair<string, LockInfo>(normalizedPath, currentLock));
        }
    }

    public bool IsLocked(string filePath)
    {
        var normalizedPath = NormalizePath(filePath);
        if (_lockedFiles.TryGetValue(normalizedPath, out var currentLock))
        {
            if (currentLock.ExpiresAt > DateTime.UtcNow) return true;
            
            // Süresi geçmişse tembel temizlik (Lazy Cleanup) yap
            _lockedFiles.TryRemove(normalizedPath, out _);
        }
        return false;
    }

    public string? GetLockOwner(string filePath)
    {
        var normalizedPath = NormalizePath(filePath);
        if (_lockedFiles.TryGetValue(normalizedPath, out var currentLock) && currentLock.ExpiresAt > DateTime.UtcNow)
        {
            return currentLock.Owner;
        }
        return null;
    }
}
