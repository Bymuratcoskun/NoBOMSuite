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
// DİKKAT: async gövdeler BEKLENİR. Beklenmediğinde geç çözülen bir söz,
// sonraki testin kurduğu sahte durumu görüyordu (2026-08-30'da canlı görüldü:
// "BOM yok" testi, komşu testin BOM'lu dosyasını onardı sanıyordu).
async function test(ad, fn) {
    try { await fn(); console.log(`  ✓ ${ad}`); gecen++; }
    catch (e) { console.error(`  ✗ ${ad}\n      ${e.message}`); process.exitCode = 1; }
}

const gecici = fs.mkdtempSync(path.join(os.tmpdir(), 'devguard-'));
const yaz = (ad, tampon) => { const y = path.join(gecici, ad); fs.writeFileSync(y, tampon); return y; };

console.log('\nDevGuard VS Code eklentisi — sahte konakta gerçek koşu\n');

(async () => {
const abonelikler = [];
eklenti.activate({ subscriptions: abonelikler });

await test('activate komutları kaydediyor', () => {
    assert.ok(sahte.kayitliKomutlar.has('devguard.bomuKaldir'));
    assert.ok(sahte.kayitliKomutlar.has('devguard.calismaAlaniniTara'));
});

await test('manifestodaki her komut gerçekten kayıtlı', () => {
    const pkg = require('../package.json');
    for (const k of pkg.contributes.commands) {
        assert.ok(sahte.kayitliKomutlar.has(k.command), `kayıtsız komut: ${k.command}`);
    }
});

await test('BOM tanılaması üretiliyor', () => {
    const yol = yaz('bomlu.cs', Buffer.concat([Buffer.from([0xEF,0xBB,0xBF]), Buffer.from('int x = 1;\n')]));
    const bulgular = eklenti.belgeyiTara(new sahte.Belge(yol, 'int x = 1;\n'));
    assert.strictEqual(bulgular.length, 1);
    assert.strictEqual(bulgular[0].code, 'bom');
});

await test('temiz dosya tanılama üretmiyor', () => {
    const yol = yaz('temiz.cs', Buffer.from('int x = 1;\n'));
    assert.strictEqual(eklenti.belgeyiTara(new sahte.Belge(yol, 'int x = 1;\n')).length, 0);
});

await test('hayalet karakter DOĞRU satır/sütunda işaretleniyor', () => {
    const metin = 'satir bir\nint x​ = 1;\n';
    const yol = yaz('hayalet.cs', Buffer.from(metin));
    const bulgular = eklenti.belgeyiTara(new sahte.Belge(yol, metin));
    assert.strictEqual(bulgular.length, 1);
    assert.strictEqual(bulgular[0].code, 'hayalet-karakter');
    assert.strictEqual(bulgular[0].range.start.line, 1);       // ikinci satır
    assert.strictEqual(bulgular[0].range.start.character, 5);  // 'int x' sonrası
});

await test('aynı satırdaki BİRDEN ÇOK hayalet karakterin hepsi bulunuyor', () => {
    const metin = 'a​b​c­d';
    const yol = yaz('cok.cs', Buffer.from(metin));
    assert.strictEqual(eklenti.belgeyiTara(new sahte.Belge(yol, metin)).length, 3);
});

await test('PARİTE: extension.js listesindeki her karakteri çekirdek de görüyor', () => {
    for (const { kod, ad } of eklenti.HAYALET) {
        assert.ok(eklenti.scanGhostChars(Buffer.from(`x${kod}y`, 'utf8')),
                  `çekirdek görmüyor: ${ad}`);
        const metin = `x${kod}y`;
        const yol = yaz(`parite-${ad.replace(/\W/g,'')}.cs`, Buffer.from(metin));
        assert.strictEqual(eklenti.belgeyiTara(new sahte.Belge(yol, metin)).length, 1,
                           `eklenti konumlandıramıyor: ${ad}`);
    }
});


    const yol = yaz('kaldir.cs', Buffer.concat([Buffer.from([0xEF,0xBB,0xBF]), Buffer.from('int x = 1;\n')]));
    const belge = new sahte.Belge(yol, 'int x = 1;\n');
    sahte.vscode.window.activeTextEditor = { document: belge };

    const oncekiBoyut = fs.statSync(yol).size;
    await sahte.kayitliKomutlar.get('devguard.bomuKaldir')();
    const sonrakiBoyut = fs.statSync(yol).size;

    await test('bomuKaldir: dosya 3 bayt küçüldü', () => {
        assert.strictEqual(oncekiBoyut - sonrakiBoyut, 3);
    });
    await test('bomuKaldir: diskte BOM kalmadı', () => {
        assert.strictEqual(eklenti.scanBom(fs.readFileSync(yol)), false);
    });
    await test('bomuKaldir: içerik korundu', () => {
        assert.strictEqual(fs.readFileSync(yol, 'utf8'), 'int x = 1;\n');
    });
    await test('bomuKaldir: BOM\'suz dosyada "kaldırıldı" DEMİYOR', async () => {
        const t = yaz('zaten-temiz.cs', Buffer.from('int y = 2;\n'));
        sahte.vscode.window.activeTextEditor = { document: new sahte.Belge(t, 'int y = 2;\n') };
        sahte.bildirimler.length = 0;
        return sahte.kayitliKomutlar.get('devguard.bomuKaldir')().then(() => {
            assert.ok(sahte.bildirimler.some(([, m]) => m.includes('BOM yok')), JSON.stringify(sahte.bildirimler));
        });
    });


    // ── REGRESYON: 2026-08-30, operatör raporu ──────────────────────────────
    // "BOM'u kaldır dediğimde açık dosya yok uyarısı veriyor."
    // Sebep: odak Sorunlar panelindeyken activeTextEditor BOŞTUR.
    const r1 = yaz('regresyon-odak.cs', Buffer.concat([Buffer.from([0xEF,0xBB,0xBF]), Buffer.from('int z = 3;\n')]));

    sahte.vscode.window.activeTextEditor = null;                 // odak düzenleyicide DEĞİL
    sahte.vscode.window.visibleTextEditors = [{ document: new sahte.Belge(r1, 'int z = 3;\n') }];
    sahte.bildirimler.length = 0;
    await sahte.kayitliKomutlar.get('devguard.bomuKaldir')();

    await test('odak düzenleyicide değilken görünen dosyayı buluyor', () => {
        assert.strictEqual(eklenti.scanBom(fs.readFileSync(r1)), false,
            'BOM kaldırılmadı: ' + JSON.stringify(sahte.bildirimler));
    });

    // Gezgin'den sağ tık: komut doğrudan uri ile çağrılır
    const r2 = yaz('regresyon-uri.cs', Buffer.concat([Buffer.from([0xEF,0xBB,0xBF]), Buffer.from('int w = 4;\n')]));
    sahte.vscode.window.activeTextEditor = null;
    sahte.vscode.window.visibleTextEditors = [];
    sahte.bildirimler.length = 0;
    await sahte.kayitliKomutlar.get('devguard.bomuKaldir')(new sahte.vscode.Uri(r2));

    await test('Gezgin sağ tıkı (uri argümanı) çalışıyor', () => {
        assert.strictEqual(eklenti.scanBom(fs.readFileSync(r2)), false,
            'BOM kaldırılmadı: ' + JSON.stringify(sahte.bildirimler));
    });

    await test('hiçbir hedef yoksa yine de anlaşılır uyarı veriyor', async () => {
        sahte.vscode.window.activeTextEditor = null;
        sahte.vscode.window.visibleTextEditors = [];
        sahte.bildirimler.length = 0;
        return sahte.kayitliKomutlar.get('devguard.bomuKaldir')().then(() => {
            assert.ok(sahte.bildirimler.some(([tur]) => tur === 'uyarı'), 'uyarı yok');
        });
    });

    // ── REGRESYON: varsayılan kapsam veri gölünü taramamalı ────────────────
    await test('varsayılan hariç desen veri/derleme dizinlerini eliyor', () => {
        const pkg = require('../package.json');
        const h = pkg.contributes.configuration.properties['devguard.haricDesen'].default;
        for (const d of ['data', 'datasets', 'node_modules', 'target', 'obj', 'bin']) {
            assert.ok(h.includes(d), `hariç listede yok: ${d}`);
        }
    });

    await test('varsayılan tarama deseni külliyat uzantılarını ALMIYOR', () => {
        const pkg = require('../package.json');
        const d = pkg.contributes.configuration.properties['devguard.taramaDeseni'].default;
        for (const u of ['txt', 'md']) {
            assert.ok(!d.includes(u), `kod deseni doğal dil uzantısı içeriyor: ${u}`);
        }
        for (const u of ['cs', 'rs', 'py', 'js']) assert.ok(d.includes(u), `kod uzantısı eksik: ${u}`);
    });

    await test('manifestodaki menü komutları gerçekten kayıtlı', () => {
        const pkg = require('../package.json');
        for (const [yer, girisler] of Object.entries(pkg.contributes.menus)) {
            for (const g of girisler) {
                assert.ok(sahte.kayitliKomutlar.has(g.command), `${yer}: kayıtsız ${g.command}`);
            }
        }
    });


    // ── REGRESYON: .sln'de BOM MEŞRUDUR ────────────────────────────────────
    // Visual Studio .sln'i BOM ile yazar. 2026-08-30 taramasında bu depodaki
    // tek "BOM bulgusu" SovereignNative.sln idi — yani yanlış alarmdı.
    const slnIcerik = 'Microsoft Visual Studio Solution File, Format Version 12.00\n';
    const sln = yaz('Cozum.sln', Buffer.concat([Buffer.from([0xEF,0xBB,0xBF]), Buffer.from(slnIcerik)]));

    await test('.sln BOM\'u için uyarı ÜRETİLMİYOR', () => {
        assert.strictEqual(eklenti.belgeyiTara(new sahte.Belge(sln, slnIcerik)).length, 0);
    });

    await test('.sln BOM\'u kaldırılmaya ÇALIŞILMIYOR', async () => {
        const oncekiBoyut = fs.statSync(sln).size;
        sahte.vscode.window.activeTextEditor = { document: new sahte.Belge(sln, slnIcerik) };
        sahte.bildirimler.length = 0;
        return sahte.kayitliKomutlar.get('devguard.bomuKaldir')().then(() => {
            assert.strictEqual(fs.statSync(sln).size, oncekiBoyut, 'dosyaya DOKUNULDU');
            assert.ok(sahte.bildirimler.some(([, m]) => m.includes('beklenen bir durumdur')),
                      JSON.stringify(sahte.bildirimler));
        });
    });

    await test('.cs BOM\'u hâlâ uyarı üretiyor (kapı fazla açılmadı)', () => {
        const y = yaz('hala-uyari.cs', Buffer.concat([Buffer.from([0xEF,0xBB,0xBF]), Buffer.from('int q = 9;\n')]));
        assert.strictEqual(eklenti.belgeyiTara(new sahte.Belge(y, 'int q = 9;\n')).length, 1);
    });


    // ── REGRESYON: tarama bulguları kendiliğinden KAYBOLMAMALI ─────────────
    // Çalışma alanı taraması yüzlerce belgeyi bellekte açar; VS Code onları
    // sonradan kapatır. Eski hâlde kapanış tanılamayı siliyordu, yani tarama
    // sonuçları kullanıcı hiçbir şey yapmadan panelden düşüyordu.
    const kal = yaz('kapansa-da-kalsin.cs', Buffer.concat([Buffer.from([0xEF,0xBB,0xBF]), Buffer.from('int k = 5;\n')]));
    const kalBelge = new sahte.Belge(kal, 'int k = 5;\n');
    eklenti.belgeyiTara(kalBelge);

    await test('dosya diskte dururken kapanış tanılamayı SİLMİYOR', () => {
        assert.strictEqual(sahte.koleksiyon.get(kalBelge.uri).length, 1);
        sahte.kapat(kalBelge);
        assert.strictEqual(sahte.koleksiyon.get(kalBelge.uri).length, 1, 'tanılama kayboldu');
    });

    await test('dosya SİLİNMİŞSE kapanış tanılamayı temizliyor', () => {
        const gecici2 = yaz('silinecek.cs', Buffer.concat([Buffer.from([0xEF,0xBB,0xBF]), Buffer.from('int s = 6;\n')]));
        const b2 = new sahte.Belge(gecici2, 'int s = 6;\n');
        eklenti.belgeyiTara(b2);
        assert.strictEqual(sahte.koleksiyon.get(b2.uri).length, 1);
        fs.unlinkSync(gecici2);
        sahte.kapat(b2);
        assert.strictEqual(sahte.koleksiyon.get(b2.uri).length, 0, 'silinmiş dosyanın tanılaması kaldı');
    });


    // ── REGRESYON: onarimdan sonra panel TAZELENMELİ ──────────────────────
    // Operatör canlı gördü (2026-08-30): sağ tıkla BOM kaldırıldı, dosya
    // gerçekten 3 bayt küçüldü, ama Sorunlar panelinde uyarı DURMAYA devam
    // etti. Sebep: uri yolunda yeniden tarama atlanıyordu.
    const tz = yaz('tazele.cs', Buffer.concat([Buffer.from([0xEF,0xBB,0xBF]), Buffer.from('int t = 7;\u200b\n')]));
    const tzBelge = new sahte.Belge(tz, 'int t = 7;\u200b\n');
    eklenti.belgeyiTara(tzBelge);

    await test('onarım öncesi 2 uyarı (BOM + hayalet)', () => {
        assert.strictEqual(sahte.koleksiyon.get(tzBelge.uri).length, 2);
    });

    sahte.vscode.window.activeTextEditor = null;
    sahte.vscode.window.visibleTextEditors = [];
    await sahte.kayitliKomutlar.get('devguard.bomuKaldir')(new sahte.vscode.Uri(tz));

    await test('sağ tık onarımından sonra BOM uyarısı panelden DÜŞÜYOR', () => {
        const kalan = sahte.koleksiyon.get(tzBelge.uri);
        assert.strictEqual(kalan.length, 1, 'panel bayat kaldı: ' + JSON.stringify(kalan.map(k => k.code)));
        assert.strictEqual(kalan[0].code, 'hayalet-karakter', 'yanlış uyarı silindi');
    });

    await new Promise(r => setTimeout(r, 50));
    fs.rmSync(gecici, { recursive: true, force: true });
    console.log(`\n${gecen} test geçti${process.exitCode ? ' — BAŞARISIZ var' : ''}\n`);
})();
