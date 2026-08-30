using System;

namespace SanitizerKit.Core.Scanners;

/// <summary>
/// Dosya metin mi, ikili mi?
///
/// NEDEN VAR (2026-08-29 ölçümü): gerçek bir depoda 452 bulgunun 162'si (%35)
/// derlenmiş `.pyc` dosyalarından geliyordu. İkili veride CRLF bayt çifti,
/// sekme ve kontrol karakteri elbette bulunur — hepsi yanlış alarm. %35 yanlış
/// alarm veren bir linter kullanılmaz, görmezden gelinir.
///
/// Sezgi git'inkiyle aynı: baştaki penceresinde NUL baytı varsa ikilidir.
/// Dizin adı listesine güvenmek yeterli DEĞİL — liste hiç tamamlanmaz;
/// içeriğe bakmak yapısal çözümdür.
/// </summary>
public static class IkiliTespit
{
    /// <summary>git'in kullandığı pencere.</summary>
    public const int Pencere = 8000;

    public static bool Ikili(ReadOnlySpan<byte> content)
    {
        int n = Math.Min(content.Length, Pencere);
        for (int i = 0; i < n; i++)
            if (content[i] == 0x00) return true;
        return false;
    }
}
