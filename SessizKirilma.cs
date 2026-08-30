using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SanitizerKit.Core.Soundness;

/// <summary>Bir "sessiz kırılma" bulgusu.</summary>
public sealed record Bulgu(string Kod, string Yol, string Mesaj, string Neden);

/// <summary>
/// SESSİZ KIRILMA DENETİMİ — "iyi görünüyor ama çalışmıyor" sınıfı.
///
/// Diğer tarayıcılar dosya İÇİNDEKİ baytlara bakar (BOM, görünmez karakter).
/// Bu denetim DEPO YAPISINA bakar: derleyici hata vermez, test kırılmaz,
/// arayüz yeşildir — ama bir şey hiç çalışmıyordur.
///
/// Buradaki her kontrol, 2026-08-30'da GERÇEK bir depoda yaşanmış bir olaydan
/// gelir. Uydurulmuş kural yoktur.
/// </summary>
public static class SessizKirilma
{
    static readonly string[] AtlanacakDizinler =
        { "node_modules", "bin", "obj", "target", ".git", "__pycache__", ".venv", "venv", "dist", "out",
          // DevGuard'ın KENDİ yedekleri: burada eski kopyalar yaşar ve her biri
          // yeniden bulgu üretirdi. Araç kendi izini taramamalı (2026-08-30:
          // .nobom/backups tek başına beş yanlış bulgu üretti).
          ".nobom", "vendor", "coverage", ".vscode-test", "PortableExport" };

    public static IReadOnlyList<Bulgu> Denetle(string kok)
    {
        var bulgular = new List<Bulgu>();
        if (!Directory.Exists(kok)) return bulgular;

        IsWorkflowYanlisDizinde(kok, bulgular);
        BosCozumDosyasi(kok, bulgular);
        LisansBeyaniKarsiliksiz(kok, bulgular);
        PaketGirdiNoktasiYok(kok, bulgular);
        EksikEklentiManifestosu(kok, bulgular);
        DuzDizindeVarsayilanDerleme(kok, bulgular);

        return bulgular;
    }

    static IEnumerable<string> Dosyalar(string kok, string desen) =>
        Directory.EnumerateFiles(kok, desen, SearchOption.AllDirectories)
                 .Where(y => !y.Split(Path.DirectorySeparatorChar)
                               .Any(p => AtlanacakDizinler.Contains(p)));

    /// <summary>
    /// GitHub Actions dosyası `.github/workflows/` DIŞINDA duruyorsa hiç koşmaz.
    /// Gerçek olay: `ci.yml` ve `secure-release.yml` depo kökündeydi; yazıldıkları
    /// günden beri tek kez çalışmamışlardı, ama dosyalar orada durduğu için
    /// "CI/CD kuruldu" sanılıyordu.
    /// </summary>
    static void IsWorkflowYanlisDizinde(string kok, List<Bulgu> b)
    {
        foreach (var y in Dosyalar(kok, "*.yml").Concat(Dosyalar(kok, "*.yaml")))
        {
            var dizin = Path.GetDirectoryName(y) ?? "";
            if (dizin.Replace('\\', '/').EndsWith(".github/workflows")) continue;

            string metin;
            try { metin = File.ReadAllText(y); } catch { continue; }
            // GitHub Actions imzası: 'on:' + 'jobs:' + en az bir 'uses:' ya da 'runs-on:'
            bool imza = Regex.IsMatch(metin, @"^\s*on\s*:", RegexOptions.Multiline)
                     && Regex.IsMatch(metin, @"^\s*jobs\s*:", RegexOptions.Multiline)
                     && Regex.IsMatch(metin, @"^\s*(runs-on|uses)\s*:", RegexOptions.Multiline);
            if (imza)
                b.Add(new Bulgu("is-akisi-yanlis-dizinde", y,
                    "GitHub Actions iş akışı `.github/workflows/` dışında",
                    "Bu dosya HİÇ KOŞMAZ. Var olması çalıştığı anlamına gelmez."));
        }
    }

    /// <summary>
    /// İçinde proje olmayan çözüm dosyası. `dotnet build` "başarılı" der —
    /// çünkü hiçbir şey derlememiştir.
    /// Gerçek olay: `NoBOMSuite.slnx` = `&lt;Solution&gt;&lt;/Solution&gt;`.
    /// </summary>
    static void BosCozumDosyasi(string kok, List<Bulgu> b)
    {
        foreach (var y in Dosyalar(kok, "*.slnx").Concat(Dosyalar(kok, "*.sln")))
        {
            string metin;
            try { metin = File.ReadAllText(y); } catch { continue; }
            bool projeVar = metin.Contains("<Project ", StringComparison.Ordinal)
                         || metin.Contains("Project(", StringComparison.Ordinal);
            if (!projeVar)
                b.Add(new Bulgu("bos-cozum", y,
                    "Çözüm dosyası hiçbir proje içermiyor",
                    "`dotnet build` başarılı döner çünkü hiçbir şey derlemez — yeşil ama boş."));
        }
    }

