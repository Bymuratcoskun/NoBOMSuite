using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using SanitizerKit.Core.Scanners;
using SanitizerKit.Core.Caching;
using SanitizerKit.Core.Config;

namespace InvokeScan
{
    class Program
    {
        public class ScanResult
        {
            public string FilePath { get; set; } = string.Empty;
            public bool HasIssue { get; set; }
            public List<string> Issues { get; set; } = new();
        }

        static readonly List<ScanResult> ScanResults = new();

        // Taranmayacak sistem/derleme klasörleri
        static readonly HashSet<string> IgnoredDirs = new(StringComparer.OrdinalIgnoreCase) 
        { 
            ".git", "node_modules", "bin", "obj", ".vs", ".idea", "dist", "build", ".nobom", "publish-cli", "publish-desktop",
            "__pycache__", ".venv", "venv", "target", ".mypy_cache", ".pytest_cache", ".ruff_cache" 
        };

        static int Main(string[] args)
        {
            if (args.Contains("--help") || args.Contains("-h"))
            {
                ShowHelp();
                return 0;
            }

            // ── SESSİZ KIRILMA DENETİMİ ────────────────────────────────────
            // Diğer kipler dosya İÇİNDEKİ baytlara bakar. Bu kip DEPO YAPISINA
            // bakar: derleyici susar, testler yeşildir, ama bir şey hiç
            // çalışmıyordur (yanlış dizindeki CI hattı, boş çözüm dosyası,
            // karşılıksız lisans beyanı, eksik eklenti manifestosu...).
            if (args.Contains("--saglamlik") || args.Contains("--soundness"))
            {
                var kokDizin = ".";
                for (int i = 0; i < args.Length - 1; i++)
                    if (args[i] == "--saglamlik" || args[i] == "--soundness")
                    {
                        if (!args[i + 1].StartsWith("--")) kokDizin = args[i + 1];
                    }

                var bulgular = SanitizerKit.Core.Soundness.SessizKirilma.Denetle(kokDizin);
                bool jsonCikti = args.Contains("--format") &&
                                 Array.IndexOf(args, "--format") + 1 < args.Length &&
                                 args[Array.IndexOf(args, "--format") + 1] == "json";

                if (jsonCikti)
                {
                    Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(bulgular,
                        new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
                }
                else if (bulgular.Count == 0)
                {
                    Console.WriteLine($"✅ Sessiz kırılma denetimi: {Path.GetFullPath(kokDizin)} temiz.");
                }
                else
                {
                    Console.WriteLine($"⚠️  {bulgular.Count} sessiz kırılma bulgusu — "
                                      + "\"iyi görünüyor ama çalışmıyor\" sınıfı:\n");
                    foreach (var b in bulgular)
                    {
                        Console.WriteLine($"  [{b.Kod}] {Path.GetFileName(b.Yol)}");
                        Console.WriteLine($"     {b.Mesaj}");
                        Console.WriteLine($"     ↳ {b.Neden}");
                        Console.WriteLine($"     {b.Yol}\n");
                    }
                }
                return bulgular.Count == 0 ? 0 : 1;
            }

            var path = ".";
            bool autoFix = args.Contains("--auto-fix");
            bool interactive = args.Contains("--interactive");
            bool formatJunit = false;
            string junitReportPath = "junit-report.xml";

            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--auto-fix")
                {
                    autoFix = true;
                }
                else if (args[i] == "--interactive")
                {
                    interactive = true;
                }
                else if (args[i] == "--format" && i + 1 < args.Length)
                {
                    if (args[i + 1].Equals("junit", StringComparison.OrdinalIgnoreCase))
                    {
                        formatJunit = true;
                    }
                    i++;
                }
                else if (args[i].StartsWith("--format=", StringComparison.OrdinalIgnoreCase))
                {
                    if (args[i].Substring(9).Equals("junit", StringComparison.OrdinalIgnoreCase))
                    {
                        formatJunit = true;
                    }
                }
                else if (args[i] == "--output" && i + 1 < args.Length)
                {
                    junitReportPath = args[i + 1];
                    i++;
                }
                else if (args[i].StartsWith("--output=", StringComparison.OrdinalIgnoreCase))
                {
                    junitReportPath = args[i].Substring(9);
                }
                else if (!args[i].StartsWith("-"))
                {
                    path = args[i];
                }
            }

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

            if (formatJunit)
            {
                ExportJUnitReport(junitReportPath, ScanResults);
            }

            return (issueCount > 0 && fixedCount < issueCount) ? 1 : 0;
        }

