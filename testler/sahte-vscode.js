'use strict';
// VS Code konak API'sinin testte gereken en küçük taklidi.
// Amaç: eklenti mantığını GERÇEKTEN koşturmak — "derleniyor" ile yetinmemek.

class Position { constructor(line, character) { this.line = line; this.character = character; } }
class Range { constructor(a, b, c, d) {
    if (a instanceof Position) { this.start = a; this.end = b; }
    else { this.start = new Position(a, b); this.end = new Position(c, d); }
} }
class Diagnostic { constructor(range, message, severity) { this.range = range; this.message = message; this.severity = severity; } }
class Uri { constructor(fsPath) { this.fsPath = fsPath; this.scheme = 'file'; this.toString = () => 'file://' + fsPath; } }

class Belge {
    constructor(fsPath, metin) { this.uri = new Uri(fsPath); this._metin = metin; }
    getText() { return this._metin; }
    positionAt(offset) {
        const once = this._metin.slice(0, offset);
        const satirlar = once.split('\n');
        return new Position(satirlar.length - 1, satirlar[satirlar.length - 1].length);
    }
}

const kayitliKomutlar = new Map();
let kapanisIsleyici = null;
const bildirimler = [];
let yapilandirma = {};

const koleksiyon = {
    _harita: new Map(),
    set(uri, tanilar) { this._harita.set(uri.toString(), tanilar); },
    delete(uri) { this._harita.delete(uri.toString()); },
    dispose() { this._harita.clear(); },
    get(uri) { return this._harita.get(uri.toString()) || []; }
};

const vscode = {
    Position, Range, Diagnostic, Uri,
    DiagnosticSeverity: { Error: 0, Warning: 1, Information: 2, Hint: 3 },
    ProgressLocation: { Notification: 15 },
    languages: { createDiagnosticCollection: () => koleksiyon },
    workspace: {
        textDocuments: [],
        getConfiguration: () => ({ get: (ad, vars) => (ad in yapilandirma ? yapilandirma[ad] : vars) }),
        onDidOpenTextDocument: () => ({ dispose() {} }),
        onDidSaveTextDocument: () => ({ dispose() {} }),
        onDidCloseTextDocument: (fn) => { kapanisIsleyici = fn; return { dispose() {} }; },
        findFiles: async () => [],
        openTextDocument: async (uri) => {
            const fs = require('fs');
            const yol = uri.fsPath || uri;
            // Gerçek VS Code metni BOM'suz verir; konağı buna sadık taklit et.
            return new Belge(yol, fs.readFileSync(yol, 'utf8').replace(/^\ufeff/, ''));
        }
    },
    window: {
        activeTextEditor: null,
        visibleTextEditors: [],
        showInformationMessage: (m) => { bildirimler.push(['bilgi', m]); },
        showWarningMessage:     (m) => { bildirimler.push(['uyarı', m]); },
        showErrorMessage:       (m) => { bildirimler.push(['hata', m]); },
        withProgress: async (_o, fn) => fn({ report() {} }, { isCancellationRequested: false })
    },
    commands: { registerCommand: (ad, fn) => { kayitliKomutlar.set(ad, fn); return { dispose() {} }; } }
};

module.exports = { vscode, Belge, kayitliKomutlar, bildirimler,
                   kapat: (b) => kapanisIsleyici && kapanisIsleyici(b),
                   koleksiyon, ayarla: (y) => { yapilandirma = y; } };
