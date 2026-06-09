using System;
using System.Text;
using Xunit;
using SanitizerKit.Core.Scanners;

namespace NoBOMSuite.Tests;

public class CoreScannersTests
{
    [Fact]
    public void BomScanner_Should_Detect_UTF8_BOM()
    {
        var scanner = new BomScanner();
        byte[] dataWithBom = new byte[] { 0xEF, 0xBB, 0xBF, 0x48, 0x65, 0x6C, 0x6C, 0x6F }; // BOM + "Hello"
        byte[] dataWithoutBom = Encoding.UTF8.GetBytes("Hello");

        Assert.True(scanner.HasIssue(dataWithBom));
        Assert.False(scanner.HasIssue(dataWithoutBom));
    }

    [Fact]
    public void LineEndingScanner_Should_Detect_CRLF()
    {
        var scanner = new LineEndingScanner();
        byte[] crlfData = Encoding.UTF8.GetBytes("Line1\r\nLine2"); // Windows tipi
        byte[] lfData = Encoding.UTF8.GetBytes("Line1\nLine2");     // POSIX (Linux/Mac) tipi

        Assert.True(scanner.HasIssue(crlfData));
        Assert.False(scanner.HasIssue(lfData));
    }

    [Fact]
    public void GhostCharScanner_Should_Detect_ZeroWidthSpace()
    {
        var scanner = new GhostCharScanner();
        byte[] cleanData = Encoding.UTF8.GetBytes("Clean code");
        byte[] ghostData = Encoding.UTF8.GetBytes("Ghost\u200Bcode"); // U+200B (Zero Width Space) enjekte edildi

        Assert.True(scanner.HasIssue(ghostData));
        Assert.False(scanner.HasIssue(cleanData));
    }
    
    [Fact]
    public void TabScanner_Should_Detect_Tabs()
    {
        var scanner = new TabScanner();
        byte[] cleanData = Encoding.UTF8.GetBytes("    Spaces only");
        byte[] tabData = Encoding.UTF8.GetBytes("\tTabbed text");

        Assert.True(scanner.HasIssue(tabData));
        Assert.False(scanner.HasIssue(cleanData));
    }
}