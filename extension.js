'use strict';

const vscode = require('vscode');
const cekirdek = require('./index.js');

/**
 * Hayalet karakterler — GhostCharScanner.cs'teki liste ile AYNI olmak ZORUNDA.
 * Çekirdek yalnız "var/yok" döner; imleci karakterin üstüne koyabilmek için
 * konumu burada buluyoruz. Bu ikilik bilinçli ve testle kilitli:
 * `npm run test:parite` iki tarafın aynı şeyi gördüğünü doğrular.
 */
const HAYALET = [
    { kod: '​', ad: 'Zero-Width Space' },
    { kod: '‌', ad: 'Zero-Width Non-Joiner' },
    { kod: '‍', ad: 'Zero-Width Joiner' },
    { kod: '⁠', ad: 'Word Joiner' },
    { kod: '­', ad: 'Soft Hyphen' }
];

/**
 * DevGuard bir KOD hijyeni aracıdır. Varsayılan kapsam bilinçli olarak dardır:
 * 2026-08-30'da bu depoda geniş kapsam 22.665 dosya tarayıp 384 veri dosyasında
 * (arXiv metinleri, Gutenberg kitapları) 364 binden fazla eşleşme buldu. Bulgular
 * DOĞRUYDU ama bağlam yanlıştı: ZWNJ/ZWJ Farsça, Arapça ve Hint dillerinde MEŞRU
 * yazım karakterleridir. Metin külliyatını taramak sinyali gürültüye boğar.
 */
const VARSAYILAN_DESEN = '**/*.{cs,js,jsx,ts,tsx,py,rs,c,h,cpp,hpp,fs,fsx,go,java,kt,rb,php,sh,json,yml,yaml,toml,xml,csproj,sln}';
const VARSAYILAN_HARIC = '**/{node_modules,bin,obj,target,dist,out,build,.git,.nobom,__pycache__,.venv,venv,vendor,coverage,data,datasets,.vscode-test}/**';

/**
 * BOM'un MEŞRU olduğu dosya türleri. Visual Studio `.sln` dosyalarını BOM ile
 * yazar ve bazı araçlar bunu bekler; burada uyarı vermek yanlış alarmdır.
 * 2026-08-30: bu depodaki tek "BOM bulgusu" SovereignNative.sln idi.
 */
const BOM_SERBEST = ['.sln'];

let tanilar;

function ayar(ad, varsayilan) {
    return vscode.workspace.getConfiguration('devguard').get(ad, varsayilan);
}

/** Belgeyi tarar ve Sorunlar paneline yazar. */
function belgeyiTara(belge) {
    if (!tanilar) return;
    if (belge.uri.scheme !== 'file') return;                  // çıktı/panel tamponlarını tarama
    if (!ayar('etkin', true)) { tanilar.delete(belge.uri); return; }

    const metin = belge.getText();
    const tampon = Buffer.from(metin, 'utf8');
    const bulgular = [];

    // 1) BOM — kararı ÇEKİRDEK verir (aynı .so, CLI ile birebir aynı mantık).
    //    VS Code metni BOM'suz verir; bu yüzden dosyanın kendisini okuyoruz.
    let diskTamponu = null;
    try { diskTamponu = require('fs').readFileSync(belge.uri.fsPath); } catch { /* kaydedilmemiş dosya */ }

    const uzanti = belge.uri.fsPath.slice(belge.uri.fsPath.lastIndexOf('.')).toLowerCase();
    const bomSerbest = ayar('bomSerbestUzantilar', BOM_SERBEST).includes(uzanti);

    if (diskTamponu && !bomSerbest && cekirdek.scanBom(diskTamponu)) {
        const t = new vscode.Diagnostic(
            new vscode.Range(0, 0, 0, 1),
            'Dosyanın başında UTF-8 BOM (EF BB BF) var. Bazı derleyiciler ilk satırı okuyamaz.',
            vscode.DiagnosticSeverity.Warning
        );
        t.source = 'DevGuard';
        t.code = 'bom';
        bulgular.push(t);
    }

    // 2) Hayalet karakterler — çekirdek "var mı" der, konumu burada buluruz.
    if (cekirdek.scanGhostChars(tampon)) {
        for (const { kod, ad } of HAYALET) {
            let i = metin.indexOf(kod);
            while (i !== -1) {
                const bas = belge.positionAt(i);
                const son = belge.positionAt(i + kod.length);
                const t = new vscode.Diagnostic(
                    new vscode.Range(bas, son),
                    `Görünmez karakter: ${ad} (U+${kod.codePointAt(0).toString(16).toUpperCase().padStart(4, '0')}). Gözle görünmez ama derleyici görür.`,
                    vscode.DiagnosticSeverity.Warning
                );
                t.source = 'DevGuard';
                t.code = 'hayalet-karakter';
                bulgular.push(t);
                i = metin.indexOf(kod, i + kod.length);
            }
        }
    }

    tanilar.set(belge.uri, bulgular);
    return bulgular;
}

