const koffi = require('koffi');
const os = require('os');
const path = require('path');

const ext = os.platform() === 'win32' ? '.dll' : os.platform() === 'darwin' ? '.dylib' : '.so';
const libPath = path.resolve(__dirname, `../../SanitizerKit.Native/bin/Release/net10.0/native/SanitizerKit.Native${ext}`);

try {
    const lib = koffi.load(libPath);
    
    // C-API Fonksiyonlarını eşleştiriyoruz
    const scan_bom = lib.func('scan_bom', 'uint8', ['const uint8*', 'int']);

    function scanBom(buffer) {
        // Node.js Buffer'ını bellek pointer'ı olarak veriyoruz
        return scan_bom(buffer, buffer.length) === 1;
    }

    const testBuffer = Buffer.from([0xEF, 0xBB, 0xBF, 0x4D, 0x65, 0x72]); // BOM + "Mer"
    console.log("BOM Karakteri Tespit Edildi mi? ->", scanBom(testBuffer) ? "Evet" : "Hayır");
} catch (error) {
    console.error(`[HATA] Native kütüphane yüklenemedi: ${error.message}`);
}
