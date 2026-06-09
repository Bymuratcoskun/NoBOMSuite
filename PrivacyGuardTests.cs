using System;
using System.Net.Http;
using System.Threading.Tasks;
using Xunit;
using SanitizerKit.Core.Security;

namespace NoBOMSuite.Tests;

public class PrivacyGuardTests
{
    [Fact]
    public async Task StrictOfflineMode_Should_Block_All_Requests()
    {
        // Tamamen çevrimdışı mod aktifken hiçbir isteğe (onaylı domain olsa bile) izin verilmemeli
        using var client = PrivacyGuard.CreateSafeHttpClient(strictOfflineMode: true, allowedDomains: new[] { "api.openai.com" });
        var exception = await Assert.ThrowsAsync<UnauthorizedAccessException>(() => client.GetAsync("https://api.openai.com"));
        Assert.Contains("GİZLİLİK İHLALİ ENGELLENDİ", exception.Message);
    }

    [Fact]
    public async Task SafeMode_Should_Block_Unauthorized_Domains()
    {
        // Çevrimdışı mod kapalı olsa bile, onaylanmamış domainlere (örn: kötü niyetli bir sunucuya) çıkış engellenmeli
        using var client = PrivacyGuard.CreateSafeHttpClient(strictOfflineMode: false, "api.openai.com");
        var exception = await Assert.ThrowsAsync<UnauthorizedAccessException>(() => client.GetAsync("https://evil-hacker.com/steal_code"));
        Assert.Contains("TELEMETRİ ENGELLENDİ", exception.Message);
    }
}