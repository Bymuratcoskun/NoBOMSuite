using System;
using System.IO;
// Microsoft.Data.Sqlite paketi gerektirir (dotnet add package Microsoft.Data.Sqlite)
using Microsoft.Data.Sqlite;

namespace SanitizerKit.Core.Logging;

public class LogDatabaseManager
{
    public static void InitializeDatabase(string dbPath)
    {
        try
        {
            bool exists = File.Exists(dbPath);
            if (!exists)
            {
                // Dosyayı oluştur
                File.WriteAllBytes(dbPath, Array.Empty<byte>());
            }

            // SQLite paketi eklendikten sonra aşağıdaki kod aktif edilebilir:
            using var connection = new SqliteConnection($"Data Source={dbPath}");
            connection.Open();

            string createTableQuery = @"
                CREATE TABLE IF NOT EXISTS Logs (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Timestamp TEXT NOT NULL,
                    Level TEXT NOT NULL,
                    Message TEXT NOT NULL,
                    SourceFile TEXT NULL
                );";
            
            using (var command = new SqliteCommand(createTableQuery, connection))
            {
                command.ExecuteNonQuery();
            }

            string createCacheTableQuery = @"
                CREATE TABLE IF NOT EXISTS FileCache (
                    FilePath TEXT PRIMARY KEY,
                    LastWriteTime TEXT NOT NULL,
                    FileHash TEXT NOT NULL,
                    HasIssues INTEGER NOT NULL
                );";

            using (var command = new SqliteCommand(createCacheTableQuery, connection))
            {
                command.ExecuteNonQuery();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[HATA] SQLite veritabanı oluşturulamadı: {ex.Message}");
        }
    }
}