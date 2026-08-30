using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using System.IO;
using SanitizerKit.Core.AI;
using System.Text.Json;
using SanitizerKit.Core.Config;

namespace NoBOMSuite.Tests;

// Sahte (Mock) HTTP İşleyici - Ağ isteği atmaz, sadece verdiğimiz JSON yanıtını anında döner
public class MockHttpMessageHandler : HttpMessageHandler
{
    private readonly string _responseContent;
    private readonly HttpStatusCode _statusCode;

    public MockHttpMessageHandler(string responseContent, HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        _responseContent = responseContent;
        _statusCode = statusCode;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = new HttpResponseMessage
        {
            StatusCode = _statusCode,
            Content = new StringContent(_responseContent)
        };
        return Task.FromResult(response);
    }
}

[Collection("BomConfig")]
public class AiAgentsTests
{

    /// <summary>Testin hangi sağlayıcıyla koştuğunu AÇIKÇA yazar ve sonunda geri alır.</summary>
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

    private static BomConfig BulutKipi() => new()
    {
        AiProvider = "OpenAI",
        StrictOfflineMode = false   // bulut testi: dış ağ yolu bilinçli olarak açık
    };

    [Fact]
    public async Task DiagnosticAiAgent_Should_Return_Expected_Diagnosis_Without_Network()
    {
        using var _ = new GeciciYapilandirma(BulutKipi());

        // Arrange: OpenAI formatına uygun başarılı bir sahte yanıt (Mock Response) hazırlıyoruz
        var mockResponseObj = new
        {
            choices = new[]
            {
                new { message = new { content = "Bu bir mock (sahte) teşhis yanıtıdır." } }
            }
        };
        string mockJson = JsonSerializer.Serialize(mockResponseObj);
        
        var handler = new MockHttpMessageHandler(mockJson);
        var httpClient = new HttpClient(handler);

        // LocalAiFirewall'dan geçebilmesi için şifrelenmiş sahte bir anahtar üretiyoruz
        string fakeApiKey = LocalAiFirewall.EncryptApiKey("sk-mock-key");

        var agent = new DiagnosticAiAgent();

        // Act: Testimizi mock client ile başlatıyoruz (Bu çağrı internete çıkmaz!)
        string result = await agent.AnalyzeIssueAsync(
            rawCode: "var test = 1", 
            errorMessage: "Mock Error", 
            encryptedApiKey: fakeApiKey, 
            endpoint: "https://api.openai.com/v1/chat/completions",
            customClient: httpClient
        );

        // Assert: Ajanın sahte HTTP yanıtını doğru şekilde ayıklayıp ayıklamadığını kontrol et
        Assert.Equal("Bu bir mock (sahte) teşhis yanıtıdır.", result);
    }

    [Fact]
    public async Task SecurityAiAgent_Should_Return_Rejected_For_Malicious_Code()
    {
        using var _ = new GeciciYapilandirma(BulutKipi());

        // Arrange: OpenAI'nin zararlı kod tespit ettiğinde vereceği formatı taklit et
        var mockResponseObj = new
        {
            choices = new[]
            {
                new { message = new { content = "REDDEDİLDİ: Önerilen çözüm SQL Injection (SQL Enjeksiyonu) güvenlik açığı barındırıyor." } }
            }
        };
        string mockJson = JsonSerializer.Serialize(mockResponseObj);
        
        var handler = new MockHttpMessageHandler(mockJson);
        var httpClient = new HttpClient(handler);

        string fakeApiKey = LocalAiFirewall.EncryptApiKey("sk-mock-security-key");

        var agent = new SecurityAiAgent();

        // Act: Güvensiz bir çözüm JSON'unu ajana gönderiyoruz
        string result = await agent.ValidateFixAsync(
            originalCode: "SELECT * FROM users", 
            proposedFixJson: "{\"fixedCode\": \"SELECT * FROM users WHERE id = \" + req.body.id}", // Açıkça SQL açığı olan bir kod önerisi
            encryptedApiKey: fakeApiKey, 
            endpoint: "https://api.openai.com/v1/chat/completions",
            customClient: httpClient
        );

        // Assert: Ajanın sadece hatayı değil, REDDEDİLDİ formatını da koruduğunu doğrula
        Assert.Equal("REDDEDİLDİ: Önerilen çözüm SQL Injection (SQL Enjeksiyonu) güvenlik açığı barındırıyor.", result);
    }

    [Fact]
    public async Task DiagnosticAiAgent_Should_Return_Expected_Ollama_Diagnosis_Without_Network()
    {
        var configPath = Path.Combine(Environment.CurrentDirectory, ".bomconfig");
        var originalConfig = BomConfigManager.LoadConfig(configPath);
        
        try
        {
            var testConfig = new BomConfig
            {
                AiProvider = "Ollama",
                OllamaEndpoint = "http://localhost:11434",
                OllamaModel = "test-model"
            };
            BomConfigManager.SaveConfig(configPath, testConfig);
            
            var mockResponseObj = new
            {
                message = new { content = "Bu bir yerel Ollama mock teşhis yanıtıdır." }
            };
            string mockJson = JsonSerializer.Serialize(mockResponseObj);
            
            var handler = new MockHttpMessageHandler(mockJson);
            var httpClient = new HttpClient(handler);
            
            var agent = new DiagnosticAiAgent();
            
            string result = await agent.AnalyzeIssueAsync(
                rawCode: "var test = 1", 
                errorMessage: "Mock Error", 
                encryptedApiKey: "", 
                customClient: httpClient
            );
            
            Assert.Equal("Bu bir yerel Ollama mock teşhis yanıtıdır.", result);
        }
        finally
        {
            BomConfigManager.SaveConfig(configPath, originalConfig);
        }
    }
}
