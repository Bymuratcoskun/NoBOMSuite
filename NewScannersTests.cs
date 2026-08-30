using System;
using System.IO;
using System.Text;
using Xunit;
using SanitizerKit.Core.Scanners;

namespace NoBOMSuite.Tests;

/// <summary>
/// 2026-08-29'da eklenen uc tarayici. Her testte POZITIF ve NEGATIF yon
/// birlikte sinanir: yakalamayan tarayici ise yaramaz, her seye bagiran
/// tarayici gormezden gelinir.
///
/// Karakterler KACIS DIZISIYLE yazildi. Sebep: dosya gorunmez karakteri HAM
/// tasisaydi tarayici KENDI test dosyasini isaretlerdi - GhostCharScanner.cs
/// ve ReplacementCharScanner.cs'te bugun yasanan sorun bu.
/// </summary>
public class NewScannersTests
{
    private static byte[] B(string s) => Encoding.UTF8.GetBytes(s);

    // -- InvisibleWhitespaceScanner ----------------------------------------

    [Fact]
    public void Invisible_Should_Detect_Nbsp()
    {
        var s = new InvisibleWhitespaceScanner();
        Assert.True(s.HasIssue(B("iki\u00A0kelime")));
        Assert.False(s.HasIssue(B("iki kelime")));
    }

    [Fact]
    public void Invisible_Should_Detect_Line_And_Paragraph_Separators()
    {
        var s = new InvisibleWhitespaceScanner();
        Assert.True(s.HasIssue(B("a\u2028b")));
        Assert.True(s.HasIssue(B("a\u2029b")));
        Assert.True(s.HasIssue(B("a\u202Fb")));
    }

    [Fact]
    public void Invisible_Should_Detect_C0_Control_Chars()
    {
        var s = new InvisibleWhitespaceScanner();
        Assert.True(s.HasIssue(B("metin\u001Cayirici")));
        Assert.True(s.HasIssue(B("dikey\u000Bsekme")));
        Assert.True(s.HasIssue(new byte[] { 0x61, 0x7F, 0x62 }));
    }

    /// <summary>
    /// Sekme, LF ve CR KAPSAM DISI - onlar TabScanner ve LineEndingScanner'in
    /// isi. Burada da yakalanirsa ayni dosya iki kez, CELISEN onarim
    /// onerisiyle isaretlenir.
    /// </summary>
    [Fact]
    public void Invisible_Should_Not_Claim_Tab_Cr_Lf()
    {
        var s = new InvisibleWhitespaceScanner();
        Assert.False(s.HasIssue(B("a\tb")));
        Assert.False(s.HasIssue(B("a\nb")));
        Assert.False(s.HasIssue(B("a\r\nb")));
    }

    /// <summary>Turkce metin normal bosluklarla TEMIZ sayilmali.</summary>
    [Fact]
    public void Invisible_Should_Accept_Clean_Turkish_Text()
    {
        var s = new InvisibleWhitespaceScanner();
        Assert.False(s.HasIssue(B("\u015Eofor \u00E7igligi \u011E\u00DC\u0130\u00D6\u00C7.\n")));
    }

    // -- ReplacementCharScanner --------------------------------------------

    [Fact]
    public void Replacement_Should_Detect_Broken_Decoding()
    {
        var s = new ReplacementCharScanner();
        Assert.True(s.HasIssue(B("bozuk \uFFFD metin")));
        Assert.False(s.HasIssue(B("saglam metin")));
    }

    /// <summary>
    /// Cok baytli mesru karakterler U+FFFD sanilmamali: EF BF BD dizisi
    /// yalnizca TAM eslesmede sayilir.
    /// </summary>
    [Fact]
    public void Replacement_Should_Not_Trip_On_Other_Multibyte()
    {
        var s = new ReplacementCharScanner();
        Assert.False(s.HasIssue(B("\uFDFD \uFF21\uFF22")));
    }

    // -- BidiScanner (Trojan Source, CVE-2021-42574) -----------------------

    [Fact]
    public void Bidi_Should_Detect_Override_And_Embedding()
    {
        var s = new BidiScanner();
        foreach (var c in new[] { '\u202A', '\u202B', '\u202C', '\u202D', '\u202E' })
            Assert.True(s.HasIssue(B($"kod{c}gizli")), $"U+{(int)c:X4} kacti");
        foreach (var c in new[] { '\u2066', '\u2067', '\u2068', '\u2069' })
            Assert.True(s.HasIssue(B($"kod{c}gizli")), $"U+{(int)c:X4} kacti");
    }

    /// <summary>
    /// Sinir komsulari bidi DEGIL: U+202F (dar kirilmaz bosluk - ayri
    /// tarayicinin isi), U+2065 ve U+206A yanlislikla yakalanmamali. Aralik
    /// kontrolu bir bayt kayarsa bu test duser.
    /// </summary>
    [Fact]
    public void Bidi_Should_Not_Trip_On_Range_Neighbours()
    {
        var s = new BidiScanner();
        Assert.False(s.HasIssue(B("a\u202Fb")));
        Assert.False(s.HasIssue(B("a\u2065b")));
        Assert.False(s.HasIssue(B("a\u206Ab")));
    }

    [Fact]
    public void Bidi_Should_Accept_Plain_Text()
    {
        var s = new BidiScanner();
        Assert.False(s.HasIssue(B("using System;\nnamespace X;\n")));
    }

