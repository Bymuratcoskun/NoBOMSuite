using System;

namespace SanitizerKit.Core.Scanners;

/// <summary>
/// Sıradan boşluk gibi görünen ama OLMAYAN karakterler ve C0 denetim
/// karakterleri.
///
/// GhostCharScanner'dan ayrı, çünkü onarımı farklı: hayalet karakterler
/// SİLİNİR, bunlar ise sıradan boşlukla DEĞİŞTİRİLİR. İkisini aynı onarıma
/// vermek kelimeleri birleştirir.
///
/// Sekme, satır başı ve satır sonu KAPSAM DIŞI — onlar TabScanner ve
/// LineEndingScanner'ın işi.
/// </summary>
public class InvisibleWhitespaceScanner : IScanner
{
    // U+00A0 kırılmaz boşluk        : C2 A0
    // U+2028 satır ayırıcı          : E2 80 A8
    // U+2029 paragraf ayırıcı       : E2 80 A9
    // U+202F dar kırılmaz boşluk    : E2 80 AF
    // U+FEFF metin içinde ZWNBSP    : EF BB BF  (baştaysa BomScanner'ın işi)
    private static ReadOnlySpan<byte> Nbsp => [0xC2, 0xA0];

    public bool HasIssue(ReadOnlySpan<byte> content)
    {
        if (content.IndexOf(Nbsp) >= 0) return true;

        for (int i = 0; i + 2 < content.Length; i++)
        {
            if (content[i] != 0xE2) continue;
            if (content[i + 1] == 0x80 && (content[i + 2] == 0xA8 || content[i + 2] == 0xA9
                                            || content[i + 2] == 0xAF)) return true;
        }

        // C0 denetim karakterleri — sekme (09), LF (0A), CR (0D) hariç.
        // Metinde işi yoktur; çoğu bozuk PDF/OCR çıkarımından bulaşır.
        foreach (byte b in content)
        {
            if (b < 0x20 && b != 0x09 && b != 0x0A && b != 0x0D) return true;
            if (b == 0x7F) return true;   // DEL
        }
        return false;
    }
}
