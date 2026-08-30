'use strict';
const assert = require('assert');
const fs = require('fs');
const os = require('os');
const path = require('path');
const Module = require('module');

const sahte = require('./sahte-vscode.js');

// 'vscode' modülünü eklenti yüklenmeden ÖNCE devreye sok
const asilResolve = Module._resolveFilename;
Module._resolveFilename = function (istek, ...rest) {
    if (istek === 'vscode') return require.resolve('./sahte-vscode.js');
    return asilResolve.call(this, istek, ...rest);
};
const asilLoad = Module._load;
Module._load = function (istek, ...rest) {
    if (istek === 'vscode') return sahte.vscode;
    return asilLoad.call(this, istek, ...rest);
};

const eklenti = require('../extension.js');

let gecen = 0;
function test(ad, fn) {
    try { fn(); console.log(`  ✓ ${ad}`); gecen++; }
    catch (e) { console.error(`  ✗ ${ad}\n      ${e.message}`); process.exitCode = 1; }
}

const gecici = fs.mkdtempSync(path.join(os.tmpdir(), 'devguard-'));
const yaz = (ad, tampon) => { const y = path.join(gecici, ad); fs.writeFileSync(y, tampon); return y; };

console.log('\nDevGuard VS Code eklentisi — sahte konakta gerçek koşu\n');

const abonelikler = [];
eklenti.activate({ subscriptions: abonelikler });

test('activate komutları kaydediyor', () => {
    assert.ok(sahte.kayitliKomutlar.has('devguard.bomuKaldir'));
    assert.ok(sahte.kayitliKomutlar.has('devguard.calismaAlaniniTara'));
});

test('manifestodaki her komut gerçekten kayıtlı', () => {
    const pkg = require('../package.json');
    for (const k of pkg.contributes.commands) {
        assert.ok(sahte.kayitliKomutlar.has(k.command), `kayıtsız komut: ${k.command}`);
    }
});

test('BOM tanılaması üretiliyor', () => {
    const yol = yaz('bomlu.cs', Buffer.concat([Buffer.from([0xEF,0xBB,0xBF]), Buffer.from('int x = 1;\n')]));
    const bulgular = eklenti.belgeyiTara(new sahte.Belge(yol, 'int x = 1;\n'));
    assert.strictEqual(bulgular.length, 1);
    assert.strictEqual(bulgular[0].code, 'bom');
});

test('temiz dosya tanılama üretmiyor', () => {
    const yol = yaz('temiz.cs', Buffer.from('int x = 1;\n'));
    assert.strictEqual(eklenti.belgeyiTara(new sahte.Belge(yol, 'int x = 1;\n')).length, 0);
});

test('hayalet karakter DOĞRU satır/sütunda işaretleniyor', () => {
    const metin = 'satir bir\nint x​ = 1;\n';
    const yol = yaz('hayalet.cs', Buffer.from(metin));
    const bulgular = eklenti.belgeyiTara(new sahte.Belge(yol, metin));
    assert.strictEqual(bulgular.length, 1);
    assert.strictEqual(bulgular[0].code, 'hayalet-karakter');
    assert.strictEqual(bulgular[0].range.start.line, 1);       // ikinci satır
    assert.strictEqual(bulgular[0].range.start.character, 5);  // 'int x' sonrası
});

test('aynı satırdaki BİRDEN ÇOK hayalet karakterin hepsi bulunuyor', () => {
    const metin = 'a​b​c­d';
    const yol = yaz('cok.cs', Buffer.from(metin));
    assert.strictEqual(eklenti.belgeyiTara(new sahte.Belge(yol, metin)).length, 3);
});

test('PARİTE: extension.js listesindeki her karakteri çekirdek de görüyor', () => {
    for (const { kod, ad } of eklenti.HAYALET) {
        assert.ok(eklenti.scanGhostChars(Buffer.from(`x${kod}y`, 'utf8')),
                  `çekirdek görmüyor: ${ad}`);
        const metin = `x${kod}y`;
        const yol = yaz(`parite-${ad.replace(/\W/g,'')}.cs`, Buffer.from(metin));
        assert.strictEqual(eklenti.belgeyiTara(new sahte.Belge(yol, metin)).length, 1,
                           `eklenti konumlandıramıyor: ${ad}`);
    }
});

(async () => {
    const yol = yaz('kaldir.cs', Buffer.concat([Buffer.from([0xEF,0xBB,0xBF]), Buffer.from('int x = 1;\n')]));
    const belge = new sahte.Belge(yol, 'int x = 1;\n');
    sahte.vscode.window.activeTextEditor = { document: belge };

    const oncekiBoyut = fs.statSync(yol).size;
    await sahte.kayitliKomutlar.get('devguard.bomuKaldir')();
    const sonrakiBoyut = fs.statSync(yol).size;

    test('bomuKaldir: dosya 3 bayt küçüldü', () => {
        assert.strictEqual(oncekiBoyut - sonrakiBoyut, 3);
    });
    test('bomuKaldir: diskte BOM kalmadı', () => {
        assert.strictEqual(eklenti.scanBom(fs.readFileSync(yol)), false);
    });
    test('bomuKaldir: içerik korundu', () => {
        assert.strictEqual(fs.readFileSync(yol, 'utf8'), 'int x = 1;\n');
    });
    test('bomuKaldir: BOM\'suz dosyada "kaldırıldı" DEMİYOR', async () => {
        const t = yaz('zaten-temiz.cs', Buffer.from('int y = 2;\n'));
        sahte.vscode.window.activeTextEditor = { document: new sahte.Belge(t, 'int y = 2;\n') };
        sahte.bildirimler.length = 0;
        return sahte.kayitliKomutlar.get('devguard.bomuKaldir')().then(() => {
            assert.ok(sahte.bildirimler.some(([, m]) => m.includes('BOM yok')), JSON.stringify(sahte.bildirimler));
        });
    });

    await new Promise(r => setTimeout(r, 50));
    fs.rmSync(gecici, { recursive: true, force: true });
    console.log(`\n${gecen} test geçti${process.exitCode ? ' — BAŞARISIZ var' : ''}\n`);
})();
