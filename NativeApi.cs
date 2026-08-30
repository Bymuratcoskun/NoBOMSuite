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

    /// <summary>
    /// UTF-8 BOM'unu kaldırır ve sonucu <paramref name="output"/> tamponuna yazar.
    /// Dönüş: yazılan bayt sayısı, hata durumunda -1.
    ///
    /// index.js bu sembolü ilk günden beri çağırıyordu ama C-API'de KARŞILIĞI YOKTU:
    /// koffi eksik sembolde yüklenirken patlar, yani Node sarmalayıcısı hiç
    /// çalışamıyordu (2026-08-30'da tespit edildi).
    ///
    /// Çıkış tamponunun en az <paramref name="length"/> baytlık olması ÇAĞIRANIN
    /// sorumluluğudur — index.js bunu `Buffer.alloc(contentBuffer.length)` ile sağlar.
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "remove_bom")]
    public static int RemoveBom(IntPtr buffer, int length, IntPtr output)
    {
        if (buffer == IntPtr.Zero || output == IntPtr.Zero || length < 0) return -1;
        if (length == 0) return 0;

        unsafe
        {
            var girdi = new ReadOnlySpan<byte>(buffer.ToPointer(), length);
            int atlanan = new BomScanner().HasIssue(girdi) ? 3 : 0;
            var kalan = girdi[atlanan..];
            kalan.CopyTo(new Span<byte>(output.ToPointer(), length));
            return kalan.Length;
        }
    }
}
