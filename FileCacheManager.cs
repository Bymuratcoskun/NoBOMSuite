using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;

namespace SanitizerKit.Core.Caching;

public static class FileCacheManager
{
    private static readonly string DbPath = Path.Combine(Environment.CurrentDirectory, "devguard_logs.db");
    private static readonly object CacheLock = new();

    public static bool IsCacheValid(string filePath, out bool hasIssues)
    {
        hasIssues = false;
        try
        {
            if (!File.Exists(filePath)) return false;

            string normalizedPath = Path.GetFullPath(filePath).ToLowerInvariant();
            string currentWriteTime = File.GetLastWriteTimeUtc(filePath).ToString("o");

            lock (CacheLock)
            {
                using var connection = new SqliteConnection($"Data Source={DbPath}");
                connection.Open();

                using var command = connection.CreateCommand();
                command.CommandText = "SELECT LastWriteTime, HasIssues FROM FileCache WHERE FilePath = $path";
                command.Parameters.AddWithValue("$path", normalizedPath);

                using var reader = command.ExecuteReader();
                if (reader.Read())
                {
                    string cachedWriteTime = reader.GetString(0);
                    hasIssues = reader.GetInt32(1) == 1;

                    return cachedWriteTime == currentWriteTime;
                }
            }
        }
        catch
        {
            // Veritabanı veya dosya hatası durumunda önbelleği geçersiz say
        }
        return false;
    }

    public static void UpdateCache(string filePath, bool hasIssues)
    {
        try
        {
            if (!File.Exists(filePath)) return;

            string normalizedPath = Path.GetFullPath(filePath).ToLowerInvariant();
            string currentWriteTime = File.GetLastWriteTimeUtc(filePath).ToString("o");
            string hash = ComputeFileHash(filePath);

            lock (CacheLock)
            {
                using var connection = new SqliteConnection($"Data Source={DbPath}");
                connection.Open();

                using var command = connection.CreateCommand();
                command.CommandText = @"
                    INSERT OR REPLACE INTO FileCache (FilePath, LastWriteTime, FileHash, HasIssues)
                    VALUES ($path, $time, $hash, $hasIssues)
                ";
                command.Parameters.AddWithValue("$path", normalizedPath);
                command.Parameters.AddWithValue("$time", currentWriteTime);
                command.Parameters.AddWithValue("$hash", hash);
                command.Parameters.AddWithValue("$hasIssues", hasIssues ? 1 : 0);

                command.ExecuteNonQuery();
            }
        }
        catch
        {
            // Önbellek yazma hatasını sessizce yoksay
        }
    }

    private static string ComputeFileHash(string filePath)
    {
        try
        {
            using var sha256 = SHA256.Create();
            using var stream = File.OpenRead(filePath);
            byte[] hashBytes = sha256.ComputeHash(stream);
            var sb = new StringBuilder();
            foreach (byte b in hashBytes)
            {
                sb.Append(b.ToString("x2"));
            }
            return sb.ToString();
        }
        catch
        {
            return string.Empty;
        }
    }
}
