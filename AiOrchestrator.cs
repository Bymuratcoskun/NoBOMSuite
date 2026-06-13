using System;
using System.Threading.Tasks;
using System.IO;
using SanitizerKit.Core.Config;
using SanitizerKit.Core.AI;

namespace SanitizerKit.UI.ViewModels;

// Bu sınıf Avalonia UI'daki konsola log basmak ve süreçleri yönetmek içindir.
public class AiOrchestrator
{
    private readonly DiagnosticAiAgent _agent1;
    private readonly FixRecipeAiAgent _agent2;
    private readonly SecurityAiAgent _agent3;
    
    // UI'daki arayüze log göndermek için event fırlatıcı
    public event Action<string>? OnLogMessage;
    public event Action<string, string, string>? OnPatchReady;

    public AiOrchestrator()
    {
        _agent1 = new DiagnosticAiAgent();
        _agent2 = new FixRecipeAiAgent();
        _agent3 = new SecurityAiAgent();
    }

    public async Task ProcessErrorLiveAsync(string filePath, string rawCode, string errorMessage, string encryptedApiKey)
    {
        var config = BomConfigManager.LoadConfig(Path.Combine(Environment.CurrentDirectory, ".bomconfig"));
        double aiTemp = config.AiTemperature;

        OnLogMessage?.Invoke("[SİSTEM] Çoklu Yapay Zeka Analiz zinciri başlatılıyor...");

        // --- 1. ADIM: Ajan 1 ile Teşhis ---
        OnLogMessage?.Invoke("\n[AJAN 1] Kod inceleniyor ve hatanın kök nedeni teşhis ediliyor...");
        string diagnosticResult = await _agent1.AnalyzeIssueAsync(rawCode, errorMessage, encryptedApiKey, temperature: aiTemp);
        OnLogMessage?.Invoke($"[AJAN 1 TEŞHİSİ]:\n{diagnosticResult}");

        if (diagnosticResult.Contains("HATA") || diagnosticResult.Contains("API"))
        {
            OnLogMessage?.Invoke("[SİSTEM] Teşhis başarısız oldu, süreç durduruldu.");
            return;
        }

        // --- 2. ADIM: Ajan 2 ile Çözüm Üretimi ---
        OnLogMessage?.Invoke("\n[AJAN 2] Çözüm kodu / reçete üretiliyor...");
        string proposedFixJson = await _agent2.GenerateFixAsync(rawCode, errorMessage, diagnosticResult, encryptedApiKey, temperature: aiTemp);
        OnLogMessage?.Invoke($"[AJAN 2 ÇÖZÜM ÖNERİSİ (JSON)]:\n{proposedFixJson}");

        if (proposedFixJson.Contains("\"error\""))
        {
            OnLogMessage?.Invoke("[SİSTEM] Çözüm üretilemedi.");
            return;
        }

        // --- 3. ADIM: Ajan 3 ile Güvenlik Denetimi ---
        OnLogMessage?.Invoke("\n[AJAN 3] Çözüm önerisi güvenlik süzgecinden geçiriliyor...");
        // Ajan 3 her halükarda tamamen güvenlik denetimi yaptığı için sıcaklığı sabit ve katı (0.0) tutulur.
        string securityVerdict = await _agent3.ValidateFixAsync(rawCode, proposedFixJson, encryptedApiKey, temperature: 0.0);

        if (securityVerdict.Contains("GÜVENLİ"))
        {
            OnLogMessage?.Invoke("[AJAN 3 ONAYI] Çözüm tamamen GÜVENLİ.");
            OnLogMessage?.Invoke("[SİSTEM] Çözüm İnceleme Penceresi açılıyor...");
            OnPatchReady?.Invoke(proposedFixJson, filePath, rawCode);
        }
        else
        {
            OnLogMessage?.Invoke($"[AJAN 3 REDDİ] Güvenlik uyarısı tespit edildi!\nSebep: {securityVerdict}");
            OnLogMessage?.Invoke("[SİSTEM] Zararlı kod engellendi. Uygulama işlemi iptal edildi.");
        }
    }
}
