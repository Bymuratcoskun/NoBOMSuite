using System.Net.Http;
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

    public async Task<string> GenerateFixAsync(string rawCode, string errorMessage, string diagnosticInfo, string encryptedApiKey, string endpoint = "https://api.openai.com/v1/chat/completions", HttpClient? customClient = null, double temperature = 0.3)
    {
        string maskedCode = LocalAiFirewall.MaskSensitiveData(rawCode);

        return await AiTasiyici.SorAsync(
            SystemPrompt,
            $"Hata: {errorMessage}\nTeşhis: {diagnosticInfo}\nKod:\n{maskedCode}",
            encryptedApiKey, endpoint, customClient, temperature);
    }
}
