using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SanitizerKit.Core.AI;
using SanitizerKit.Core.Config;
using Xunit;
using Xunit.Sdk;

namespace NoBOMSuite.Tests;

/// <summary>Gönderilen isteği YAKALAYAN sahte işleyici — uç ve gövde denetlenebilsin diye.</summary>
public class YakalayanHandler : HttpMessageHandler
{
    private readonly string _yanit;
    public Uri? Uc { get; private set; }
    public string? Govde { get; private set; }
    public bool YetkiBasligiVarMi { get; private set; }

    public YakalayanHandler(string yanit) => _yanit = yanit;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Uc = request.RequestUri;
        Govde = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
        YetkiBasligiVarMi = request.Headers.Authorization is not null;
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(_yanit) };
    }
}

[Collection("BomConfig")]
public class YerelOpenAiTests
{
    private sealed class GeciciYapilandirma : IDisposable
    {
        private readonly string _yol;
        private readonly BomConfig _onceki;
        public GeciciYapilandirma(BomConfig yeni)
        {
            _yol = Path.Combine(Environment.CurrentDirectory, ".bomconfig");
            _onceki = BomConfigManager.LoadConfig(_yol);
            BomConfigManager.SaveConfig(_yol, yeni);
        }
        public void Dispose() => BomConfigManager.SaveConfig(_yol, _onceki);
    }

    private static BomConfig YerelKip(string uc = "http://127.0.0.1:8090", string model = "coder-14b") => new()
    {
        AiProvider = "LocalOpenAI",
        LocalEndpoint = uc,
        LocalModel = model,
        StrictOfflineMode = true
    };

    [Theory]
    [InlineData("LocalOpenAI", AiKip.YerelOpenAI)]
    [InlineData("localopenai", AiKip.YerelOpenAI)]
    [InlineData("Yerel", AiKip.YerelOpenAI)]
    [InlineData("Ollama", AiKip.Ollama)]
    [InlineData("OpenAI", AiKip.Bulut)]
    [InlineData("", AiKip.Bulut)]
    [InlineData(null, AiKip.Bulut)]
    public void KipCoz_Saglayici_Adini_Dogru_Kipe_Cevirir(string? ad, AiKip beklenen)
        => Assert.Equal(beklenen, AiTasiyici.KipCoz(ad));

    [Fact]
    public void Yerel_Kipler_Dis_Aga_Cikmaz()
    {
        Assert.False(AiTasiyici.DisAgaCikar(AiKip.YerelOpenAI));
        Assert.False(AiTasiyici.DisAgaCikar(AiKip.Ollama));
        Assert.True(AiTasiyici.DisAgaCikar(AiKip.Bulut));
    }

    [Fact]
    public async Task YerelOpenAI_OpenAI_Ucuna_Anahtarsiz_Gider()
    {
        using var kip = new GeciciYapilandirma(YerelKip());

        var yanit = JsonSerializer.Serialize(new
        {
            choices = new[] { new { message = new { content = "yerel teşhis" } } }
        });
        var handler = new YakalayanHandler(yanit);
        using var client = new HttpClient(handler);

        var sonuc = await new DiagnosticAiAgent().AnalyzeIssueAsync(
            rawCode: "var t = 1", errorMessage: "Mock", encryptedApiKey: "", customClient: client);

        Assert.Equal("yerel teşhis", sonuc);
        Assert.Equal("http://127.0.0.1:8090/v1/chat/completions", handler.Uc!.ToString());
        Assert.False(handler.YetkiBasligiVarMi);                       // anahtar İSTENMEZ
        using var govde = JsonDocument.Parse(handler.Govde!);
        Assert.Equal("coder-14b", govde.RootElement.GetProperty("model").GetString());
        Assert.False(govde.RootElement.TryGetProperty("options", out _)); // Ollama lehçesi DEĞİL
    }

