using System;

namespace SanitizerKit.Core.Scanners;

public class GhostCharScanner : IScanner
{
    public bool HasIssue(ReadOnlySpan<byte> content)
    {
        // UTF-8 Zero-Width Space (\u200B): 0xE2, 0x80, 0x8B
        // Genellikle kopyala-yapıştır yapıldığında kodun arasına sızar ve görünmediği için bulunması çok zordur.
        ReadOnlySpan<byte> ghostChar = [0xE2, 0x80, 0x8B];
        return content.IndexOf(ghostChar) >= 0;
    }
}
