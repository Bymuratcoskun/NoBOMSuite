using System;

namespace SanitizerKit.Core.Scanners;

public interface IScanner
{
    bool HasIssue(ReadOnlySpan<byte> content);
}