/**
 * Komutun üzerinde çalışacağı belgeyi bulur.
 *
 * `activeTextEditor` yalnız odak GERÇEK bir metin düzenleyicideyken doludur;
 * kullanıcı Sorunlar panelinde ya da Gezgin'deyken bostur. 2026-08-30'da tam
 * bu yuzden "açık dosya yok" hatası alındı: dosya duruyordu, odak başkaydı.
 */
function hedefBelge(uri) {
    if (uri && uri.fsPath) return { uri };                      // sağ tık / Gezgin
    const etkin = vscode.window.activeTextEditor;
    if (etkin) return etkin.document;

    const gorunur = (vscode.window.visibleTextEditors || [])
        .filter((e) => e.document && e.document.uri.scheme === 'file');
    if (gorunur.length === 1) return gorunur[0].document;       // tek dosya açıksa o
    return null;
}

/** Etkin dosyadaki BOM'u diskten kaldırır. */
async function bomuKaldir(uri) {
    const belge = hedefBelge(uri);
    if (!belge) {
        vscode.window.showWarningMessage(
            'DevGuard: hedef dosya bulunamadı. Dosyanın sekmesine tıklayıp tekrar deneyin ' +
            '(ya da Gezgin\'de dosyaya sağ tıklayın).');
        return;
    }

    const yol = belge.uri.fsPath;
    const fs = require('fs');
    const once = fs.readFileSync(yol);

    if (!cekirdek.scanBom(once)) {
        vscode.window.showInformationMessage('DevGuard: bu dosyada BOM yok.');
        return;
    }

    const uz = yol.slice(yol.lastIndexOf('.')).toLowerCase();
    if (ayar('bomSerbestUzantilar', BOM_SERBEST).includes(uz)) {
        vscode.window.showWarningMessage(
            `DevGuard: ${uz} dosyalarında BOM beklenen bir durumdur (Visual Studio böyle yazar). ` +
            `Kaldırmak araçları bozabilir — dokunulmadı.`);
        return;
    }

    const sonra = cekirdek.removeBom(once);

    // KANIT ŞARTI: baytlar gerçekten değişmedikçe "kaldırıldı" DEME.
    if (sonra.length === once.length) {
        vscode.window.showErrorMessage('DevGuard: BOM kaldırılamadı (bayt sayısı değişmedi).');
        return;
    }

    fs.writeFileSync(yol, sonra);
    if (cekirdek.scanBom(fs.readFileSync(yol))) {
        vscode.window.showErrorMessage('DevGuard: yazıldı ama BOM hâlâ duruyor.');
        return;
    }

    if (belge.getText) belgeyiTara(belge);
    vscode.window.showInformationMessage(`DevGuard: BOM kaldırıldı (${once.length} → ${sonra.length} bayt).`);
}

