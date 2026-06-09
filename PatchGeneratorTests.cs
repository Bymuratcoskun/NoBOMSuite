using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Xunit;
using SanitizerKit.Core.Patching;

namespace NoBOMSuite.Tests;

public class PatchGeneratorTests
{
    [Fact]
    public void PythonPatch_Should_ModifyFile_Successfully()
    {
        // İşletim sisteminde python/python3 komutlarını algıla
        string pythonCmd = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "python" : "python3";
        
        // İzole bir test klasörü oluştur
        string testDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(testDir);
        
        try
        {
            string patchPath = Path.Combine(testDir, "patch.py");
            string targetPath = Path.Combine(testDir, "target.js");
            
            // 1. Hedef dosya içeriğini hazırla
            File.WriteAllText(targetPath, "var password = \"1234\";\nconsole.log(password);");
            
            // 2. Python Yamasını (PatchGenerator ile) oluştur
            string patchContent = PatchGenerator.GeneratePythonPatch(
                "Hardcoded Password Masker",
                "password\\s*=\\s*['\"](.*?)['\"]",
                "password = \"[MASKED]\""
            );
            File.WriteAllText(patchPath, patchContent);
            
            // 3. Çalıştır (Terminalden otomatik test)
            var processInfo = new ProcessStartInfo
            {
                FileName = pythonCmd,
                Arguments = $"\"{patchPath}\" \"{targetPath}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            
            using var process = Process.Start(processInfo);
            Assert.NotNull(process);
            process.WaitForExit();
            
            // 4. Assert: Hedef dosya değişti mi?
            string updatedContent = File.ReadAllText(targetPath);
            Assert.Contains("[MASKED]", updatedContent);
            Assert.DoesNotContain("1234", updatedContent);
        }
        finally
        {
            // 5. Cleanup
            if (Directory.Exists(testDir))
            {
                Directory.Delete(testDir, true);
            }
        }
    }
}