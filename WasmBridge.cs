using System;
using System.Runtime.InteropServices.JavaScript;
using SanitizerKit.Core.Scanners;

namespace SanitizerKit.Wasm;

public partial class WasmBridge
{
    [JSExport]
    public static bool ScanBom(byte[] content)
    {
        var scanner = new BomScanner();
        return scanner.HasIssue(content);
    }

    [JSExport]
    public static bool ScanGhostChars(byte[] content)
    {
        var scanner = new GhostCharScanner();
        return scanner.HasIssue(content);
    }
    
    [JSExport]
    public static bool ScanLineEndings(byte[] content)
    {
        var scanner = new LineEndingScanner();
        return scanner.HasIssue(content);
    }
    
    [JSExport]
    public static bool ScanNewline(byte[] content)
    {
        var scanner = new NewlineScanner();
        return scanner.HasIssue(content);
    }

    [JSExport]
    public static bool ScanTabs(byte[] content)
    {
        var scanner = new TabScanner();
        return scanner.HasIssue(content);
    }

    [JSExport]
    public static bool ScanHardcodedPasswords(byte[] content)
    {
        var scanner = new HardcodedPasswordScanner();
        return scanner.HasIssue(content);
    }
}