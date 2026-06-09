using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace SanitizerKit.Core.Config;

public class BomConfig
{
    public bool AutoFix { get; set; } = false;
    public bool BackupEnabled { get; set; } = true;
    public bool StrictOfflineMode { get; set; } = true; // GİZLİLİK İLKESİ: Varsayılan olarak tüm dış ağ istekleri kapalıdır.
    public int TabSize { get; set; } = 4; // Tab başına eklenecek boşluk sayısı
    public List<string> ExcludedExtensions { get; set; } = new() { ".exe", ".dll", ".png", ".jpg", ".zip" };
    public Dictionary<string, string> CustomRules { get; set; } = new();
}

public class BomConfigManager
{
    public static BomConfig LoadConfig(string configPath)
    {
        if (!File.Exists(configPath)) return new BomConfig();
        try
        {
            string json = File.ReadAllText(configPath);
            return JsonSerializer.Deserialize<BomConfig>(json) ?? new BomConfig();
        }
        catch
        {
            return new BomConfig();
        }
    }

    public static void SaveConfig(string configPath, BomConfig config)
    {
        var options = new JsonSerializerOptions { WriteIndented = true };
        string json = JsonSerializer.Serialize(config, options);
        File.WriteAllText(configPath, json);
    }

    public static void ExportPortableConfig(string targetDirectory)
    {
        string configPath = Path.Combine(targetDirectory, ".bomconfig");
        // Mevcut yapılandırmayı (veya varsayılanı) taşınabilir dizine aktarıyoruz.
        SaveConfig(configPath, new BomConfig()); 
    }
}
