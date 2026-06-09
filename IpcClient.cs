using System;
using System.IO;
using System.IO.Pipes;
using System.Threading.Tasks;

namespace SanitizerKit.Core.IPC;

/// <summary>
/// IDE Eklentisi veya başka bir sürecin, çalışan DevGuard Masaüstü uygulamasına
/// komut göndermesini sağlayan istemci.
/// </summary>
public class IpcClient
{
    private const string PipeName = "devguard_ipc_bridge";
    private const int Timeout = 1000; // 1 saniye bağlantı zaman aşımı

    private static async Task SendMessageAsync(string message)
    {
        try
        {
            // Sunucuya (Masaüstü Uygulaması) bağlanmayı dene
            using var pipeClient = new NamedPipeClientStream(".", PipeName, PipeDirection.Out, PipeOptions.Asynchronous);
            await pipeClient.ConnectAsync(Timeout);
            
            // Mesajı gönder
            using var writer = new StreamWriter(pipeClient);
            await writer.WriteLineAsync(message);
            await writer.FlushAsync();
        }
        catch (TimeoutException)
        {
            // DevGuard Desktop uygulaması çalışmıyorsa bu hata alınır. Sorun değil.
        }
        catch (Exception)
        {
            // Diğer olası hataları yoksay
        }
    }

    public static Task SendLockAsync(string filePath) => SendMessageAsync($"LOCK|{filePath}");
    public static Task SendUnlockAsync(string filePath) => SendMessageAsync($"UNLOCK|{filePath}");
    public static Task SendDiagnosticsAsync(string filePath, string diagnosticMessage) => SendMessageAsync($"DIAGNOSTICS|{filePath}|{diagnosticMessage}");
}