        static void ShowHelp()
        {
            Console.WriteLine("🛡️  DevGuard (NoBOMSuite) CLI - Komut Satırı Tarayıcısı");
            Console.WriteLine("Kullanım: SanitizerKit.CLI <dosya_veya_klasor_yolu> [seçenekler]");
            Console.WriteLine("\nSeçenekler:");
            Console.WriteLine("  --help, -h       Bu yardım menüsünü gösterir.");
            Console.WriteLine("  --auto-fix       Tespit edilen sorunları anında otomatik onarır.");
            Console.WriteLine("  --interactive    Sorun bulunduğunda onarmak için kullanıcıya sorar.");
            Console.WriteLine("  --format junit   JUnit XML formatında rapor üretir.");
            Console.WriteLine("  --output <path>  JUnit XML rapor dosyasının kaydedileceği yol (Varsayılan: junit-report.xml).");
            Console.WriteLine("  --saglamlik [dizin]  SESSİZ KIRILMA denetimi: iyi görünüp çalışmayan yapılar");
            Console.WriteLine("                       (yanlış dizindeki CI hattı, boş çözüm dosyası,");
            Console.WriteLine("                        karşılıksız lisans, eksik eklenti manifestosu, bayat kara liste)");

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

            // Önbellek kontrolü (Incremental Scan)
            if (FileCacheManager.IsCacheValid(path, out bool cachedHasIssues))
            {
                if (!cachedHasIssues)
                {
                    scannedCount++;
                    ScanResults.Add(new ScanResult { FilePath = path, HasIssue = false });
                    return; // Temiz ve değişmemiş dosya, taramayı es geç.
                }
            }

            try
            {
                byte[] content = File.ReadAllBytes(path);
                var span = new ReadOnlySpan<byte>(content);

                // İKİLİ DOSYA TARANMAZ. Dizin listesi hiç tamamlanmaz; içeriğe
                // bakmak yapısal çözümdür. Ölçüm: bu denetim olmadan gerçek bir
                // depoda bulguların %35'i .pyc dosyalarından gelen yanlış alarmdı.
                if (IkiliTespit.Ikili(span))
                {
                    return;
                }
                scannedCount++;

                var result = new ScanResult { FilePath = path };
                bool hasIssue = false;
                var issues = new List<string>();
                bool fixBom = false, fixCrlf = false, fixGhost = false, fixNewline = false, fixTab = false, fixPassword = false;

                if (new BomScanner().HasIssue(span)) { hasIssue = true; issues.Add("BOM"); fixBom = true; }
                if (new LineEndingScanner().HasIssue(span)) { hasIssue = true; issues.Add("CRLF"); fixCrlf = true; }
                if (new GhostCharScanner().HasIssue(span)) { hasIssue = true; issues.Add("GhostChar"); fixGhost = true; }
                if (new NewlineScanner().HasIssue(span)) { hasIssue = true; issues.Add("NoEOFNewline"); fixNewline = true; }
                // Aşağıdaki üçü RAPOR EDİLİR, otomatik onarılmaz — onarımları
                // ya bağlama bağlı (NBSP→boşluk) ya da imkânsız (U+FFFD:
                // özgün karakter zaten kayıp) ya da meşru kullanımı var (bidi).
                if (new InvisibleWhitespaceScanner().HasIssue(span)) { hasIssue = true; issues.Add("InvisibleWS"); }
                if (new ReplacementCharScanner().HasIssue(span)) { hasIssue = true; issues.Add("BrokenDecode"); }
                if (new BidiScanner().HasIssue(span)) { hasIssue = true; issues.Add("BidiTrojan"); }
                if (new TabScanner().HasIssue(span)) { hasIssue = true; issues.Add("Tab"); fixTab = true; }
                if (new HardcodedPasswordScanner().HasIssue(span)) { hasIssue = true; issues.Add("HardcodedPass"); fixPassword = true; }

                var config = BomConfigManager.LoadConfig(Path.Combine(Environment.CurrentDirectory, ".bomconfig"));
                if (config.EnabledModules.TryGetValue("EntropyScanner", out bool isEntropyEnabled) && isEntropyEnabled)
                {
                    if (new EntropyScanner().HasIssue(span)) { hasIssue = true; issues.Add("HighEntropySecret"); }
                }

                if (hasIssue)
                {
                    issueCount++;
                    Console.WriteLine($"[SORUN] {path} -> ({string.Join(", ", issues)})");
                    result.HasIssue = true;
                    result.Issues.AddRange(issues);

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
                            if (fixGhost)
                            {
                                // GhostCharScanner BES karakter tespit ediyor; burada
                                // yalnizca U+200B siliniyordu. Kalan dordu icin "ONARILDI"
                                // deniyor ama dosya degismiyordu (2026-08-29 olcumu:
                                // yumusak tire hic silinmiyor, ucu bir arada olan dosyada
                                // 27 bayttan yalniz 3'u gidiyordu).
                                foreach (var hayalet in new[] { "\u200B", "\u200C", "\u200D", "\u2060", "\u00AD" })
                                    tempText = tempText.Replace(hayalet, "");
                            }
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

                        // ONARIMI DOGRULA. Eskiden burasi kosulsuz "ONARILDI" yazip
                        // onbellegi "temiz" isaretliyordu -- onarilmamis dosyalar dahil.
                        // Bu bir sahte-basari idi ve kendini kalicilastiriyordu: onbellek
                        // temiz diyince sonraki taramalar dosyayi atlayabiliyordu.
                        // "Hata vermedi" onarim kaniti degildir; ESERE bakilir.
                        var yeniden = new ReadOnlySpan<byte>(outputBytes.ToArray());
                        var kalan = new List<string>();
                        if (new BomScanner().HasIssue(yeniden)) kalan.Add("BOM");
                        if (new LineEndingScanner().HasIssue(yeniden)) kalan.Add("CRLF");
                        if (new GhostCharScanner().HasIssue(yeniden)) kalan.Add("GhostChar");
                        if (new NewlineScanner().HasIssue(yeniden)) kalan.Add("NoEOFNewline");
                        if (new TabScanner().HasIssue(yeniden)) kalan.Add("Tab");
                        if (new InvisibleWhitespaceScanner().HasIssue(yeniden)) kalan.Add("InvisibleWS");
                        if (new ReplacementCharScanner().HasIssue(yeniden)) kalan.Add("BrokenDecode");
                        if (new BidiScanner().HasIssue(yeniden)) kalan.Add("BidiTrojan");
                        if (new HardcodedPasswordScanner().HasIssue(yeniden)) kalan.Add("HardcodedPass");

                        if (kalan.Count == 0)
                        {
                            Console.WriteLine($"  ✨ [ONARILDI] {path}");
                            fixedCount++;
                            FileCacheManager.UpdateCache(path, hasIssues: false);
                        }
                        else
                        {
                            // Kismi onarim da onarim degildir: kullanici neyin KALDIGINI gormeli.
                            Console.WriteLine($"  ⚠️  [KISMEN] {path} -> onarilamayan: ({string.Join(", ", kalan)})");
                            FileCacheManager.UpdateCache(path, hasIssues: true);
                        }
                    }
                    else
                    {
                        FileCacheManager.UpdateCache(path, hasIssues: true);
                    }
                }
                else
                {
                    FileCacheManager.UpdateCache(path, hasIssues: false);
                }

                ScanResults.Add(result);
            }
            catch
            {
                // Okunamayan dosyaları sessizce atla
            }
        }

