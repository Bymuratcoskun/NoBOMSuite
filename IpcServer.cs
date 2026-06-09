using System;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;

namespace SanitizerKit.Core.IPC;

public class IpcServer
{
    // Hem Windows hem Unix uyumluluğu için tekil bir pipe adı
    private const string PipeName = "devguard_ipc_bridge";
    private readonly Action<string> _onMessageReceived;
    private CancellationTokenSource? _cancellationTokenSource;

    public IpcServer(Action<string> onMessageReceived)
    {
        _onMessageReceived = onMessageReceived;
    }

    public void Start()
    {
        _cancellationTokenSource = new CancellationTokenSource();
        _ = Task.Run(() => ListenAsync(_cancellationTokenSource.Token));
    }

    public void Stop()
    {
        _cancellationTokenSource?.Cancel();
    }

    private async Task ListenAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                // PipeTransmissionMode.Byte kullanılması tüm platformlarda sorunsuz çalışmasını güvence altına alır
                using var pipeServer = new NamedPipeServerStream(
                    PipeName, 
                    PipeDirection.InOut, 
                    1, 
                    PipeTransmissionMode.Byte, 
                    PipeOptions.Asynchronous);
                    
                await pipeServer.WaitForConnectionAsync(token);

                using var reader = new StreamReader(pipeServer);
                string? message = await reader.ReadLineAsync(token);

                if (!string.IsNullOrEmpty(message))
                {
                    _onMessageReceived(message);
                }
            }
            catch (OperationCanceledException)
            {
                break; // İptal edildi, döngüden çık
            }
            catch (Exception)
            {
                // Hata veya kopma yaşanırsa bekle ve tekrar dinlemeye çalış
                try
                {
                    await Task.Delay(1000, token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }
}