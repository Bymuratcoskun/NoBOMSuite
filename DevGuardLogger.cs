using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SanitizerKit.Core.Logging;

public enum LogLevel
{
    DEBUG,
    INFO,
    WARNING,
    ERROR
}

public enum LogFormat
{
    Text,
    Json
}

public class LogMessage
{
    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; }

    [JsonPropertyName("level")]
    public string Level { get; set; } = string.Empty;

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;
}

public class DevGuardLogger
{
    private readonly LogFormat _format;

    public DevGuardLogger(LogFormat format = LogFormat.Text)
    {
        _format = format;
    }

    public void Debug(string message) => Log(LogLevel.DEBUG, message);
    public void Info(string message) => Log(LogLevel.INFO, message);
    public void Warning(string message) => Log(LogLevel.WARNING, message);
    public void Error(string message) => Log(LogLevel.ERROR, message);

    private void Log(LogLevel level, string message)
    {
        if (_format == LogFormat.Json)
        {
            LogJson(level, message);
        }
        else
        {
            LogText(level, message);
        }
    }

    private void LogJson(LogLevel level, string message)
    {
        var logObj = new LogMessage
        {
            Timestamp = DateTime.UtcNow,
            Level = level.ToString(),
            Message = message
        };
        
        string json = JsonSerializer.Serialize(logObj);
        Console.WriteLine(json);
    }

    private void LogText(LogLevel level, string message)
    {
        string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        Console.Write($"[{timestamp}] ");

        ConsoleColor originalColor = Console.ForegroundColor;

        Console.ForegroundColor = level switch
        {
            LogLevel.DEBUG => ConsoleColor.DarkGray,
            LogLevel.INFO => ConsoleColor.Cyan,
            LogLevel.WARNING => ConsoleColor.Yellow,
            LogLevel.ERROR => ConsoleColor.Red,
            _ => originalColor
        };

        Console.WriteLine($"[{level.ToString().PadRight(5)}] {message}");
        Console.ForegroundColor = originalColor;
    }
}