    // -- HardcodedPasswordScanner: onarim YAKINSAMALI -----------------------

    /// <summary>
    /// Onarim degeri [MASKED_BY_DEVGUARD] ile degistiriyordu ama tarayici
    /// desene bakip yine isaretliyordu: onarim -> tarama dongusu HIC
    /// yakinsamiyordu. Dosya sonsuza kadar sorunlu gorunur, kullanici uyariyi
    /// ciddiye almayi birakir. Bir onarim kendi sonucunu temiz sayamiyorsa
    /// onarim degildir.
    /// </summary>
    [Fact]
    public void Password_Fix_Should_Converge()
    {
        var s = new HardcodedPasswordScanner();
        Assert.True(s.HasIssue(B("password = \"SuperGizli123\"")));
        Assert.False(s.HasIssue(B("password = \"[MASKED_BY_DEVGUARD]\"")));
    }

    /// <summary>Maske ADI gecen ama gercek parola tasiyan satir yine yakalanmali.</summary>
    [Fact]
    public void Password_Mask_Should_Not_Become_A_Bypass()
    {
        var s = new HardcodedPasswordScanner();
        Assert.True(s.HasIssue(B("secret = \"MASKED_BY_DEVGUARD\"")));
        Assert.True(s.HasIssue(B("pass = \"[MASKED_BY_DEVGUARD] ama degil\"")));
    }

    // -- IkiliTespit: yanlis alarmin en buyuk kaynagi --------------------

    /// <summary>
    /// Olcum (2026-08-29, gercek depo): bu denetim OLMADAN 452 bulgunun
    /// 162'si (%35) derlenmis .pyc dosyalarindan geliyordu. Ikili veride CRLF
    /// bayt cifti, sekme ve kontrol karakteri elbette bulunur. %35 yanlis alarm
    /// veren linter kullanilmaz, gormezden gelinir.
    /// </summary>
    [Fact]
    public void Ikili_Should_Detect_Nul_Byte()
    {
        Assert.True(IkiliTespit.Ikili(new byte[] { 0x61, 0x00, 0x62 }));
        Assert.False(IkiliTespit.Ikili(B("saf metin\n")));
    }

    /// <summary>Turkce metin cok baytlidir ama IKILI DEGILDIR.</summary>
    [Fact]
    public void Ikili_Should_Not_Claim_Utf8_Text()
    {
        Assert.False(IkiliTespit.Ikili(B("\u015Eofor \u00E7igligi \u011E\u00DC\u0130\u00D6\u00C7")));
        Assert.False(IkiliTespit.Ikili(B("emoji: \U0001F600")));
    }

    /// <summary>
    /// NUL yalnizca PENCERE icinde aranir; sonrasindaki NUL dosyayi ikili
    /// yapmaz. Pencere kaldirilirsa bu test duser.
    /// </summary>
    [Fact]
    public void Ikili_Should_Only_Look_At_The_Window()
    {
        var buyuk = new byte[IkiliTespit.Pencere + 10];
        for (int i = 0; i < buyuk.Length; i++) buyuk[i] = (byte)'a';
        buyuk[IkiliTespit.Pencere + 5] = 0x00;
        Assert.False(IkiliTespit.Ikili(buyuk));
        buyuk[IkiliTespit.Pencere - 1] = 0x00;
        Assert.True(IkiliTespit.Ikili(buyuk));
    }

    [Fact]
    public void Ikili_Should_Accept_Empty()
    {
        Assert.False(IkiliTespit.Ikili(Array.Empty<byte>()));
    }

    // -- SqliteLogger: yazan taraf kendi semasini kurmali -------------------

    /// <summary>
    /// 2026-08-29: Log() dogrudan INSERT yapiyor, tabloyu HIC olusturmuyordu.
    /// Semayi kuran tek cagri (LogDatabaseManager.Initialize) MainWindow'daki
    /// DISA AKTARMA akisinin icindeydi; normal acilista hic calismiyordu.
    /// Sonuc: her log yazimi sessizce basarisiz (catch yutuyor), panel ve log
    /// goruntuleyici acilista "no such table: Logs" veriyordu.
    ///
    /// Ders: bir bilesenin ihtiyaci olan durumu BASKASININ kurmasina guvenmek,
    /// o baskasi cagrilmadiginda sessiz ariza uretir.
    /// </summary>
    [Fact]
    public void SqliteLogger_Should_Create_Its_Own_Schema()
    {
        var eski = Environment.CurrentDirectory;
        var gecici = Path.Combine(Path.GetTempPath(), "devguard-log-test-" + Guid.NewGuid());
        Directory.CreateDirectory(gecici);
        try
        {
            Environment.CurrentDirectory = gecici;
            SanitizerKit.Core.Logging.SqliteLogger.Log("INFO", "sema kurulmali", "test.cs");

            var db = Path.Combine(gecici, "devguard_logs.db");
            Assert.True(File.Exists(db), "veritabani olusmadi");
            using var c = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={db}");
            c.Open();
            var k = c.CreateCommand();
            k.CommandText = "SELECT count(*) FROM Logs WHERE Message = 'sema kurulmali'";
            Assert.Equal(1L, (long)k.ExecuteScalar()!);
        }
        finally
        {
            Environment.CurrentDirectory = eski;
            try { Directory.Delete(gecici, true); } catch { }
        }
    }
}
