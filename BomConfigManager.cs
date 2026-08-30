using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SanitizerKit.Core.Config;

[JsonSerializable(typeof(BomConfig))]
public partial class BomConfigJsonContext : JsonSerializerContext
{
}

public class BomConfig
{
    public bool AutoFix { get; set; } = false;
    public bool BackupEnabled { get; set; } = true;
    public bool StrictOfflineMode { get; set; } = true; // GİZLİLİK İLKESİ: Varsayılan olarak tüm dış ağ istekleri kapalıdır.
    public int TabSize { get; set; } = 4; // Tab başına eklenecek boşluk sayısı
    public List<string> ExcludedExtensions { get; set; } = new() { ".exe", ".dll", ".png", ".jpg", ".zip" };
    public Dictionary<string, string> CustomRules { get; set; } = new();
    public Dictionary<string, bool> EnabledModules { get; set; } = new() { { "AutoLogger", true }, { "SqliteLogger", false }, { "EntropyScanner", true } };
    public double AiTemperature { get; set; } = 0.2;
    public bool HasSeenWelcomeTour { get; set; } = false;
    public string AiProvider { get; set; } = "OpenAI";
    public string OllamaEndpoint { get; set; } = "http://localhost:11434";
    public string OllamaModel { get; set; } = "qwen2.5-coder:7b";

    // Yerel OpenAI-uyumlu sunucu (llama.cpp / vLLM). API anahtarı İSTEMEZ,
    // dış ağa çıkmaz — AiProvider = "LocalOpenAI" ile devreye girer.
    public string LocalEndpoint { get; set; } = "http://127.0.0.1:8090";
    public string LocalModel { get; set; } = "coder-14b";

    // Yanıt bekleme süresi (saniye). HttpClient varsayılanı 100 sn'dir; bulut için
    // bol, YEREL bir 14B için değil — 2026-08-30'da canlı testte 100 sn'de kesildi.
    public int AiTimeoutSeconds { get; set; } = 600;
    public string EncryptedApiKey { get; set; } = string.Empty;
    public string AccentColor { get; set; } = "#89B4FA";
}

public class BomConfigManager
{
    public static BomConfig LoadConfig(string configPath)
    {
        if (!File.Exists(configPath)) return new BomConfig();
        try
        {
            string json = File.ReadAllText(configPath);
            return JsonSerializer.Deserialize(json, BomConfigJsonContext.Default.BomConfig) ?? new BomConfig();
        }
        catch (JsonException ex)
        {
            // Bozuk JSON dosyasını konsola yansıt; varsayılan config ile devam et
            Console.WriteLine($"[DevGuard] .bomconfig dosyası okunamadı (bozuk JSON): {ex.Message}");
            return new BomConfig();
        }
        catch (IOException ex)
        {
            Console.WriteLine($"[DevGuard] .bomconfig dosyasına erişilemedi: {ex.Message}");
            return new BomConfig();
        }
    }

    public static void SaveConfig(string configPath, BomConfig config)
    {
        var options = new JsonSerializerOptions 
        { 
            WriteIndented = true,
            TypeInfoResolver = BomConfigJsonContext.Default
        };
        string json = JsonSerializer.Serialize(config, options);
        File.WriteAllText(configPath, json);
    }

    public static void ExportPortableConfig(string targetDirectory, string? sourceConfigPath = null)
    {
        string destConfigPath = Path.Combine(targetDirectory, ".bomconfig");
        string resolvedSource = sourceConfigPath ?? Path.Combine(Environment.CurrentDirectory, ".bomconfig");
        BomConfig config = LoadConfig(resolvedSource);
        SaveConfig(destConfigPath, config);
    }
}
