using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using SanitizerKit.Core.Security;

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

    public async Task<string> AnalyzeIssueAsync(string rawCode, string errorMessage, string encryptedApiKey, string endpoint = "https://api.openai.com/v1/chat/completions")
    {
        // 1. GÜVENLİK ADIMI: Kodu yapay zekaya (buluta) göndermeden önce şifre ve anahtarları maskele!
        string maskedCode = LocalAiFirewall.MaskSensitiveData(rawCode);

        // 2. GÜVENLİK ADIMI: Şifrelenmiş API anahtarını yerelde o anki bilgisayar için çöz.
        string apiKey = LocalAiFirewall.DecryptApiKey(encryptedApiKey);
        if (string.IsNullOrEmpty(apiKey))
            return "HATA: API Anahtarı çözülemedi veya bulunamadı.";

        // 3. GÜVENLİK ADIMI: PrivacyGuard ile yalnızca izinli LLM adreslerine çıkış yapabilen izole HTTP Client kullan.
        // Bu sayede uygulamaya zararlı bir kod sızsa bile 'hacker.com' adresine veri çalamaz.
        using var client = PrivacyGuard.CreateSafeHttpClient(
            strictOfflineMode: false, 
            "api.openai.com", "api.anthropic.com", "api.groq.com", "generativelanguage.googleapis.com"
        );

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        // OpenAI uyumlu evrensel JSON Payload'u
        var payload = new
        {
            model = "gpt-3.5-turbo", // Opsiyonel: Kullanıcı ayarlardan farklı modeller seçebilir.
            messages = new[]
            {
                new { role = "system", content = SystemPrompt },
                new { role = "user", content = $"Hata Mesajı: {errorMessage}\n\nİncelenecek Kod (Maskelenmiş):\n```\n{maskedCode}\n```" }
            },
            temperature = 0.2 // Halüsinasyonu engellemek için yaratıcılığı düşük seviyede (0.2) tutuyoruz.
        };

        string jsonPayload = JsonSerializer.Serialize(payload);
        using var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

        try
        {
            using var response = await client.PostAsync(endpoint, content);
            if (!response.IsSuccessStatusCode)
            {
                return $"API BAĞLANTI HATASI: {response.StatusCode} - {await response.Content.ReadAsStringAsync()}";
            }

            var responseJson = await response.Content.ReadAsStringAsync();
            using var document = JsonDocument.Parse(responseJson);
            
            var root = document.RootElement;
            if (root.TryGetProperty("choices", out var choices) && choices.ValueKind == JsonValueKind.Array && choices.GetArrayLength() > 0)
            {
                return choices[0].GetProperty("message").GetProperty("content").GetString() ?? "Yanıt alınamadı.";
            }
            
            return "API formatı anlaşılamadı.";
        }
        catch (UnauthorizedAccessException ex)
        {
            return ex.Message; // PrivacyGuard (Ağ Muhafızı) tarafından engellenirse
        }
        catch (Exception ex)
        {
            return $"BEKLENMEYEN HATA: {ex.Message}";
        }
    }
}