        static string EscapeXml(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            return value.Replace("&", "&amp;")
                        .Replace("<", "&lt;")
                        .Replace(">", "&gt;")
                        .Replace("\"", "&quot;")
                        .Replace("'", "&apos;");
        }

        static void ExportJUnitReport(string outputPath, List<ScanResult> results)
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
                int totalTests = results.Count;
                int totalFailures = results.Count(r => r.HasIssue);
                
                sb.AppendLine($"<testsuites name=\"DevGuard Scan\" tests=\"{totalTests}\" failures=\"{totalFailures}\" time=\"0.0\">");
                sb.AppendLine($"  <testsuite name=\"DevGuard Scan\" tests=\"{totalTests}\" failures=\"{totalFailures}\" id=\"0\" time=\"0.0\">");

                foreach (var result in results)
                {
                    string safePath = EscapeXml(result.FilePath);
                    sb.AppendLine($"    <testcase name=\"{safePath}\" classname=\"DevGuardScanner\" time=\"0.0\">");
                    if (result.HasIssue)
                    {
                        string issuesStr = string.Join(", ", result.Issues);
                        string safeIssues = EscapeXml(issuesStr);
                        sb.AppendLine($"      <failure message=\"Issues found: {safeIssues}\" type=\"DevGuardFailure\">");
                        sb.AppendLine($"        File: {safePath}");
                        sb.AppendLine($"        Issues: {safeIssues}");
                        sb.AppendLine("      </failure>");
                    }
                    sb.AppendLine("    </testcase>");
                }

                sb.AppendLine("  </testsuite>");
                sb.AppendLine("</testsuites>");

                File.WriteAllText(outputPath, sb.ToString(), Encoding.UTF8);
                Console.WriteLine($"\n📋 JUnit XML Raporu kaydedildi: {Path.GetFullPath(outputPath)}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\n❌ Rapor yazılırken hata oluştu: {ex.Message}");
            }
        }
    }
}
