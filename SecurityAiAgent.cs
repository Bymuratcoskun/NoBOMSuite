using System.Net.Http;
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

    public async Task<string> ValidateFixAsync(string originalCode, string proposedFixJson, string encryptedApiKey, string endpoint = "https://api.openai.com/v1/chat/completions", HttpClient? customClient = null, double temperature = 0.0)
    {
        return await AiTasiyici.SorAsync(
            SystemPrompt,
            $"Orijinal Kod (Maskelenmiş):\n{LocalAiFirewall.MaskSensitiveData(originalCode)}\n\nAjan 2 Tarafından Önerilen Çözüm (JSON):\n{proposedFixJson}",
            encryptedApiKey, endpoint, customClient, temperature);
    }
}
