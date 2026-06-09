using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using SanitizerKit.Core.Security;

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

    public async Task<string> GenerateFixAsync(string rawCode, string errorMessage, string diagnosticInfo, string encryptedApiKey, string endpoint = "https://api.openai.com/v1/chat/completions")
    {
        // Güvenlik 1: Veri Maskeleme
        string maskedCode = LocalAiFirewall.MaskSensitiveData(rawCode);
        
        // Güvenlik 2: AES Şifre Çözücü
        string apiKey = LocalAiFirewall.DecryptApiKey(encryptedApiKey);
        
        if (string.IsNullOrEmpty(apiKey))
            return "{\"error\": \"API Anahtarı bulunamadı veya geçersiz.\"}";

        // Güvenlik 3: Privacy Guard (Ağ Muhafızı)
        using var client = PrivacyGuard.CreateSafeHttpClient(
            strictOfflineMode: false, 
            "api.openai.com", "api.anthropic.com", "api.groq.com", "generativelanguage.googleapis.com"
        );

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        var payload = new
        {
            model = "gpt-3.5-turbo",
            messages = new[]
            {
                new { role = "system", content = SystemPrompt },
                new { role = "user", content = $"Ajan 1 Teşhisi: {diagnosticInfo}\n\nHata Mesajı: {errorMessage}\n\nHatalı Kod:\n{maskedCode}" }
            },
            // Kod onarımı deterministik olmalı, halüsinasyonu engellemek için ısıyı (yaratıcılığı) sıfıra yakın (0.1) tutuyoruz.
            temperature = 0.1,
            response_format = new { type = "json_object" } // Sadece JSON dönmesini OpenAI API seviyesinde zorluyoruz
        };

        string jsonPayload = JsonSerializer.Serialize(payload);
        using var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

        try
        {
            using var response = await client.PostAsync(endpoint, content);
            if (!response.IsSuccessStatusCode)
            {
                return $"{{\"error\": \"API BAĞLANTI HATASI: {response.StatusCode}\"}}";
            }

            var responseJson = await response.Content.ReadAsStringAsync();
            using var document = JsonDocument.Parse(responseJson);
            
            var root = document.RootElement;
            if (root.TryGetProperty("choices", out var choices) && choices.ValueKind == JsonValueKind.Array && choices.GetArrayLength() > 0)
            {
                // Ajan 2'nin ürettiği saf JSON verisi (fixedCode ve suggestedRecipe barındırır)
                return choices[0].GetProperty("message").GetProperty("content").GetString() ?? "{\"error\": \"Yanıt boş.\"}";
            }
            
            return "{\"error\": \"API formatı anlaşılamadı.\"}";
        }
        catch (UnauthorizedAccessException ex)
        {
            return $"{{\"error\": \"GİZLİLİK ENGELLENMESİ: {ex.Message}\"}}";
        }
        catch (Exception ex)
        {
            return $"{{\"error\": \"BEKLENMEYEN HATA: {ex.Message}\"}}";
        }
    }
}
