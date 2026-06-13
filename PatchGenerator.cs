using System;
using System.IO;
using System.Text;

namespace SanitizerKit.Core.Patching;

public class PatchGenerator
{
    // Bağımsız bir Python yaması (script) üretir
    public static string GeneratePythonPatch(string ruleName, string regexPattern, string replacement)
    {
        var sb = new StringBuilder();
        sb.AppendLine("#!/usr/bin/env python3");
        sb.AppendLine("# ==========================================");
        sb.AppendLine($"# NoBOMSuite Bağımsız Yama: {ruleName}");
        sb.AppendLine("# ==========================================");
        sb.AppendLine("import sys, os, re");
        sb.AppendLine();
        string escapedPattern = regexPattern.Replace("\"", "\\\"");
        string escapedReplacement = replacement.Replace("\"", "\\\"");
        sb.AppendLine($"PATTERN = r\"{escapedPattern}\"");
        sb.AppendLine($"REPLACEMENT = \"{escapedReplacement}\"");
        sb.AppendLine();
        sb.AppendLine("def apply_patch(filepath):");
        sb.AppendLine("    try:");
        sb.AppendLine("        with open(filepath, 'r', encoding='utf-8') as f:");
        sb.AppendLine("            content = f.read()");
        sb.AppendLine("        ");
        sb.AppendLine("        if re.search(PATTERN, content):");
        sb.AppendLine("            new_content = re.sub(PATTERN, REPLACEMENT, content)");
        sb.AppendLine("            with open(filepath, 'w', encoding='utf-8') as f:");
        sb.AppendLine("                f.write(new_content)");
        sb.AppendLine("            print(f'✅ Yama başarıyla uygulandı: {filepath}')");
        sb.AppendLine("        else:");
        sb.AppendLine("            print(f'☑️ Dosya temiz (Kural bulunamadı): {filepath}')");
        sb.AppendLine("    except Exception as e:");
        sb.AppendLine("        print(f'❌ Hata ({filepath}): {e}')");
        sb.AppendLine();
        sb.AppendLine("if __name__ == '__main__':");
        sb.AppendLine("    if len(sys.argv) < 2:");
        sb.AppendLine("        print('Kullanım: python patch.py <dosya_yolu>')");
        sb.AppendLine("        sys.exit(1)");
        sb.AppendLine("    ");
        sb.AppendLine("    apply_patch(sys.argv[1])");
        return sb.ToString();
    }

    // Bağımsız bir Bash yaması (script) üretir
    public static string GenerateBashPatch(string ruleName, string searchString, string replaceString)
    {
        var sb = new StringBuilder();
        sb.AppendLine("#!/bin/bash");
        sb.AppendLine("# ==========================================");
        sb.AppendLine($"# NoBOMSuite Bağımsız Yama: {ruleName}");
        sb.AppendLine("# ==========================================");
        sb.AppendLine();
        sb.AppendLine("if [ -z \"$1\" ]; then");
        sb.AppendLine("    echo \"Kullanım: ./patch.sh <dosya_yolu>\"");
        sb.AppendLine("    exit 1");
        sb.AppendLine("fi");
        sb.AppendLine();
        sb.AppendLine("FILE=$1");
        sb.AppendLine($"SEARCH=\"{searchString}\"");
        sb.AppendLine($"REPLACE=\"{replaceString}\"");
        sb.AppendLine();
        sb.AppendLine("if grep -q \"$SEARCH\" \"$FILE\"; then");
        // sed kullanarak dosya içinde hedef değişikliği yapar
        sb.AppendLine("    sed -i \"s/$SEARCH/$REPLACE/g\" \"$FILE\"");
        sb.AppendLine("    echo \"✅ Yama başarıyla uygulandı: $FILE\"");
        sb.AppendLine("else");
        sb.AppendLine("    echo \"☑️ Dosya temiz (Kural bulunamadı): $FILE\"");
        sb.AppendLine("fi");
        return sb.ToString();
    }

    public static void ExportPatch(string outputPath, string content)
    {
        File.WriteAllText(outputPath, content, new UTF8Encoding(false)); // Dosyayı BOM'suz kaydet
    }
}
