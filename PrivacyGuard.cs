using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace SanitizerKit.Core.Security;

/// <summary>
/// Giden tüm HTTP isteklerini denetleyen ve izinsiz telemetri/analitik gönderimini engelleyen ağ muhafızı.
/// </summary>
public class PrivacyGuardHandler : DelegatingHandler
{
    private readonly bool _strictOfflineMode;
    private readonly string[] _allowedDomains;

    public PrivacyGuardHandler(bool strictOfflineMode, string[] allowedDomains)
    {
        _strictOfflineMode = strictOfflineMode;
        _allowedDomains = allowedDomains;
        InnerHandler = new SocketsHttpHandler(); // Modern ve performanslı alt handler
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.RequestUri != null)
        {
            string host = request.RequestUri.Host;
            bool isLocal = host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || host.Equals("127.0.0.1");

            // Eğer katı çevrimdışı mod açıksa (Varsayılan), yerel olmayan HİÇBİR dış ağ isteğine izin verilmez.
            if (_strictOfflineMode && !isLocal)
            {
                throw new UnauthorizedAccessException("[GİZLİLİK İHLALİ ENGELLENDİ] Uygulama %100 Çevrimdışı modunda çalışıyor. Telemetri veya dış ağ isteği reddedildi.");
            }

            // Yerel olmayan isteklerin izin verilen domainler (whitelist) listesinde olduğunu doğrula
            if (!isLocal)
            {
                bool isAllowed = false;
                foreach (var domain in _allowedDomains)
                {
                    if (host.EndsWith(domain, StringComparison.OrdinalIgnoreCase))
                    {
                        isAllowed = true;
                        break;
                    }
                }

                if (!isAllowed)
                {
                    throw new UnauthorizedAccessException($"[TELEMETRİ ENGELLENDİ] '{host}' adresine giden ağ isteği gizlilik ilkesi gereği engellendi.");
                }
            }
        }

        return await base.SendAsync(request, cancellationToken);
    }
}

public static class PrivacyGuard
{
    public static HttpClient CreateSafeHttpClient(bool strictOfflineMode = true, params string[] allowedDomains)
    {
        var handler = new PrivacyGuardHandler(strictOfflineMode, allowedDomains);
        return new HttpClient(handler);
    }
}
