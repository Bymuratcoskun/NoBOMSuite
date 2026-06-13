using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.IO;
using SanitizerKit.Core.Security;
using SanitizerKit.Core.Config;

namespace SanitizerKit.Core.AI;

public class DiagnosticAiAgent
{
    // Halüsinasyon riskini sıfıra indiren ve rolü kesin bir şekilde çizen "System Prompt"
    private const string SystemPrompt = @"Sen DevGuard (NoBOMSuite) Sisteminin 'Ajan 1: Teşhis ve Genel Bilgilendirme Ajanı'sın.
GÖREVİN: Geliştiricinin karşılaştığı hatanın teorik nedenini, standartları (POSIX, UTF-8 vb.) baz alarak açıklamak.
KATI KURALLAR:
1. KESİNLİKLE düzeltilmiş kodu (çözümü) yazma! Kod düzeltmek senin yetkinde değildir, bu Ajan 2'nin görevidir.
2. Sadece hatanın neden kaynaklandığını (Kök Neden Analizi) dökümantasyon tarzında açıkla.
3. Kısa, öz ve profesyonel bir dil kullan.
4. Analizini bitirdikten sonra kullanıcıya 'Onarım için Ajan 2'ye devredebilirsiniz' şeklinde yönlendirme yap.";

    public async Task<string> AnalyzeIssueAsync(string rawCode, string errorMessage, string encryptedApiKey, string endpoint = "https://api.openai.com/v1/chat/completions", HttpClient? customClient = null, double temperature = 0.2)
    {
        // 1. GÜVENLİK ADIMI: Kodu yapay zekaya (buluta) göndermeden önce şifre ve anahtarları maskele!
        string maskedCode = LocalAiFirewall.MaskSensitiveData(rawCode);

        // Yapılandırmayı oku
        var config = BomConfigManager.LoadConfig(Path.Combine(Environment.CurrentDirectory, ".bomconfig"));
        bool isOllama = config.AiProvider.Equals("Ollama", StringComparison.OrdinalIgnoreCase);

        // 2. GÜVENLİK ADIMI: Şifrelenmiş API anahtarını yerelde o anki bilgisayar için çöz (OpenAI ise).
        string apiKey = string.Empty;
        if (!isOllama)
        {
            apiKey = LocalAiFirewall.DecryptApiKey(encryptedApiKey);
            if (string.IsNullOrEmpty(apiKey))
                return "HATA: API Anahtarı çözülemedi veya bulunamadı.";
        }

        using var defaultClient = customClient == null ? PrivacyGuard.CreateSafeHttpClient(
            strictOfflineMode: false,
            "api.openai.com", "api.anthropic.com", "api.groq.com", "generativelanguage.googleapis.com") : null;
        
        var client = customClient ?? defaultClient!;

        if (!isOllama)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        }

        // Endpoint belirle: Eğer varsayılan OpenAI endpoint'i geçildiyse ve Ollama seçildiyse Ollama endpoint'ine yönlendir.
        string finalEndpoint = isOllama && endpoint == "https://api.openai.com/v1/chat/completions"
            ? config.OllamaEndpoint.TrimEnd('/') + "/api/chat"
            : endpoint;

        // Sağlayıcıya göre JSON payload'u oluştur
        object payload;
        if (isOllama)
        {
            payload = new
            {
                model = config.OllamaModel,
                messages = new[]
                {
                    new { role = "system", content = SystemPrompt },
                    new { role = "user", content = $"Hata Mesajı: {errorMessage}\n\nİncelenecek Kod (Maskelenmiş):\n```\n{maskedCode}\n```" }
                },
                stream = false,
                options = new { temperature = temperature }
            };
        }
        else
        {
            payload = new
            {
                model = "gpt-4o-mini",
                messages = new[]
                {
                    new { role = "system", content = SystemPrompt },
                    new { role = "user", content = $"Hata Mesajı: {errorMessage}\n\nİncelenecek Kod (Maskelenmiş):\n```\n{maskedCode}\n```" }
                },
                temperature = temperature
            };
        }

        string jsonPayload = JsonSerializer.Serialize(payload);

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
                    delayMs *= 2; // Üstel bekleme (Exponential Backoff)
                    continue;
                }

                var responseJson = await response.Content.ReadAsStringAsync();
                using var document = JsonDocument.Parse(responseJson);
                
                var root = document.RootElement;
                if (isOllama)
                {
                    if (root.TryGetProperty("message", out var messageProp) && messageProp.TryGetProperty("content", out var contentProp))
                    {
                        return contentProp.GetString() ?? "Yanıt alınamadı.";
                    }
                }
                else
                {
                    if (root.TryGetProperty("choices", out var choices) && choices.ValueKind == JsonValueKind.Array && choices.GetArrayLength() > 0)
                    {
                        return choices[0].GetProperty("message").GetProperty("content").GetString() ?? "Yanıt alınamadı.";
                    }
                }
                
                return "API formatı anlaşılamadı.";
            }
            catch (UnauthorizedAccessException ex)
            {
                return ex.Message; // PrivacyGuard (Ağ Muhafızı) tarafından engellenirse tekrar deneme yapma
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