using System;
using System.Text;
using Xunit;
using SanitizerKit.Core.Scanners;

namespace NoBOMSuite.Tests;

public class ScannerTests
{
    [Fact]
    public void BomScanner_ShouldDetect_Utf8Bom()
    {
        var scanner = new BomScanner();
        byte[] data = { 0xEF, 0xBB, 0xBF, 0x61, 0x62, 0x63 }; // BOM + "abc"
        Assert.True(scanner.HasIssue(data));
    }

    [Fact]
    public void BomScanner_ShouldIgnore_CleanData()
    {
        var scanner = new BomScanner();
        byte[] data = { 0x61, 0x62, 0x63 }; // Sadece "abc"
        Assert.False(scanner.HasIssue(data));
    }

    [Fact]
    public void LineEndingScanner_ShouldDetect_CRLF()
    {
        var scanner = new LineEndingScanner();
        byte[] data = Encoding.UTF8.GetBytes("Line 1\r\nLine 2");
        Assert.True(scanner.HasIssue(data));
    }

    [Fact]
    public void GhostCharScanner_ShouldDetect_ZeroWidthSpace()
    {
        var scanner = new GhostCharScanner();
        byte[] data = { 0x61, 0xE2, 0x80, 0x8B, 0x62 }; // 'a' + ZWSP + 'b'
        Assert.True(scanner.HasIssue(data));
    }

    [Fact]
    public void NewlineScanner_ShouldDetect_MissingFinalNewline()
    {
        var scanner = new NewlineScanner();
        byte[] data = Encoding.UTF8.GetBytes("No newline at the end");
        Assert.True(scanner.HasIssue(data));
    }

    [Fact]
    public void TabScanner_ShouldDetect_Tabs()
    {
        var scanner = new TabScanner();
        // Tab karakteri içeren bir kod dizesi
        byte[] data = Encoding.UTF8.GetBytes("function test() {\n\treturn true;\n}");
        Assert.True(scanner.HasIssue(data));
    }

    [Fact]
    public void HardcodedPasswordScanner_ShouldDetect_Passwords()
    {
        var scanner = new HardcodedPasswordScanner();
        byte[] data = Encoding.UTF8.GetBytes("const db_pass = \"secret123\";");
        Assert.True(scanner.HasIssue(data));
    }
}