using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using SanitizerKit.Core.Config;
using SanitizerKit.Core.Security;

namespace SanitizerKit.Core.AI;

/// <summary>Yapay zekâ sağlayıcı kipleri.</summary>
public enum AiKip
{
    /// <summary>Bulut (OpenAI uyumlu, API anahtarı ŞART, dış ağa çıkar).</summary>
    Bulut,
    /// <summary>Yerel Ollama (<c>/api/chat</c> lehçesi, anahtar istemez).</summary>
    Ollama,
    /// <summary>Yerel OpenAI-uyumlu sunucu (llama.cpp / vLLM — <c>/v1/chat/completions</c>, anahtar istemez).</summary>
    YerelOpenAI
}

/// <summary>
/// Dört AI ajanının ortak taşıyıcısı: kip çözümü, uç seçimi, gövde kurulumu,
/// yanıt ayıklama ve yeniden deneme tek yerde toplanır.
///
/// Daha önce bu mantık dört dosyada kopyalanmıştı; yeni bir sağlayıcı eklemek
/// dört ayrı düzenleme gerektiriyordu.
/// </summary>
public static class AiTasiyici
{
    public const string BulutUcu = "https://api.openai.com/v1/chat/completions";

    /// <summary>Yapılandırmadaki sağlayıcı adını kipe çevirir. Tanınmayan ad → Bulut (eski davranış).</summary>
    public static AiKip KipCoz(string? saglayici) => (saglayici ?? string.Empty).Trim().ToLowerInvariant() switch
    {
        "ollama" => AiKip.Ollama,
        "localopenai" or "local" or "yerel" or "localai" or "llamacpp" => AiKip.YerelOpenAI,
        _ => AiKip.Bulut
    };

    /// <summary>Kip dış ağa çıkıyor mu? Yalnız Bulut çıkar.</summary>
    public static bool DisAgaCikar(AiKip kip) => kip == AiKip.Bulut;

    /// <summary>Uç adresini çözer. Çağıran açıkça bir uç geçtiyse ona dokunulmaz.</summary>
    public static string UcCoz(AiKip kip, BomConfig config, string endpoint)
    {
        if (endpoint != BulutUcu) return endpoint;   // çağıran bilinçli olarak başka bir uç verdi
        return kip switch
        {
            AiKip.Ollama => config.OllamaEndpoint.TrimEnd('/') + "/api/chat",
            AiKip.YerelOpenAI => config.LocalEndpoint.TrimEnd('/') + "/v1/chat/completions",
            _ => BulutUcu
        };
    }

    /// <summary>İstek gövdesini kipin lehçesine göre kurar.</summary>
    public static object GovdeKur(AiKip kip, BomConfig config, string sistemPrompt, string kullaniciPrompt, double temperature)
    {
        var mesajlar = new[]
        {
            new { role = "system", content = sistemPrompt },
            new { role = "user", content = kullaniciPrompt }
        };

        // Ollama sıcaklığı `options` altında, OpenAI lehçesi kökte bekler.
        if (kip == AiKip.Ollama)
            return new { model = config.OllamaModel, messages = mesajlar, stream = false, options = new { temperature } };

        string model = kip == AiKip.YerelOpenAI ? config.LocalModel : "gpt-4o-mini";
        return new { model, messages = mesajlar, temperature };
    }

    /// <summary>Yanıt gövdesinden metni ayıklar. Ayıklanamazsa null.</summary>
    public static string? YanitCoz(AiKip kip, JsonElement root)
    {
        if (kip == AiKip.Ollama)
        {
            return root.TryGetProperty("message", out var m) && m.TryGetProperty("content", out var c)
                ? c.GetString()
                : null;
        }

        return root.TryGetProperty("choices", out var choices)
               && choices.ValueKind == JsonValueKind.Array
               && choices.GetArrayLength() > 0
               && choices[0].TryGetProperty("message", out var msg)
               && msg.TryGetProperty("content", out var content)
            ? content.GetString()
            : null;
    }

