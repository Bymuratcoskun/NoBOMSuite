using System;

namespace SanitizerKit.Core.Scanners;

public class TabScanner : IScanner
{
    public bool HasIssue(ReadOnlySpan<byte> content)
    {
        // Tab (Sekme) karakteri ASCII 0x09'dur. Kod standartları gereği bunu tespit ediyoruz.
        return content.IndexOf((byte)0x09) >= 0;
    }
}