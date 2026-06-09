using System;

namespace SanitizerKit.Core.Scanners;

public class LineEndingScanner : IScanner
{
    public bool HasIssue(ReadOnlySpan<byte> content)
    {
        // Eğer dosya içinde Windows tarzı CRLF (0x0D, 0x0A) varsa yakala
        // Linux/Mac ortamları için veya LF zorunlu projeler için bir "sorun" teşkil eder.
        ReadOnlySpan<byte> crlf = [0x0D, 0x0A];
        return content.IndexOf(crlf) >= 0;
    }
}
