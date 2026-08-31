using System;
using System.Diagnostics;
using System.IO;
using System.Xml.Linq;
using Xunit;

namespace NoBOMSuite.Tests;

public class CliTests
{
    [Fact]
    public void Cli_Should_Generate_JUnitReport_With_Failures_For_Issues()
    {
        // İzole bir test klasörü oluştur
        string testDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(testDir);
        
        try
        {
            string targetPath = Path.Combine(testDir, "test_file.txt");
            string reportPath = Path.Combine(testDir, "report.xml");
            
            // 1. Sorunlu bir dosya oluştur (BOM + CRLF)
            byte[] bomBytes = { 0xEF, 0xBB, 0xBF };
            byte[] contentBytes = System.Text.Encoding.UTF8.GetBytes("line1\r\nline2\r\n");
            byte[] fullBytes = new byte[bomBytes.Length + contentBytes.Length];
            Buffer.BlockCopy(bomBytes, 0, fullBytes, 0, bomBytes.Length);
            Buffer.BlockCopy(contentBytes, 0, fullBytes, bomBytes.Length, contentBytes.Length);
            
            File.WriteAllBytes(targetPath, fullBytes);
            
            // 2. CLI'yı ÖNCEDEN DERLENMİŞ ikiliyle çalıştır.
            //
            // Eskiden burada "dotnet run --project ..." vardı: test kendi içinde
            // BİR DERLEME başlatıyordu. Eşzamanlı bir `dotnet build` varsa aynı
            // obj/ ve bin/ dizinleri üzerinde yarışırlar ve test SEBEPSİZ düşer.
            // 2026-08-31'de canlı görüldü: build+test peş peşe koşunca 2 test
            // düştü, tek başına 6 turda hiç düşmedi. CI'da paralel iş varsa aynı
            // kırılganlık oradadır.
            //
            // Ayrıca sabit bir "/home/<kullanıcı>/..." yedek yolu vardı — başka
            // hiçbir makinede anlamı yok. Kaldırıldı: kök bulunamazsa test
            // SESSİZCE yanlış yere bakmak yerine AÇIKÇA düşer.
            string projectDir = AppContext.BaseDirectory;
            while (!string.IsNullOrEmpty(projectDir) && !File.Exists(Path.Combine(projectDir, "NoBOMSuite.slnx")))
            {
                projectDir = Path.GetDirectoryName(projectDir) ?? string.Empty;
            }
            Assert.False(string.IsNullOrEmpty(projectDir),
                "Depo kökü (NoBOMSuite.slnx) bulunamadı — testin öznesi yok.");

            string cliBinary = Path.Combine(AppContext.BaseDirectory, "SanitizerKit.CLI.dll");
            Assert.True(File.Exists(cliBinary),
                $"CLI ikilisi yok: {cliBinary}. TestFixtures, CLI'ya proje referansı taşımalı.");

            var processInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"\"{cliBinary}\" \"{targetPath}\" --format junit --output \"{reportPath}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = projectDir
            };
            
            using var process = Process.Start(processInfo);
            Assert.NotNull(process);
            string stdout = process.StandardOutput.ReadToEnd();
            string stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();
            
            // CLI exit code should be 1 since there's an unresolved issue
            Assert.Equal(1, process.ExitCode);
            
            // 3. Rapor dosyasının varlığını ve içeriğini doğrula
            Assert.True(File.Exists(reportPath), $"Report file not found at {reportPath}. stdout: {stdout}, stderr: {stderr}");
            
            string xmlContent = File.ReadAllText(reportPath);
            var doc = XDocument.Parse(xmlContent);
            
            var testsuites = doc.Element("testsuites");
            Assert.NotNull(testsuites);
            Assert.Equal("1", testsuites.Attribute("failures")?.Value);
            
            var testcase = testsuites.Element("testsuite")?.Element("testcase");
            Assert.NotNull(testcase);
            Assert.Equal(targetPath, testcase.Attribute("name")?.Value);
            
            var failure = testcase.Element("failure");
            Assert.NotNull(failure);
            Assert.Contains("BOM", failure.Attribute("message")?.Value);
            Assert.Contains("CRLF", failure.Attribute("message")?.Value);
        }
        finally
        {
            // 4. Temizlik
            if (Directory.Exists(testDir))
            {
                Directory.Delete(testDir, true);
            }
        }
    }
}
