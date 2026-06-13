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

public class FixRecipeAiAgent
{
    private const string SystemPrompt = @"Sen DevGuard (NoBOMSuite) Sisteminin 'Ajan 2: Çözüm, Öneri ve Kural Üretim Ajanı'sın.
GÖREVİN: Ajan 1'in teşhis ettiği hatayı kalıcı olarak çözecek kodu veya Patch Generator için bir Regex/Reçete kuralı üretmek.
KATI KURALLAR:
1. Yanıtını SADECE geçerli bir JSON formatında döndür. Asla markdown (```json) veya ekstra açıklama ekleme.
2. Format şu şekilde olmalıdır: { ""action"": ""replace_code"" | ""generate_regex"", ""payload"": ""düzeltilmiş kod veya regex kuralı"" }";

    public async Task<string> GenerateFixAsync(string rawCode, string errorMessage, string diagnosticInfo, string encryptedApiKey, string endpoint = "https://api.openai.com/v1/chat/completions", HttpClient? customClient = null, double temperature = 0.3)
    {
        string maskedCode = LocalAiFirewall.MaskSensitiveData(rawCode);
        
        var config = BomConfigManager.LoadConfig(Path.Combine(Environment.CurrentDirectory, ".bomconfig"));
        bool isOllama = config.AiProvider.Equals("Ollama", StringComparison.OrdinalIgnoreCase);

        string apiKey = string.Empty;
        if (!isOllama)
        {
            apiKey = LocalAiFirewall.DecryptApiKey(encryptedApiKey);
            if (string.IsNullOrEmpty(apiKey)) return "{\"error\": \"API Anahtarı bulunamadı.\"}";
        }

        using var defaultClient = customClient == null ? PrivacyGuard.CreateSafeHttpClient(
            strictOfflineMode: false, "api.openai.com", "api.anthropic.com", "api.groq.com", "generativelanguage.googleapis.com") : null;
            
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
                    new { role = "user", content = $"Hata: {errorMessage}\nTeşhis: {diagnosticInfo}\nKod:\n{maskedCode}" }
                },
                stream = false,
                format = "json",
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
                    new { role = "user", content = $"Hata: {errorMessage}\nTeşhis: {diagnosticInfo}\nKod:\n{maskedCode}" }
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
                        return $"{{\"error\": \"API Hatası: {response.StatusCode}\"}}";
                        
                    await Task.Delay(delayMs);
                    delayMs *= 2;
                    continue;
                }
                
                var responseJson = await response.Content.ReadAsStringAsync();
                using var document = JsonDocument.Parse(responseJson);
                
                var root = document.RootElement;
                if (isOllama)
                {
                    if (root.TryGetProperty("message", out var messageProp) && messageProp.TryGetProperty("content", out var contentProp))
                    {
                        return contentProp.GetString() ?? "{}";
                    }
                }
                else
                {
                    if (root.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
                        return choices[0].GetProperty("message").GetProperty("content").GetString() ?? "{}";
                }
                
                return "{\"error\": \"Geçersiz yanıt\"}";
            }
            catch (UnauthorizedAccessException ex)
            {
                return $"{{\"error\": \"GİZLİLİK ENGELLENMESİ: {ex.Message}\"}}";
            }
            catch (Exception ex)
            {
                if (attempt == maxAttempts)
                    return $"{{\"error\": \"{ex.Message}\"}}";
                    
                await Task.Delay(delayMs);
                delayMs *= 2;
            }
        }
        
        return "{\"error\": \"API yanıt vermedi.\"}";
    }
}