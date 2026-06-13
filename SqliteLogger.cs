using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace SanitizerKit.Core.Logging;

public static class SqliteLogger
{
    private static readonly string DbPath = Path.Combine(Environment.CurrentDirectory, "devguard_logs.db");
    private static readonly object DbLock = new();

    public static void Log(string level, string message, string? sourceFile = null)
    {
        try
        {
            // SQLite'ın çoklu thread erişimi kısıtlı olduğu için basit bir lock mekanizması kullanıyoruz.
            // Yüksek frekanslı loglama için bu yapı daha gelişmiş bir kuyruk (queue) sistemine dönüştürülebilir.
            lock (DbLock)
            {
                using var connection = new SqliteConnection($"Data Source={DbPath}");
                connection.Open();

                var command = connection.CreateCommand();
                command.CommandText = @"
                    INSERT INTO Logs (Timestamp, Level, Message, SourceFile)
                    VALUES ($timestamp, $level, $message, $sourceFile)
                ";

                command.Parameters.AddWithValue("$timestamp", DateTime.UtcNow.ToString("o")); // ISO 8601
                command.Parameters.AddWithValue("$level", level);
                command.Parameters.AddWithValue("$message", message);
                command.Parameters.AddWithValue("$sourceFile", sourceFile ?? (object)DBNull.Value);

                command.ExecuteNonQuery();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[HATA] SQLite loglama başarısız: {ex.Message}");
        }
    }
}