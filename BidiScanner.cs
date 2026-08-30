using System;

namespace SanitizerKit.Core.Scanners;

/// <summary>
/// Çift-yönlü (bidi) yazım denetim karakterleri — "Trojan Source" saldırısı
/// (CVE-2021-42574). Kaynak kod insana bir şey, derleyiciye BAŞKA bir şey
/// gösterebilir: gözle onaylanan bir yama, gerçekte farklı kod çalıştırır.
///
/// Bu YALNIZCA rapor edilir, sessizce onarılmaz: karakterin meşru kullanımı da
/// vardır (Arapça/İbranice metin gömme) ve kaldırmak metni bozar. Kararı
/// kullanıcı verir.
/// </summary>
public class BidiScanner : IScanner
{
    // U+202A..U+202E  LRE RLE PDF LRO RLO : E2 80 AA..AE
    // U+2066..U+2069  LRI RLI FSI PDI     : E2 81 A6..A9
    public bool HasIssue(ReadOnlySpan<byte> content)
    {
        for (int i = 0; i + 2 < content.Length; i++)
        {
            if (content[i] != 0xE2) continue;
            byte b1 = content[i + 1], b2 = content[i + 2];
            if (b1 == 0x80 && b2 >= 0xAA && b2 <= 0xAE) return true;
            if (b1 == 0x81 && b2 >= 0xA6 && b2 <= 0xA9) return true;
        }
        return false;
    }
}
