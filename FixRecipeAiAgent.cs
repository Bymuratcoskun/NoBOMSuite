using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using SanitizerKit.Core.Security;

namespace SanitizerKit.Core.AI;

public class FixRecipeAiAgent
{
    private const string SystemPrompt = @"Sen DevGuard (NoBOMSuite) Sisteminin 'Ajan 2: Çözüm, Öneri ve Kural Üretim Ajanı'sın.
GÖREVİN: Ajan 1'in teşhis ettiği hatayı kalıcı olarak çözecek kodu veya Patch Generator için bir Regex/Reçete kuralı üretmek.
KATI KURALLAR:
1. Yanıtını SADECE geçerli bir JSON formatında döndür. Asla markdown (```json) veya ekstra açıklama ekleme.
2. Format şu şekilde olmalıdır: { ""action"": ""replace_code"" | ""generate_regex"", ""payload"": ""düzeltilmiş kod veya regex kuralı"" }";

    public async Task<string> GenerateFixAsync(string rawCode, string errorMessage, string diagnosticInfo, string encryptedApiKey, string endpoint = "https://api.openai.com/v1/chat/completions")
    {
        string maskedCode = LocalAiFirewall.MaskSensitiveData(rawCode);
        string apiKey = LocalAiFirewall.DecryptApiKey(encryptedApiKey);
        
        if (string.IsNullOrEmpty(apiKey)) return "{\"error\": \"API Anahtarı bulunamadı.\"}";

        using var client = PrivacyGuard.CreateSafeHttpClient(false, "api.openai.com", "api.anthropic.com", "api.groq.com", "generativelanguage.googleapis.com");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        var payload = new
        {
            model = "gpt-3.5-turbo",
            messages = new[]
            {
                new { role = "system", content = SystemPrompt },
                new { role = "user", content = $"Hata: {errorMessage}\nTeşhis: {diagnosticInfo}\nKod:\n{maskedCode}" }
            },
            temperature = 0.3 // Kararlı ve hatasız kod üretimi için düşük sıcaklık
        };

        string jsonPayload = JsonSerializer.Serialize(payload);
        using var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

        try
        {
            var response = await client.PostAsync(endpoint, content);
            if (!response.IsSuccessStatusCode) return $"{{\"error\": \"API Hatası: {response.StatusCode}\"}}";
            
            var responseJson = await response.Content.ReadAsStringAsync();
            using var document = JsonDocument.Parse(responseJson);
            
            if (document.RootElement.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
                return choices[0].GetProperty("message").GetProperty("content").GetString() ?? "{}";
            
            return "{\"error\": \"Geçersiz yanıt\"}";
        }
        catch (Exception ex)
        {
            return $"{{\"error\": \"{ex.Message}\"}}";
        }
    }
}