    /// <summary>Kipe uygun HttpClient kurar. Yerel kiplerde dış ağ TAMAMEN kapalıdır.</summary>
    public static HttpClient IstemciKur(AiKip kip, BomConfig config)
    {
        var istemci = DisAgaCikar(kip)
            ? PrivacyGuard.CreateSafeHttpClient(
                strictOfflineMode: config.StrictOfflineMode,
                "api.openai.com", "api.anthropic.com", "api.groq.com", "generativelanguage.googleapis.com")
            : PrivacyGuard.CreateSafeHttpClient(strictOfflineMode: true); // yalnız localhost/127.0.0.1

        // HttpClient varsayılanı 100 sn. Yerel bir 14B paylaşımlı APU'da bunu rahatça
        // aşar; 2026-08-30 canlı testinde üç denemenin ÜÇÜ de 100 sn'de kesildi.
        if (config.AiTimeoutSeconds > 0)
            istemci.Timeout = TimeSpan.FromSeconds(config.AiTimeoutSeconds);

        return istemci;
    }

    /// <summary>
    /// Tek soru–tek cevap. Ağ hatalarında üstel bekleme ile 3 deneme yapar;
    /// gizlilik muhafızı engellerse tekrar DENEMEZ.
    /// </summary>
    public static async Task<string> SorAsync(
        string sistemPrompt,
        string kullaniciPrompt,
        string encryptedApiKey,
        string endpoint = BulutUcu,
        HttpClient? customClient = null,
        double temperature = 0.2,
        BomConfig? config = null)
    {
        config ??= BomConfigManager.LoadConfig(Path.Combine(Environment.CurrentDirectory, ".bomconfig"));
        var kip = KipCoz(config.AiProvider);

        // Kullanıcı %100 çevrimdışı istemişse bulut yolu AÇILMAZ. Eskiden ajanlar
        // bu ayarı okumayıp `strictOfflineMode: false` yazıyordu; ayar sessizce eziliyordu.
        if (DisAgaCikar(kip) && config.StrictOfflineMode)
            return "GİZLİLİK: %100 çevrimdışı kip açıkken bulut sağlayıcı kullanılamaz. " +
                   "AiProvider'ı 'LocalOpenAI' yapın ya da StrictOfflineMode'u kapatın.";

        string apiKey = string.Empty;
        if (DisAgaCikar(kip))
        {
            apiKey = LocalAiFirewall.DecryptApiKey(encryptedApiKey);
            if (string.IsNullOrEmpty(apiKey))
                return "HATA: API Anahtarı çözülemedi veya bulunamadı.";
        }

        using var defaultClient = customClient == null ? IstemciKur(kip, config) : null;
        var client = customClient ?? defaultClient!;

        if (DisAgaCikar(kip))
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        string finalEndpoint = UcCoz(kip, config, endpoint);
        string jsonPayload = JsonSerializer.Serialize(GovdeKur(kip, config, sistemPrompt, kullaniciPrompt, temperature));

        int maxAttempts = 3;
        int delayMs = 1000;

        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                using var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
                using var response = await client.PostAsync(finalEndpoint, content);

                if (!response.IsSuccessStatusCode)
                {
                    if (attempt == maxAttempts)
                        return $"API BAĞLANTI HATASI: {response.StatusCode} - {await response.Content.ReadAsStringAsync()}";

                    await Task.Delay(delayMs);
                    delayMs *= 2;
                    continue;
                }

                var responseJson = await response.Content.ReadAsStringAsync();
                using var document = JsonDocument.Parse(responseJson);
                return YanitCoz(kip, document.RootElement) ?? "API formatı anlaşılamadı.";
            }
            catch (UnauthorizedAccessException ex)
            {
                return ex.Message; // ağ muhafızı engelledi — tekrar denemenin anlamı yok
            }
            catch (Exception ex)
            {
                if (attempt == maxAttempts)
                    return $"BEKLENMEYEN HATA: {ex.Message}";

                await Task.Delay(delayMs);
                delayMs *= 2;
            }
        }

        return "API yanıt vermedi.";
    }
}