    /// <summary>`package.json` bir lisans BEYAN ediyor ama karşılığı olan dosya yok.</summary>
    static void LisansBeyaniKarsiliksiz(string kok, List<Bulgu> b)
    {
        var pj = Path.Combine(kok, "package.json");
        if (!File.Exists(pj)) return;
        var lisans = AlanOku(pj, "license");
        if (string.IsNullOrWhiteSpace(lisans)) return;

        string[] adaylar = { "LICENSE", "LICENSE.md", "LICENSE.txt", "COPYING" };
        if (!adaylar.Any(a => File.Exists(Path.Combine(kok, a))))
            b.Add(new Bulgu("lisans-karsiliksiz", pj,
                $"package.json \"{lisans}\" diyor ama LICENSE dosyası yok",
                "Yasal beyan ile depo içeriği çelişiyor; paket yöneticileri ve mağazalar dosyayı arar."));
    }

    /// <summary>`package.json`'daki `main` var olmayan bir dosyayı gösteriyor.</summary>
    static void PaketGirdiNoktasiYok(string kok, List<Bulgu> b)
    {
        var pj = Path.Combine(kok, "package.json");
        if (!File.Exists(pj)) return;
        var ana = AlanOku(pj, "main");
        if (string.IsNullOrWhiteSpace(ana)) return;

        var hedef = Path.GetFullPath(Path.Combine(kok, ana.TrimStart('.', '/', '\\')));
        if (!File.Exists(hedef))
            b.Add(new Bulgu("girdi-noktasi-yok", pj,
                $"package.json main → \"{ana}\" bulunamadı",
                "Paket `require` edildiği anda patlar; kurulum sessizce başarılı görünür."));
    }

    /// <summary>
    /// VS Code eklentisi gibi görünen ama manifestosu eksik paket.
    /// Gerçek olay: yol haritası "VS Code eklentisi tamamlandı" diyordu; oysa
    /// `engines.vscode`, `contributes` ve `activationEvents` üçü de yoktu —
    /// paket sıradan bir npm modülüydü, eklenti olarak asla yüklenemezdi.
    /// </summary>
    static void EksikEklentiManifestosu(string kok, List<Bulgu> b)
    {
        var pj = Path.Combine(kok, "package.json");
        if (!File.Exists(pj)) return;
        JsonElement k;
        try { k = JsonDocument.Parse(File.ReadAllText(pj)).RootElement; } catch { return; }

        bool eklentiIddiasi =
            (k.TryGetProperty("categories", out var kat) && kat.ValueKind == JsonValueKind.Array
                && kat.EnumerateArray().Any(x => x.ValueKind == JsonValueKind.String))
            || k.TryGetProperty("contributes", out _)
            || k.TryGetProperty("activationEvents", out _)
            || (k.TryGetProperty("engines", out var m) && m.TryGetProperty("vscode", out _));
        if (!eklentiIddiasi) return;

        var eksik = new List<string>();
        if (!(k.TryGetProperty("engines", out var e) && e.TryGetProperty("vscode", out _))) eksik.Add("engines.vscode");
        if (!k.TryGetProperty("contributes", out _)) eksik.Add("contributes");
        if (!k.TryGetProperty("activationEvents", out _)) eksik.Add("activationEvents");

        if (eksik.Count > 0)
            b.Add(new Bulgu("eklenti-manifestosu-eksik", pj,
                "VS Code eklentisi gibi görünüyor ama eksik: " + string.Join(", ", eksik),
                "Bu hâliyle eklenti olarak YÜKLENEMEZ; npm paketi olarak sorunsuz görünür."));
    }

    /// <summary>
    /// Düz dizinde birden çok csproj varsa ve biri varsayılan glob kullanıyorsa,
    /// komşu projenin dosyalarını da toplar. Kara liste (`Compile Remove`) bayatlar
    /// ve YENİ dosya eklenince proje sessizce kırılır.
    /// Gerçek olay: aynı kusur üç projede çıktı (CLI, Native, Desktop).
    /// </summary>
    static void DuzDizindeVarsayilanDerleme(string kok, List<Bulgu> b)
    {
        foreach (var dizin in Dosyalar(kok, "*.csproj")
                     .Select(Path.GetDirectoryName).Distinct())
        {
            if (dizin is null) continue;
            var projeler = Directory.GetFiles(dizin, "*.csproj");
            if (projeler.Length < 2) continue;         // düz dizin değil, sorun yok

            foreach (var p in projeler)
            {
                string m;
                try { m = File.ReadAllText(p); } catch { continue; }
                bool acikListe = m.Contains("<EnableDefaultCompileItems>false", StringComparison.OrdinalIgnoreCase);
                bool karaListe = m.Contains("<Compile Remove=", StringComparison.OrdinalIgnoreCase);
                if (!acikListe && karaListe)
                    b.Add(new Bulgu("bayatlayan-kara-liste", p,
                        $"Düz dizinde {projeler.Length} proje var; bu proje kara liste kullanıyor",
                        "Dizine eklenen HER yeni dosya bu projeye sızar. Kara liste bayatlar ve derleme sessizce kırılır; `EnableDefaultCompileItems=false` + açık liste kullanın."));
            }
        }
    }

    static string AlanOku(string jsonYolu, string alan)
    {
        try
        {
            var k = JsonDocument.Parse(File.ReadAllText(jsonYolu)).RootElement;
            return k.TryGetProperty(alan, out var v) && v.ValueKind == JsonValueKind.String
                ? v.GetString() ?? "" : "";
        }
        catch { return ""; }
    }
}
