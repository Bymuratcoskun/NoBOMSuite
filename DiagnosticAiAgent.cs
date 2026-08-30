using System.Net.Http;
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

    public async Task<string> AnalyzeIssueAsync(string rawCode, string errorMessage, string encryptedApiKey, string endpoint = "https://api.openai.com/v1/chat/completions", HttpClient? customClient = null, double temperature = 0.2)
    {
        string maskedCode = LocalAiFirewall.MaskSensitiveData(rawCode);

        return await AiTasiyici.SorAsync(
            SystemPrompt,
            $"Hata Mesajı: {errorMessage}\n\nİncelenecek Kod (Maskelenmiş):\n```\n{maskedCode}\n```",
            encryptedApiKey, endpoint, customClient, temperature);
    }
}
