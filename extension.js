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

    if (diskTamponu && cekirdek.scanBom(diskTamponu)) {
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

/** Etkin dosyadaki BOM'u diskten kaldırır. */
async function bomuKaldir() {
    const editor = vscode.window.activeTextEditor;
    if (!editor) { vscode.window.showWarningMessage('DevGuard: açık bir dosya yok.'); return; }

    const yol = editor.document.uri.fsPath;
    const fs = require('fs');
    const once = fs.readFileSync(yol);

    if (!cekirdek.scanBom(once)) {
        vscode.window.showInformationMessage('DevGuard: bu dosyada BOM yok.');
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

    belgeyiTara(editor.document);
    vscode.window.showInformationMessage(`DevGuard: BOM kaldırıldı (${once.length} → ${sonra.length} bayt).`);
}

/** Çalışma alanındaki dosyaları tarar. */
async function calismaAlaniniTara() {
    const desen = ayar('taramaDeseni', '**/*.{cs,js,ts,py,rs,json,md,txt,c,h,cpp,fs,go,java,sh}');
    const haric = ayar('haricDesen', '**/{node_modules,bin,obj,target,.git,__pycache__,.venv,venv}/**');

    const dosyalar = await vscode.workspace.findFiles(desen, haric);
    let sorunlu = 0;

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
            }
        }
    );

    vscode.window.showInformationMessage(
        `DevGuard: ${dosyalar.length} dosya tarandı, ${sorunlu} dosyada sorun bulundu.`
    );
}

function activate(context) {
    tanilar = vscode.languages.createDiagnosticCollection('devguard');
    context.subscriptions.push(tanilar);

    context.subscriptions.push(
        vscode.commands.registerCommand('devguard.bomuKaldir', bomuKaldir),
        vscode.commands.registerCommand('devguard.calismaAlaniniTara', calismaAlaniniTara),
        vscode.workspace.onDidOpenTextDocument(belgeyiTara),
        vscode.workspace.onDidSaveTextDocument((b) => { if (ayar('kaydettesTara', true)) belgeyiTara(b); }),
        vscode.workspace.onDidCloseTextDocument((b) => tanilar.delete(b.uri))
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
