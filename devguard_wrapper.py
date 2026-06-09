import ctypes
import os
import sys

# İşletim sistemine göre derlenmiş kütüphanenin (.dll, .so, .dylib) uzantısını bul
lib_ext = '.dll' if os.name == 'nt' else '.dylib' if sys.platform == 'darwin' else '.so'

# Çalışma dizininden bağımsız, dosyanın kendi konumunu baz alan güvenli yol oluşturma
base_dir = os.path.dirname(os.path.abspath(__file__))
lib_path = os.path.normpath(os.path.join(base_dir, f"../../SanitizerKit.Native/bin/Release/net10.0/native/SanitizerKit.Native{lib_ext}"))

try:
    devguard_lib = ctypes.CDLL(lib_path)
    # C-API argüman tiplerini belirle (Pointer, Int)
    devguard_lib.scan_bom.argtypes = [ctypes.c_char_p, ctypes.c_int]
    devguard_lib.scan_bom.restype = ctypes.c_uint8

    # C# tarafındaki BOM temizleme fonksiyonunu dahil et
    if hasattr(devguard_lib, 'remove_bom'):
        devguard_lib.remove_bom.argtypes = [ctypes.c_char_p, ctypes.c_int, ctypes.c_char_p]
        devguard_lib.remove_bom.restype = ctypes.c_int
except OSError as e:
    raise RuntimeError(f"[HATA] Native kütüphane bulunamadı veya yüklenemedi: {lib_path}\nLütfen 'dotnet publish' çalıştırın.\nDetay: {e}")

def scan_bom(content_bytes: bytes) -> bool:
    # Python byte dizisini (bytes) doğrudan C-API işaretçisine gönderiyoruz
    result = devguard_lib.scan_bom(content_bytes, len(content_bytes))
    return bool(result)

def remove_bom(content_bytes: bytes) -> bytes:
    """Native C# kütüphanesini kullanarak BOM karakterini temizler."""
    if not hasattr(devguard_lib, 'remove_bom'):
        raise NotImplementedError("Native kütüphanede 'remove_bom' fonksiyonu bulunamadı.")
        
    # Temizlenmiş veriyi tutmak için bellekte girdi boyutu kadar yer ayır
    out_buffer = ctypes.create_string_buffer(len(content_bytes))
    new_length = devguard_lib.remove_bom(content_bytes, len(content_bytes), out_buffer)
    
    # İşlem başarılıysa yeni uzunluğa göre tampondan veriyi al
    return out_buffer.raw[:new_length] if new_length >= 0 else content_bytes

if __name__ == "__main__":
    # Test verisi: En başa BOM karakteri enjekte edilmiş bir byte dizisi
    test_content = b"\xef\xbb\xbfMerhaba Dunya"
    has_bom = scan_bom(test_content)
    print(f"BOM Karakteri Tespit Edildi mi? -> {'Evet' if has_bom else 'Hayır'}")
    
    if has_bom:
        cleaned = remove_bom(test_content)
        print(f"Temizlenmiş içerik: {cleaned}")
