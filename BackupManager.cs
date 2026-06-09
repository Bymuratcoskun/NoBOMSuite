using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace SanitizerKit.Core.Backups;

public class BackupManifest
{
    public string SessionId { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public Dictionary<string, string> FileMap { get; set; } = []; // YedekDosyaYolu -> OrjinalDosyaYolu
}

public class BackupManager
{
    private readonly string _baseBackupDir;
    private readonly string _sessionId;
    private readonly string _sessionBackupDir;
    private readonly BackupManifest _manifest;

    public BackupManager(string targetDirectory)
    {
        _baseBackupDir = Path.Combine(targetDirectory, ".nobom", "backups");
        _sessionId = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        _sessionBackupDir = Path.Combine(_baseBackupDir, _sessionId);
        
        _manifest = new BackupManifest 
        { 
            SessionId = _sessionId,
            Timestamp = DateTime.UtcNow
        };
    }

    public void BackupFile(string originalFilePath)
    {
        if (!File.Exists(originalFilePath)) return;

        if (!Directory.Exists(_sessionBackupDir))
        {
            Directory.CreateDirectory(_sessionBackupDir);
        }

        string fileName = Path.GetFileName(originalFilePath);
        string uniqueFileName = $"{Guid.NewGuid():N}_{fileName}";
        string backupFilePath = Path.Combine(_sessionBackupDir, uniqueFileName);

        File.Copy(originalFilePath, backupFilePath, overwrite: true);
        _manifest.FileMap[backupFilePath] = originalFilePath;
        
        SaveManifest();
    }

    private void SaveManifest()
    {
        string manifestPath = Path.Combine(_sessionBackupDir, "manifest.json");
        string json = JsonSerializer.Serialize(_manifest, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(manifestPath, json);
    }

    public bool Rollback()
    {
        string manifestPath = Path.Combine(_sessionBackupDir, "manifest.json");
        if (!File.Exists(manifestPath)) return false;

        try
        {
            foreach (var kvp in _manifest.FileMap)
            {
                string backupFilePath = kvp.Key;
                string originalFilePath = kvp.Value;

                if (File.Exists(backupFilePath))
                {
                    // Orijinal dosyanın klasörü (örneğin kullanıcı kazara klasörü silmişse) yoksa oluştur
                    string? originalDir = Path.GetDirectoryName(originalFilePath);
                    if (!string.IsNullOrEmpty(originalDir) && !Directory.Exists(originalDir))
                    {
                        Directory.CreateDirectory(originalDir);
                    }

                    // Yedeği orijinalin üzerine yazarak kurtar
                    File.Copy(backupFilePath, originalFilePath, overwrite: true);
                }
            }
            return true;
        }
        catch
        {
            return false;
        }
    }
}
