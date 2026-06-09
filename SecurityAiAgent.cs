using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using SanitizerKit.Core.Security;

namespace SanitizerKit.Core.AI;

public class SecurityAiAgent
{
    private const string SystemPrompt = @"Sen DevGuard (NoBOMSuite) Sisteminin 'Ajan 3: Güvenlik ve Gizlilik Gardiyanı Ajanı'sın.
GÖREVİN: Ajan 2 tarafından üretilen çözüm kodunu denetlemek ve kullanıcının sistemine zarar verip vermeyeceğini doğrulamak.
KATI KURALLAR:
1. Üretilen çözümde herhangi bir güvenlik açığı (SQL Injection, XSS vb.), zararlı kod (malicious pattern), veri sızıntısı riski veya halüsinasyon (alakasız kod üretimi) varsa çözümü REDDET.
2. Çözüm tamamen güvenli ve orijinal sorunu çözmeye odaklıysa, SADECE 'GÜVENLİ' kelimesini döndür.
3. Eğer güvensizse, SADECE 'REDDEDİLDİ: <Sebep>' formatında yanıt ver. Başka hiçbir açıklama veya sohbet metni ekleme.";

    public async Task<string> ValidateFixAsync(string originalCode, string proposedFixJson, string encryptedApiKey, string endpoint = "https://api.openai.com/v1/chat/completions")
    {
        string apiKey = LocalAiFirewall.DecryptApiKey(encryptedApiKey);
        
        if (string.IsNullOrEmpty(apiKey))
            return "HATA: API Anahtarı bulunamadı veya geçersiz.";

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
                new { role = "user", content = $"Orijinal Kod (Maskelenmiş):\n{LocalAiFirewall.MaskSensitiveData(originalCode)}\n\nAjan 2 Tarafından Önerilen Çözüm (JSON):\n{proposedFixJson}" }
            },
            // Güvenlik denetimi sıfır halüsinasyon ile, %100 deterministik olmalı.
            temperature = 0.0 
        };

        string jsonPayload = JsonSerializer.Serialize(payload);
        var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

        try
        {
            var response = await client.PostAsync(endpoint, content);
            if (!response.IsSuccessStatusCode)
            {
                return $"API BAĞLANTI HATASI: {response.StatusCode}";
            }

            var responseJson = await response.Content.ReadAsStringAsync();
            using var document = JsonDocument.Parse(responseJson);
            
            var root = document.RootElement;
            if (root.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
            {
                string result = choices[0].GetProperty("message").GetProperty("content").GetString() ?? "";
                return result.Trim(); // Başındaki ve sonundaki boşlukları silerek sadece GÜVENLİ veya REDDEDİLDİ kısmını alırız.
            }
            
            return "API formatı anlaşılamadı.";
        }
        catch (UnauthorizedAccessException ex)
        {
            return $"GİZLİLİK ENGELLENMESİ: {ex.Message}";
        }
        catch (Exception ex)
        {
            return $"BEKLENMEYEN HATA: {ex.Message}";
        }
    }
}
