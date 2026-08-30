using System;
using System.IO;
using System.Linq;
using SanitizerKit.Core.Soundness;
using Xunit;

namespace NoBOMSuite.Tests;

/// <summary>
/// Her test, 2026-08-30'da GERÇEK bir depoda yaşanmış bir olayı temsil eder.
/// Bu yüzden her testin iki yüzü vardır: kusurlu depoda bulgu ÇIKMALI,
/// sağlam depoda ÇIKMAMALI — yoksa denetim ya kör ya da gürültücü olur.
/// </summary>
public class SessizKirilmaTests : IDisposable
{
    readonly string _kok;
    public SessizKirilmaTests()
    {
        _kok = Path.Combine(Path.GetTempPath(), "sessiz-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_kok);
    }
    public void Dispose() { try { Directory.Delete(_kok, true); } catch { } }

    string Yaz(string goreliYol, string icerik)
    {
        var y = Path.Combine(_kok, goreliYol);
        Directory.CreateDirectory(Path.GetDirectoryName(y)!);
        File.WriteAllText(y, icerik);
        return y;
    }

    string[] Kodlar() => SessizKirilma.Denetle(_kok).Select(b => b.Kod).ToArray();

    const string IsAkisi = @"
name: CI
on:
  push:
    branches: [ main ]
jobs:
  build:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
";

    // ── İŞ AKIŞI YANLIŞ DİZİNDE ────────────────────────────────────────────
    [Fact]
    public void Kokteki_is_akisi_yakalanir()
    {
        Yaz("ci.yml", IsAkisi);
        Assert.Contains("is-akisi-yanlis-dizinde", Kodlar());
    }

    [Fact]
    public void Dogru_dizindeki_is_akisi_bulgu_uretmez()
    {
        Yaz(".github/workflows/ci.yml", IsAkisi);
        Assert.DoesNotContain("is-akisi-yanlis-dizinde", Kodlar());
    }

    [Fact]
    public void Siradan_yaml_is_akisi_sanilmaz()
    {
        // docker-compose, k8s, ayar dosyaları yanlış alarm ÜRETMEMELİ.
        Yaz("docker-compose.yml", "services:\n  web:\n    image: nginx\n");
        Yaz("ayarlar.yaml", "on: true\nrenk: mavi\n");
        Assert.DoesNotContain("is-akisi-yanlis-dizinde", Kodlar());
    }

    // ── BOŞ ÇÖZÜM ──────────────────────────────────────────────────────────
    [Fact]
    public void Bos_cozum_dosyasi_yakalanir()
    {
        Yaz("Uygulama.slnx", "<Solution>\n</Solution>\n");
        Assert.Contains("bos-cozum", Kodlar());
    }

    [Fact]
    public void Dolu_cozum_bulgu_uretmez()
    {
        Yaz("Uygulama.slnx", "<Solution>\n  <Project Path=\"A.csproj\" />\n</Solution>\n");
        Assert.DoesNotContain("bos-cozum", Kodlar());
    }

    // ── LİSANS ─────────────────────────────────────────────────────────────
    [Fact]
    public void Lisans_beyani_karsiliksizsa_yakalanir()
    {
        Yaz("package.json", "{\"name\":\"x\",\"license\":\"MIT\"}");
        Assert.Contains("lisans-karsiliksiz", Kodlar());
    }

    [Fact]
    public void LICENSE_varsa_bulgu_uretmez()
    {
        Yaz("package.json", "{\"name\":\"x\",\"license\":\"MIT\"}");
        Yaz("LICENSE", "MIT License\n");
        Assert.DoesNotContain("lisans-karsiliksiz", Kodlar());
    }

    // ── GİRDİ NOKTASI ──────────────────────────────────────────────────────
    [Fact]
    public void Olmayan_main_yakalanir()
    {
        Yaz("package.json", "{\"name\":\"x\",\"main\":\"./yok.js\"}");
        Assert.Contains("girdi-noktasi-yok", Kodlar());
    }

    [Fact]
    public void Var_olan_main_bulgu_uretmez()
    {
        Yaz("package.json", "{\"name\":\"x\",\"main\":\"./index.js\"}");
        Yaz("index.js", "module.exports = {};\n");
        Assert.DoesNotContain("girdi-noktasi-yok", Kodlar());
    }

    // ── EKLENTİ MANİFESTOSU ────────────────────────────────────────────────
    [Fact]
    public void Eklenti_gibi_gorunup_manifestosu_eksik_olan_yakalanir()
    {
        // Gerçek olay: categories var, ama engines/contributes/activationEvents yok.
        Yaz("package.json", "{\"name\":\"x\",\"categories\":[\"Linters\"]}");
        Assert.Contains("eklenti-manifestosu-eksik", Kodlar());
    }

    [Fact]
    public void Tam_eklenti_manifestosu_bulgu_uretmez()
    {
        Yaz("package.json", """
        {"name":"x","categories":["Linters"],
         "engines":{"vscode":"^1.85.0"},
         "contributes":{"commands":[]},
         "activationEvents":["onStartupFinished"]}
        """);
        Assert.DoesNotContain("eklenti-manifestosu-eksik", Kodlar());
    }

    [Fact]
    public void Siradan_npm_paketi_eklenti_sanilmaz()
    {
        Yaz("package.json", "{\"name\":\"x\",\"version\":\"1.0.0\"}");
        Assert.DoesNotContain("eklenti-manifestosu-eksik", Kodlar());
    }

    // ── BAYATLAYAN KARA LİSTE ──────────────────────────────────────────────
    [Fact]
    public void Duz_dizinde_kara_liste_yakalanir()
    {
        Yaz("A.csproj", "<Project><ItemGroup><Compile Remove=\"B.cs\" /></ItemGroup></Project>");
        Yaz("B.csproj", "<Project></Project>");
        Assert.Contains("bayatlayan-kara-liste", Kodlar());
    }

    [Fact]
    public void Acik_liste_kullanan_proje_bulgu_uretmez()
    {
        Yaz("A.csproj", """
        <Project><PropertyGroup><EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup>
        <ItemGroup><Compile Include="a.cs" /><Compile Remove="B.cs" /></ItemGroup></Project>
        """);
        Yaz("B.csproj", "<Project></Project>");
        Assert.DoesNotContain("bayatlayan-kara-liste", Kodlar());
    }

    [Fact]
    public void Tek_projeli_dizinde_kara_liste_sorun_degil()
    {
        Yaz("A.csproj", "<Project><ItemGroup><Compile Remove=\"x.cs\" /></ItemGroup></Project>");
        Assert.DoesNotContain("bayatlayan-kara-liste", Kodlar());
    }

    // ── GÜRÜLTÜ KAPISI ─────────────────────────────────────────────────────
    [Fact]
    public void Saglam_depo_HIC_bulgu_uretmez()
    {
        Yaz(".github/workflows/ci.yml", IsAkisi);
        Yaz("package.json", """
        {"name":"x","license":"MIT","main":"./index.js"}
        """);
        Yaz("LICENSE", "MIT License\n");
        Yaz("index.js", "module.exports = {};\n");
        Yaz("Uygulama.slnx", "<Solution>\n  <Project Path=\"A.csproj\" />\n</Solution>\n");
        Assert.Empty(SessizKirilma.Denetle(_kok));
    }

    [Fact]
    public void Atlanan_dizinlerdeki_dosyalar_taranmaz()
    {
        Yaz("node_modules/paket/ci.yml", IsAkisi);
        Yaz("bin/Debug/bos.slnx", "<Solution></Solution>");
        Assert.Empty(SessizKirilma.Denetle(_kok));
    }

    [Fact]
    public void DevGuardin_kendi_yedekleri_taranmaz()
    {
        // 2026-08-30: .nobom/backups altındaki eski kopyalar tek başına
        // BEŞ yanlış bulgu üretti. Araç kendi izini taramamalı.
        Yaz(".nobom/backups/20260609_120000/eski_ci.yml", IsAkisi);
        Yaz(".nobom/backups/20260609_120000/bos.slnx", "<Solution></Solution>");
        Assert.Empty(SessizKirilma.Denetle(_kok));
    }
}
