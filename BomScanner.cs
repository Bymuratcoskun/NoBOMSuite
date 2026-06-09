using System;

namespace SanitizerKit.Core.Scanners;

public class BomScanner : IScanner
{
    public bool HasIssue(ReadOnlySpan<byte> content)
    {
        // UTF-8 BOM byte'ları: EF BB BF
        if (content.Length >= 3)
        {
            return content[0] == 0xEF && content[1] == 0xBB && content[2] == 0xBF;
        }
        return false;
    }
}
