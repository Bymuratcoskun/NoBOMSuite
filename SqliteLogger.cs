using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace SanitizerKit.Core.Logging;

public static class SqliteLogger
{
    private static readonly string DbPath = Path.Combine(Environment.CurrentDirectory, "devguard_logs.db");
    private static readonly object DbLock = new();
    private static bool _semaKuruldu;

    /// <summary>
    /// Şemayı GARANTİ eder. Yazan taraf kendi tablosunu kurmalı.
    ///
    /// NEDEN VAR (2026-08-29): Log() doğrudan INSERT yapıyordu ve tabloyu hiç
    /// oluşturmuyordu. Şemayı kuran tek çağrı (LogDatabaseManager.Initialize)
    /// MainWindow'daki DIŞA AKTARMA akışının içindeydi, normal açılışta hiç
    /// çalışmıyordu. Sonuç: her log yazımı sessizce başarısız oluyor
    /// (catch bloğu yutuyor), panel ve log görüntüleyici de açılışta
    /// "no such table: Logs" hatası veriyordu.
    ///
    /// Ders: bir bileşenin ihtiyacı olan durumu BAŞKASININ kurmasına güvenmek,
    /// o başkası çağrılmadığında sessiz arıza üretir.
    /// </summary>
    private static void SemayiGarantile(SqliteConnection connection)
    {
        if (_semaKuruldu) return;
        var kur = connection.CreateCommand();
        kur.CommandText = @"
            CREATE TABLE IF NOT EXISTS Logs (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Timestamp TEXT NOT NULL,
                Level TEXT NOT NULL,
                Message TEXT NOT NULL,
                SourceFile TEXT NULL
            );";
        kur.ExecuteNonQuery();
        _semaKuruldu = true;
    }

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
                SemayiGarantile(connection);

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