/** Çalışma alanındaki dosyaları tarar. */
async function calismaAlaniniTara() {
    const desen = ayar('taramaDeseni', VARSAYILAN_DESEN);
    const haric = ayar('haricDesen', VARSAYILAN_HARIC);
    const tavan  = ayar('enFazlaSorunluDosya', 200);

    const dosyalar = await vscode.workspace.findFiles(desen, haric);
    let sorunlu = 0;
    let tavanaVuruldu = false;

    await vscode.window.withProgress(
        { location: vscode.ProgressLocation.Notification, title: 'DevGuard taraması', cancellable: true },
        async (ilerleme, iptal) => {
            for (let i = 0; i < dosyalar.length; i++) {
                if (iptal.isCancellationRequested) break;
                if (i % 25 === 0) ilerleme.report({ message: `${i}/${dosyalar.length}` });
                try {
                    const belge = await vscode.workspace.openTextDocument(dosyalar[i]);
                    const bulgular = belgeyiTara(belge);
                    if (bulgular && bulgular.length) sorunlu++;
                } catch { /* okunamayan dosyayı atla */ }

                // Tavan: 22.665 dosyalık bir veri havuzunda tarama, Sorunlar panelini
                // kullanılamaz hâle getirir (2026-08-30'da 1000'den fazla uyarı).
                if (sorunlu >= tavan) { tavanaVuruldu = true; break; }
            }
        }
    );

    if (tavanaVuruldu) {
        vscode.window.showWarningMessage(
            `DevGuard: ${tavan} sorunlu dosyada durduruldu — kapsam fazla geniş görünüyor. ` +
            `Veri/derleme dizinlerini "devguard.haricDesen" ile eleyin.`);
    } else {
        vscode.window.showInformationMessage(
            `DevGuard: ${dosyalar.length} dosya tarandı, ${sorunlu} dosyada sorun bulundu.`);
    }
}

/**
 * Belge kapandığında tanılamayı SİLME — dosya diskten silinmediyse bulgu hâlâ geçerli.
 *
 * Çalışma alanı taraması `openTextDocument` ile yüzlerce belgeyi bellekte açar;
 * VS Code bunları görünür olmadıkları için bir süre sonra kapatır ve
 * onDidCloseTextDocument tetiklenir. Eski hâlde tarama bulguları, kullanıcı
 * hiçbir şey yapmadan Sorunlar panelinden TEK TEK KAYBOLUYORDU.
 * Yalnız dosya gerçekten yoksa temizlenir.
 */
function belgeKapandi(belge) {
    if (!tanilar) return;
    try {
        if (!require('fs').existsSync(belge.uri.fsPath)) tanilar.delete(belge.uri);
    } catch {
        tanilar.delete(belge.uri);
    }
}

function activate(context) {
    tanilar = vscode.languages.createDiagnosticCollection('devguard');
    context.subscriptions.push(tanilar);

    context.subscriptions.push(
        vscode.commands.registerCommand('devguard.bomuKaldir', (uri) => bomuKaldir(uri)),
        vscode.commands.registerCommand('devguard.calismaAlaniniTara', calismaAlaniniTara),
        vscode.workspace.onDidOpenTextDocument(belgeyiTara),
        vscode.workspace.onDidSaveTextDocument((b) => { if (ayar('kaydettesTara', true)) belgeyiTara(b); }),
        vscode.workspace.onDidCloseTextDocument(belgeKapandi)
    );

    vscode.workspace.textDocuments.forEach(belgeyiTara);
}

function deactivate() {
    if (tanilar) tanilar.dispose();
}

// VS Code eklenti sözleşmesi + npm kütüphane yüzeyi AYNI dosyadan sunulur:
// package.json'daki "main" ikisi tarafından da okunuyor.
module.exports = {
    activate,
    deactivate,
    belgeyiTara,
    HAYALET,
    scanBom: cekirdek.scanBom,
    scanGhostChars: cekirdek.scanGhostChars,
    removeBom: cekirdek.removeBom
};