    [Fact]
    public async Task Ollama_Lehcesi_Bozulmadi()
    {
        using var kip = new GeciciYapilandirma(new BomConfig
        {
            AiProvider = "Ollama",
            OllamaEndpoint = "http://localhost:11434",
            OllamaModel = "test-model"
        });

        var handler = new YakalayanHandler(JsonSerializer.Serialize(new { message = new { content = "ollama" } }));
        using var client = new HttpClient(handler);

        var sonuc = await new DiagnosticAiAgent().AnalyzeIssueAsync("var t = 1", "Mock", "", customClient: client);

        Assert.Equal("ollama", sonuc);
        Assert.Equal("http://localhost:11434/api/chat", handler.Uc!.ToString());
        using var govde = JsonDocument.Parse(handler.Govde!);
        Assert.True(govde.RootElement.TryGetProperty("options", out _));
    }

    [Fact]
    public async Task Cagiran_Acikca_Uc_Verirse_Ona_Dokunulmaz()
    {
        using var kip = new GeciciYapilandirma(YerelKip());
        var handler = new YakalayanHandler(JsonSerializer.Serialize(new
        {
            choices = new[] { new { message = new { content = "x" } } }
        }));
        using var client = new HttpClient(handler);

        await new DiagnosticAiAgent().AnalyzeIssueAsync(
            "k", "h", "", endpoint: "http://127.0.0.1:9999/ozel", customClient: client);

        Assert.Equal("http://127.0.0.1:9999/ozel", handler.Uc!.ToString());
    }

    /// <summary>
    /// REGRESYON: ajanlar `strictOfflineMode: false` yazıp kullanıcının
    /// StrictOfflineMode ayarını sessizce eziyordu. README'nin "%100 çevrimdışı"
    /// iddiası bu yüzden yanlıştı.
    /// </summary>
    [Fact]
    public async Task Cevrimdisi_Kip_Acikken_Bulut_Yolu_Acilmaz()
    {
        using var kip = new GeciciYapilandirma(new BomConfig
        {
            AiProvider = "OpenAI",
            StrictOfflineMode = true
        });

        var handler = new YakalayanHandler("{}");
        using var client = new HttpClient(handler);

        var sonuc = await new DiagnosticAiAgent().AnalyzeIssueAsync("k", "h", "anahtar", customClient: client);

        Assert.StartsWith("GİZLİLİK:", sonuc);
        Assert.Null(handler.Uc);          // tek bir istek bile KURULMADI
    }

    /// <summary>Yerel kipin HttpClient'ı dış ağa çıkarsa gizlilik muhafızı fırlatmalı.</summary>
    [Fact]
    public async Task Yerel_Kipin_Istemcisi_Dis_Agi_Reddeder()
    {
        using var istemci = AiTasiyici.IstemciKur(AiKip.YerelOpenAI, YerelKip());
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => istemci.GetAsync("https://api.openai.com/v1/models"));
    }

    /// <summary>
    /// CANLI kanıt: mock testler yalnız kabloların doğru bağlandığını gösterir.
    /// Lehçenin gerçek sunucuda tuttuğunu ancak gerçek çağrı kanıtlar.
    /// Varsayılan olarak ATLANIR; `DEVGUARD_CANLI_UC` verilince koşar.
    /// </summary>
    [SkippableFact]
    public async Task CANLI_Yerel_14B_Gercekten_Cevap_Veriyor()
    {
        var uc = Environment.GetEnvironmentVariable("DEVGUARD_CANLI_UC");
        Skip.If(string.IsNullOrWhiteSpace(uc), "DEVGUARD_CANLI_UC verilmedi — canlı uç testi atlandı.");

        using var kip = new GeciciYapilandirma(YerelKip(uc!, Environment.GetEnvironmentVariable("DEVGUARD_CANLI_MODEL") ?? "coder-14b"));

        var sonuc = await new DiagnosticAiAgent().AnalyzeIssueAsync(
            rawCode: "int x = 1;\r\n",
            errorMessage: "Dosyanın başında BOM (EF BB BF) var ve derleyici ilk satırı okuyamıyor.",
            encryptedApiKey: "");

        Assert.False(sonuc.StartsWith("HATA:"), sonuc);
        Assert.False(sonuc.StartsWith("GİZLİLİK:"), sonuc);
        Assert.False(sonuc.StartsWith("API "), sonuc);
        Assert.False(sonuc.StartsWith("BEKLENMEYEN HATA:"), sonuc);
        Assert.True(sonuc.Length > 40, $"yanıt fazla kısa: {sonuc}");

        Console.WriteLine("=== CANLI 14B YANITI ===");
        Console.WriteLine(sonuc);
    }
}
