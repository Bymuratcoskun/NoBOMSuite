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

public class SecurityAiAgent
{
    private const string SystemPrompt = @"Sen DevGuard (NoBOMSuite) Sisteminin 'Ajan 3: Güvenlik ve Gizlilik Gardiyanı Ajanı'sın.
GÖREVİN: Ajan 2 tarafından üretilen çözüm kodunu denetlemek ve kullanıcının sistemine zarar verip vermeyeceğini doğrulamak.
KATI KURALLAR:
1. Üretilen çözümde herhangi bir güvenlik açığı (SQL Injection, XSS vb.), zararlı kod (malicious pattern), veri sızıntısı riski veya halüsinasyon (alakasız kod üretimi) varsa çözümü REDDET.
2. Çözüm tamamen güvenli ve orijinal sorunu çözmeye odaklıysa, SADECE 'GÜVENLİ' kelimesini döndür.
3. Eğer güvensizse, SADECE 'REDDEDİLDİ: <Sebep>' formatında yanıt ver. Başka hiçbir açıklama veya sohbet metni ekleme.";

    public async Task<string> ValidateFixAsync(string originalCode, string proposedFixJson, string encryptedApiKey, string endpoint = "https://api.openai.com/v1/chat/completions", HttpClient? customClient = null, double temperature = 0.0)
    {
        var config = BomConfigManager.LoadConfig(Path.Combine(Environment.CurrentDirectory, ".bomconfig"));
        bool isOllama = config.AiProvider.Equals("Ollama", StringComparison.OrdinalIgnoreCase);

        string apiKey = string.Empty;
        if (!isOllama)
        {
            apiKey = LocalAiFirewall.DecryptApiKey(encryptedApiKey);
            if (string.IsNullOrEmpty(apiKey))
                return "HATA: API Anahtarı bulunamadı veya geçersiz.";
        }

        using var defaultClient = customClient == null ? PrivacyGuard.CreateSafeHttpClient(
            strictOfflineMode: false,
            "api.openai.com", "api.anthropic.com", "api.groq.com", "generativelanguage.googleapis.com") : null;
        
        var client = customClient ?? defaultClient!;

        if (!isOllama)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        }

        // Endpoint belirle
        string finalEndpoint = isOllama && endpoint == "https://api.openai.com/v1/chat/completions"
            ? config.OllamaEndpoint.TrimEnd('/') + "/api/chat"
            : endpoint;

        object payload;
        if (isOllama)
        {
            payload = new
            {
                model = config.OllamaModel,
                messages = new[]
                {
                    new { role = "system", content = SystemPrompt },
                    new { role = "user", content = $"Orijinal Kod (Maskelenmiş):\n{LocalAiFirewall.MaskSensitiveData(originalCode)}\n\nAjan 2 Tarafından Önerilen Çözüm (JSON):\n{proposedFixJson}" }
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
                    new { role = "user", content = $"Orijinal Kod (Maskelenmiş):\n{LocalAiFirewall.MaskSensitiveData(originalCode)}\n\nAjan 2 Tarafından Önerilen Çözüm (JSON):\n{proposedFixJson}" }
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
                var response = await client.PostAsync(finalEndpoint, content);
                
                if (!response.IsSuccessStatusCode)
                {
                    if (attempt == maxAttempts)
                        return $"API BAĞLANTI HATASI: {response.StatusCode}";
                        
                    await Task.Delay(delayMs);
                    delayMs *= 2; // Üstel Bekleme (Exponential Backoff)
                    continue;
                }

                var responseJson = await response.Content.ReadAsStringAsync();
                using var document = JsonDocument.Parse(responseJson);
                
                var root = document.RootElement;
                if (isOllama)
                {
                    if (root.TryGetProperty("message", out var messageProp) && messageProp.TryGetProperty("content", out var contentProp))
                    {
                        string result = contentProp.GetString() ?? "";
                        return result.Trim();
                    }
                }
                else
                {
                    if (root.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
                    {
                        string result = choices[0].GetProperty("message").GetProperty("content").GetString() ?? "";
                        return result.Trim(); // Başındaki ve sonundaki boşlukları silerek sadece GÜVENLİ veya REDDEDİLDİ kısmını alırız.
                    }
                }
                
                return "API formatı anlaşılamadı.";
            }
            catch (UnauthorizedAccessException ex)
            {
                return $"GİZLİLİK ENGELLENMESİ: {ex.Message}";
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
