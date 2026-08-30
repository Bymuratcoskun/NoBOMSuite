using System;

namespace SanitizerKit.Core.Scanners;

public class GhostCharScanner : IScanner
{
    // Tespit edilen görünmez/hayalet karakterler (UTF-8 byte dizileri):
    // ​ Zero-Width Space       : E2 80 8B  — kopyala-yapıştırda en sık bulaşan
    // ‌ Zero-Width Non-Joiner  : E2 80 8C
    // ‍ Zero-Width Joiner      : E2 80 8D
    // ⁠ Word Joiner            : E2 81 A0
    // ­ Soft Hyphen            : C2 AD     — satır sonu kararları için, kodda anlamsız
    private static ReadOnlySpan<byte> ZeroWidthSpace        => [0xE2, 0x80, 0x8B];
    private static ReadOnlySpan<byte> ZeroWidthNonJoiner    => [0xE2, 0x80, 0x8C];
    private static ReadOnlySpan<byte> ZeroWidthJoiner       => [0xE2, 0x80, 0x8D];
    private static ReadOnlySpan<byte> WordJoiner            => [0xE2, 0x81, 0xA0];
    private static ReadOnlySpan<byte> SoftHyphen            => [0xC2, 0xAD];

    public bool HasIssue(ReadOnlySpan<byte> content)
    {
        return content.IndexOf(ZeroWidthSpace)     >= 0
            || content.IndexOf(ZeroWidthNonJoiner) >= 0
            || content.IndexOf(ZeroWidthJoiner)    >= 0
            || content.IndexOf(WordJoiner)          >= 0
            || content.IndexOf(SoftHyphen)          >= 0;
    }
}
