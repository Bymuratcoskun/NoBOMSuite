using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using SanitizerKit.Core.Scanners;

namespace InvokeScan
{
    class Program
    {
        // Taranmayacak sistem/derleme klasörleri
        static readonly HashSet<string> IgnoredDirs = new(StringComparer.OrdinalIgnoreCase) 
        { 
            ".git", "node_modules", "bin", "obj", ".vs", ".idea", "dist", "build", ".nobom", "publish-cli", "publish-desktop" 
        };

        static int Main(string[] args)
        {
            if (args.Length == 0 || args.Contains("--help") || args.Contains("-h"))
            {
                Console.WriteLine("🛡️  DevGuard (NoBOMSuite) CLI - Komut Satırı Tarayıcısı");
                Console.WriteLine("Kullanım: SanitizerKit.CLI <dosya_veya_klasor_yolu> [seçenekler]");
                Console.WriteLine("\nSeçenekler:");
                Console.WriteLine("  --help, -h       Bu yardım menüsünü gösterir.");
                Console.WriteLine("  --auto-fix       Tespit edilen sorunları anında otomatik onarır.");
                Console.WriteLine("  --interactive    Sorun bulunduğunda onarmak için kullanıcıya sorar.");
                return 0;
            }

            var path = args.FirstOrDefault(a => !a.StartsWith("-")) ?? ".";
            bool autoFix = args.Contains("--auto-fix");
            bool interactive = args.Contains("--interactive");

            if (!File.Exists(path) && !Directory.Exists(path))
            {
                Console.WriteLine($"❌ Yol bulunamadı: {path}");
                return 1;
            }

            Console.WriteLine($"🔍 Tarama başlatılıyor: {Path.GetFullPath(path)}");
            
            int scannedCount = 0;
            int issueCount = 0;
            int fixedCount = 0;

            if (File.Exists(path))
            {
                ScanFile(path, ref scannedCount, ref issueCount, ref fixedCount, autoFix, interactive);
            }
            else
            {
                ScanDirectory(path, ref scannedCount, ref issueCount, ref fixedCount, autoFix, interactive);
            }

            Console.WriteLine("\n--- 📊 Tarama Sonuçları ---");
            Console.WriteLine($"Taranan Dosya Sayısı: {scannedCount}");
            Console.WriteLine($"Sorunlu Dosya Sayısı: {issueCount}");
            Console.WriteLine($"Onarılan Dosya Sayısı: {fixedCount}");

            if (issueCount == 0)
            {
                Console.WriteLine("\n✅ Mükemmel! Tüm dosyalar temiz.");
            }

            return (issueCount > 0 && fixedCount < issueCount) ? 1 : 0;
        }

        static void ScanDirectory(string dir, ref int scannedCount, ref int issueCount, ref int fixedCount, bool autoFix, bool interactive)
        {
            try
            {
                foreach (var file in Directory.GetFiles(dir))
                {
                    ScanFile(file, ref scannedCount, ref issueCount, ref fixedCount, autoFix, interactive);
                }

                foreach (var subDir in Directory.GetDirectories(dir))
                {
                    if (!IgnoredDirs.Contains(Path.GetFileName(subDir)))
                    {
                        ScanDirectory(subDir, ref scannedCount, ref issueCount, ref fixedCount, autoFix, interactive);
                    }
                }
            }
            catch (UnauthorizedAccessException)
            {
                // Yetki olmayan sistem klasörlerini sessizce atla
            }
        }

        static void ScanFile(string path, ref int scannedCount, ref int issueCount, ref int fixedCount, bool autoFix, bool interactive)
        {
            // Binary (İkili) dosyaları atlamak için uzantı kontrolü
            string ext = Path.GetExtension(path).ToLowerInvariant();
            string[] binaryExts = { ".exe", ".dll", ".png", ".jpg", ".zip", ".bin", ".so", ".dylib" };
            if (Array.IndexOf(binaryExts, ext) >= 0) return;

            try
            {
                byte[] content = File.ReadAllBytes(path);
                var span = new ReadOnlySpan<byte>(content);
                scannedCount++;

                bool hasIssue = false;
                var issues = new List<string>();
                bool fixBom = false, fixCrlf = false, fixGhost = false, fixNewline = false, fixTab = false, fixPassword = false;

                if (new BomScanner().HasIssue(span)) { hasIssue = true; issues.Add("BOM"); fixBom = true; }
                if (new LineEndingScanner().HasIssue(span)) { hasIssue = true; issues.Add("CRLF"); fixCrlf = true; }
                if (new GhostCharScanner().HasIssue(span)) { hasIssue = true; issues.Add("GhostChar"); fixGhost = true; }
                if (new NewlineScanner().HasIssue(span)) { hasIssue = true; issues.Add("NoEOFNewline"); fixNewline = true; }
                if (new TabScanner().HasIssue(span)) { hasIssue = true; issues.Add("Tab"); fixTab = true; }
                if (new HardcodedPasswordScanner().HasIssue(span)) { hasIssue = true; issues.Add("HardcodedPass"); fixPassword = true; }

                if (hasIssue)
                {
                    issueCount++;
                    Console.WriteLine($"[SORUN] {path} -> ({string.Join(", ", issues)})");

                    bool shouldFix = autoFix;
                    
                    if (interactive && !autoFix)
                    {
                        Console.Write($"  ❓ Bu dosyayı onarmak istiyor musunuz? (E/h): ");
                        var keyInfo = Console.ReadKey();
                        Console.WriteLine();
                        if (keyInfo.Key == ConsoleKey.E || keyInfo.Key == ConsoleKey.Enter)
                        {
                            shouldFix = true;
                        }
                    }

                    if (shouldFix)
                    {
                        var outputBytes = new List<byte>(content);
                        
                        if (fixBom) outputBytes.RemoveRange(0, 3);
                        if (fixCrlf || fixGhost || fixTab || fixPassword)
                        {
                            string tempText = Encoding.UTF8.GetString(outputBytes.ToArray());
                            if (fixCrlf) tempText = tempText.Replace("\r\n", "\n");
                            if (fixGhost) tempText = tempText.Replace("\u200B", "");
                            if (fixTab) tempText = tempText.Replace("\t", "    ");
                            if (fixPassword)
                            {
                                var regex = new Regex(@"((password|passwd|pass|secret)\s*[:=]\s*)(['""])(.*?)\3", RegexOptions.IgnoreCase);
                                tempText = regex.Replace(tempText, "$1$3[MASKED_BY_DEVGUARD]$3");
                            }
                            outputBytes = new List<byte>(new UTF8Encoding(false).GetBytes(tempText));
                        }
                        if (fixNewline)
                        {
                            if (outputBytes.Count > 0 && outputBytes[outputBytes.Count - 1] != 0x0A)
                                outputBytes.Add(0x0A);
                        }
                        
                        File.WriteAllBytes(path, outputBytes.ToArray());
                        Console.WriteLine($"  ✨ [ONARILDI] {path}");
                        fixedCount++;
                    }
                }
            }
            catch
            {
                // Okunamayan dosyaları sessizce atla
            }
        }
    }
}
