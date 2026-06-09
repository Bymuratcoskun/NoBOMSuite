using System;
using System.Runtime.InteropServices;
using SanitizerKit.Core.Scanners;

namespace SanitizerKit.Native;

public static class NativeApi
{
    // Dış dünyadan (C, C++, Python, Node) "scan_bom" adıyla çağırılabilecek fonksiyon
    [UnmanagedCallersOnly(EntryPoint = "scan_bom")]
    public static byte ScanBom(IntPtr buffer, int length)
    {
        if (buffer == IntPtr.Zero || length <= 0) return 0;
        
        unsafe
        {
            // Gelen C-Pointer'ını C# tarafında Span'e çeviriyoruz (Kopya yok, RAM kullanımı yok)
            ReadOnlySpan<byte> span = new ReadOnlySpan<byte>(buffer.ToPointer(), length);
            var scanner = new BomScanner();
            return scanner.HasIssue(span) ? (byte)1 : (byte)0;
        }
    }

    // Görünmez karakterleri tarayan C-API köprüsü
    [UnmanagedCallersOnly(EntryPoint = "scan_ghost_chars")]
    public static byte ScanGhostChars(IntPtr buffer, int length)
    {
        if (buffer == IntPtr.Zero || length <= 0) return 0;
        
        unsafe
        {
            ReadOnlySpan<byte> span = new ReadOnlySpan<byte>(buffer.ToPointer(), length);
            var scanner = new GhostCharScanner();
            return scanner.HasIssue(span) ? (byte)1 : (byte)0;
        }
    }
}
