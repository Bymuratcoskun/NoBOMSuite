using System;
using System.Text;
using System.Text.RegularExpressions;

namespace SanitizerKit.Core.Scanners;

public class HardcodedPasswordScanner : IScanner
{
    private static readonly Regex PasswordRegex = new(
        @"(password|passwd|pass|secret)\s*[:=]\s*(['""])(.*?)\2",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>PatchGenerator'in yazdigi maske; bunu tekrar isaretlemeyiz.</summary>
    private const string Masked = "[MASKED_BY_DEVGUARD]";

    public bool HasIssue(ReadOnlySpan<byte> content)
    {
        if (content.IsEmpty) return false;

        try
        {
            string text = Encoding.UTF8.GetString(content);
            // Zaten maskelenmis degerler parola SAYILMAZ.
            //
            // Olcum (2026-08-29): onarim degeri [MASKED_BY_DEVGUARD] ile
            // degistiriyor, ama tarayici desene bakip yine isaretliyordu. Yani
            // onarim -> tarama dongusu hic YAKINSAMIYORDU: dosya sonsuza kadar
            // sorunlu gorunuyor, kullanici uyariyi ciddiye almayi birakiyor.
            // Bir onarim, kendi sonucunu temiz sayamiyorsa onarim degildir.
            foreach (Match m in PasswordRegex.Matches(text))
                if (m.Groups[3].Value != Masked) return true;
            return false;
        }
        catch
        {
            return false;
        }
    }
}