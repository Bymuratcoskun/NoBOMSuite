const ffi = require('ffi-napi');
const path = require('path');
const os = require('os');

// İşletim sistemine göre kütüphane uzantısını belirle
const libExt = {
    'win32': '.dll',
    'darwin': '.dylib',
    'linux': '.so'
}[os.platform()];

if (!libExt) {
    throw new Error(`Desteklenmeyen platform: ${os.platform()}`);
}

// Derlenmiş native kütüphanenin yolu
// Bu yol, kütüphanenin 'native' alt klasörüne yerleştirildiğini varsayar.
const libPath = path.join(__dirname, 'native', `SanitizerKit.Native${libExt}`);

let devguard;
try {
    // C-API fonksiyon imzalarını tanımla
    devguard = ffi.Library(libPath, {
        'scan_bom': ['byte', ['char *', 'int']],
        'scan_ghost_chars': ['byte', ['char *', 'int']],
        'remove_bom': ['int', ['char *', 'int', 'char *']]
        // Diğer fonksiyonlar buraya eklenebilir
    });
} catch (e) {
    throw new Error(`Native kütüphane yüklenemedi: ${libPath}. Lütfen kütüphanenin doğru yolda olduğundan emin olun. Detay: ${e.message}`);
}

/**
 * Bir buffer'ı UTF-8 BOM için tarar.
 * @param {Buffer} contentBuffer Dosya içeriği.
 * @returns {boolean} BOM bulunursa true, aksi takdirde false.
 */
function scanBom(contentBuffer) {
    if (!Buffer.isBuffer(contentBuffer)) throw new TypeError('Girdi Buffer olmalıdır.');
    return devguard.scan_bom(contentBuffer, contentBuffer.length) === 1;
}

/**
 * Bir buffer'ı görünmez 'hayalet' karakterler için tarar.
 * @param {Buffer} contentBuffer Dosya içeriği.
 * @returns {boolean} Hayalet karakter bulunursa true, aksi takdirde false.
 */
function scanGhostChars(contentBuffer) {
    if (!Buffer.isBuffer(contentBuffer)) throw new TypeError('Girdi Buffer olmalıdır.');
    return devguard.scan_ghost_chars(contentBuffer, contentBuffer.length) === 1;
}

/**
 * Bir buffer'dan UTF-8 BOM'unu kaldırır.
 * @param {Buffer} contentBuffer Dosya içeriği.
 * @returns {Buffer} BOM'u kaldırılmış yeni bir buffer.
 */
function removeBom(contentBuffer) {
    if (!Buffer.isBuffer(contentBuffer)) throw new TypeError('Girdi Buffer olmalıdır.');
    
    const outBuffer = Buffer.alloc(contentBuffer.length);
    const newLength = devguard.remove_bom(contentBuffer, contentBuffer.length, outBuffer);
    
    if (newLength < 0) return contentBuffer; // Hata durumunda orijinali döndür
    
    return outBuffer.slice(0, newLength);
}

module.exports = {
    scanBom,
    scanGhostChars,
    removeBom
};