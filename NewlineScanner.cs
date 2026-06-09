using System;

namespace SanitizerKit.Core.Scanners;

public class NewlineScanner : IScanner
{
    public bool HasIssue(ReadOnlySpan<byte> content)
    {
        if (content.IsEmpty) 
            return false; // Dosya tamamen boşsa sorun yok sayılır

        // POSIX standartlarına göre dosyanın son karakteri '\n' (0x0A) olmalıdır.
        // Eğer değilse (örneğin son satırda Enter'a basılmamışsa) sorun var demektir.
        return content[^1] != 0x0A;
    }
}
