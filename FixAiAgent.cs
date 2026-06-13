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

public class FixAiAgent
{
    // Bu ajan yalnızca makine-okunabilir JSON üretmeye programlanmıştır.
    private const string SystemPrompt = @"Sen DevGuard (NoBOMSuite) Sisteminin 'Ajan 2: Çözüm, Öneri ve Kural Üretim Ajanı'sın.
GÖREVİN: Ajan 1'in yaptığı teşhisi baz alarak hatalı kodu düzeltmek ve/veya o hatayı gelecekte otomatik çözecek bir Regex tabanlı Reçete (Patch) kuralı üretmek.
KATI KURALLAR:
1. Asla laf kalabalığı yapma, açıklama veya teorik bilgi verme. (Bu Ajan 1'in işiydi).
2. Çıktını HER ZAMAN ve SADECE aşağıdaki JSON formatında ver. JSON formatı dışına çıkan tek bir harf dahi ekleme:
{
  ""fixedCode"": ""Düzeltilmiş tam kod bloğu"",
  ""suggestedRecipe"": {
    ""ruleName"": ""Kural_Adi"",
    ""regexPattern"": ""Aranacak_Regex"",
    ""replacement"": ""Yerine_Konacak_Metin""
  }
}
Eğer hatanın genel bir yama kuralına (Regex) dönüştürülmesi mantıklı veya mümkün değilse, 'suggestedRecipe' kısmını null bırakabilirsin.";

    public async Task<string> GenerateFixAsync(string rawCode, string errorMessage, string diagnosticInfo, string encryptedApiKey, string endpoint = "https://api.openai.com/v1/chat/completions", HttpClient? customClient = null, double temperature = 0.1)
    {
        // Güvenlik 1: Veri Maskeleme
        string maskedCode = LocalAiFirewall.MaskSensitiveData(rawCode);
        
        var config = BomConfigManager.LoadConfig(Path.Combine(Environment.CurrentDirectory, ".bomconfig"));
        bool isOllama = config.AiProvider.Equals("Ollama", StringComparison.OrdinalIgnoreCase);

        // Güvenlik 2: AES Şifre Çözücü
        string apiKey = string.Empty;
        if (!isOllama)
        {
            apiKey = LocalAiFirewall.DecryptApiKey(encryptedApiKey);
            if (string.IsNullOrEmpty(apiKey))
                return "{\"error\": \"API Anahtarı bulunamadı veya geçersiz.\"}";
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
                    new { role = "user", content = $"Ajan 1 Teşhisi: {diagnosticInfo}\n\nHata Mesajı: {errorMessage}\n\nHatalı Kod:\n{maskedCode}" }
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
                    new { role = "user", content = $"Ajan 1 Teşhisi: {diagnosticInfo}\n\nHata Mesajı: {errorMessage}\n\nHatalı Kod:\n{maskedCode}" }
                },
                temperature = temperature,
                response_format = new { type = "json_object" }
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
                        return $"{{\"error\": \"API BAĞLANTI HATASI: {response.StatusCode}\"}}";
                        
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
                        return contentProp.GetString() ?? "{\"error\": \"Yanıt boş.\"}";
                    }
                }
                else
                {
                    if (root.TryGetProperty("choices", out var choices) && choices.ValueKind == JsonValueKind.Array && choices.GetArrayLength() > 0)
                    {
                        return choices[0].GetProperty("message").GetProperty("content").GetString() ?? "{\"error\": \"Yanıt boş.\"}";
                    }
                }
                
                return "{\"error\": \"API formatı anlaşılamadı.\"}";
            }
            catch (UnauthorizedAccessException ex)
            {
                return $"{{\"error\": \"GİZLİLİK ENGELLENMESİ: {ex.Message}\"}}";
            }
            catch (Exception ex)
            {
                if (attempt == maxAttempts)
                    return $"{{\"error\": \"BEKLENMEYEN HATA: {ex.Message}\"}}";
                    
                await Task.Delay(delayMs);
                delayMs *= 2;
            }
        }
        
        return "{\"error\": \"API yanıt vermedi.\"}";
    }
}
