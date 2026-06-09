using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace SanitizerKit.Core.AI;

public class LocalAiFirewall
{
    private static byte[] GetMachineKey()
    {
        // Makineye özel benzersiz bir şifreleme anahtarı üretir (Cross-platform)
        string machineName = Environment.MachineName;
        string userName = Environment.UserName;
        string combined = $"{machineName}_{userName}_DevGuard_NoBOM";
        
        using var sha256 = SHA256.Create();
        return sha256.ComputeHash(Encoding.UTF8.GetBytes(combined));
    }

    public static string EncryptApiKey(string plainApiKey)
    {
        if (string.IsNullOrEmpty(plainApiKey)) return string.Empty;

        using var aes = Aes.Create();
        aes.Key = GetMachineKey();
        aes.GenerateIV();

        using var encryptor = aes.CreateEncryptor(aes.Key, aes.IV);
        using var ms = new MemoryStream();
        
        // IV'yi en başa ekle ki çözerken kullanabilelim
        ms.Write(aes.IV, 0, aes.IV.Length);
        
        using (var cs = new CryptoStream(ms, encryptor, CryptoStreamMode.Write))
        using (var sw = new StreamWriter(cs))
        {
            sw.Write(plainApiKey);
        }

        return Convert.ToBase64String(ms.ToArray());
    }

    public static string DecryptApiKey(string encryptedApiKey)
    {
        if (string.IsNullOrEmpty(encryptedApiKey)) return string.Empty;

        try
        {
            byte[] fullCipher = Convert.FromBase64String(encryptedApiKey);
            
            using var aes = Aes.Create();
            aes.Key = GetMachineKey();
            
            byte[] iv = new byte[aes.BlockSize / 8];
            Array.Copy(fullCipher, 0, iv, 0, iv.Length);
            aes.IV = iv;

            using var decryptor = aes.CreateDecryptor(aes.Key, aes.IV);
            using var ms = new MemoryStream(fullCipher, iv.Length, fullCipher.Length - iv.Length);
            using var cs = new CryptoStream(ms, decryptor, CryptoStreamMode.Read);
            using var sr = new StreamReader(cs);
            
            return sr.ReadToEnd();
        }
        catch
        {
            return string.Empty; // Şifre çözülemezse (farklı makineye kopyalanmışsa) boş döner
        }
    }

    public static string MaskSensitiveData(string codeSnippet)
    {
        if (string.IsNullOrEmpty(codeSnippet)) return codeSnippet;

        string maskedCode = codeSnippet;

        // 1. Şifre ve Parola Maskeleme (password = "...", pass: '...' vb.)
        var passwordRegex = new Regex(@"(password|passwd|pass|secret)\s*[:=]\s*(['""])(.*?)\2", RegexOptions.IgnoreCase);
        maskedCode = passwordRegex.Replace(maskedCode, "$1 = $2[MASKED_BY_DEVGUARD]$2");

        // 2. API Anahtarı Maskeleme (api_key = "...", apiKey: '...' vb.)
        var apiKeyRegex = new Regex(@"(api[_-]?key|token|auth)\s*[:=]\s*(['""])(.*?)\2", RegexOptions.IgnoreCase);
        maskedCode = apiKeyRegex.Replace(maskedCode, "$1 = $2[MASKED_BY_DEVGUARD]$2");

        // 3. Email Adresleri Maskeleme
        var emailRegex = new Regex(@"[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}");
        maskedCode = emailRegex.Replace(maskedCode, "[EMAIL_MASKED]");

        return maskedCode;
    }
}
