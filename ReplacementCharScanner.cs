using System;

namespace SanitizerKit.Core.Scanners;

/// <summary>
/// U+FFFD (�) — bozuk kod çözmenin KANITI.
///
/// Diğer bulgulardan farkı: bu bir kirlilik değil, bir KAYIP izidir. Dosya
/// yanlış kodlamayla okunmuş ve özgün karakter zaten yok olmuş. O yüzden
/// ONARILAMAZ — kaldırmak kaybı gizlemekten başka işe yaramaz. Doğru tepki
/// kaynağı doğru kodlamayla yeniden almaktır.
/// </summary>
public class ReplacementCharScanner : IScanner
{
    // U+FFFD : EF BF BD
    private static ReadOnlySpan<byte> Replacement => [0xEF, 0xBF, 0xBD];

    public bool HasIssue(ReadOnlySpan<byte> content) => content.IndexOf(Replacement) >= 0;
}
