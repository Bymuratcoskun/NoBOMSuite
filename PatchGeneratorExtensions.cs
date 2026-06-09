using System;

namespace SanitizerKit.Core.Patching;

public static class PatchGeneratorExtensions
{
    /// <summary>
    /// Proje içindeki [MASKED_BY_DEVGUARD] içeren tüm satırları tespit eder ve tamamen silen bir yama kodu üretir.
    /// </summary>
    public static string GenerateMaskedPasswordCleanerPatch()
    {
        // Regex Açıklaması: Satır başından (\^[ \t]*) itibaren içinde [MASKED_BY_DEVGUARD] geçen satırı 
        // ve sonundaki satır sonu karakterini (\r?\n?) bütünüyle eşleştirir ve boşlukla ("") yer değiştirerek satırı siler.
        string regexPattern = @"^[ \t]*.*?\[MASKED_BY_DEVGUARD\].*?\r?\n?";
        
        return PatchGenerator.GeneratePythonPatch(
            "DevGuard Masked Password Line Remover",
            regexPattern,
            "" // Satırı tamamen sil
        );
    }
}