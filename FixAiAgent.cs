using System.Net.Http;
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

    public async Task<string> GenerateFixAsync(string rawCode, string errorMessage, string diagnosticInfo, string encryptedApiKey, string endpoint = "https://api.openai.com/v1/chat/completions", HttpClient? customClient = null, double temperature = 0.1)
    {
        string maskedCode = LocalAiFirewall.MaskSensitiveData(rawCode);

        return await AiTasiyici.SorAsync(
            SystemPrompt,
            $"Ajan 1 Teşhisi: {diagnosticInfo}\n\nHata Mesajı: {errorMessage}\n\nHatalı Kod:\n{maskedCode}",
            encryptedApiKey, endpoint, customClient, temperature);
    }
}
