#!/usr/bin/env python3
import os
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
IGNORES = {".git", "bin", "obj", "node_modules"}

text_extensions = None

results = {
    'files_scanned': 0,
    'bom_files': [],
    'ghost_char_files': [],
    'mixed_line_endings': [],
    'no_final_newline': []
}

def should_ignore(path: Path):
    for part in path.parts:
        if part in IGNORES:
            return True
    return False

def is_binary(data: bytes) -> bool:
    # Simple heuristic: NUL byte or high ratio of non-text bytes
    if b"\x00" in data:
        return True
    text_chars = b"\n\r\t\f\b" + bytes(range(0x20, 0x100))
    nontext = sum(1 for b in data if b not in text_chars)
    return (nontext / max(1, len(data))) > 0.30

for p in ROOT.rglob("*"):
    if p.is_file() and not should_ignore(p):
        try:
            data = p.read_bytes()
        except Exception:
            continue
        results['files_scanned'] += 1
        if data.startswith(b"\xef\xbb\xbf") or data.startswith(b"\xff\xfe") or data.startswith(b"\xfe\xff"):
            results['bom_files'].append(str(p.relative_to(ROOT)))
        # ghost/control chars excluding common whitespace \t \n \r
        ghost = False
        for b in data:
            if b < 0x20 and b not in (0x09, 0x0A, 0x0D):
                ghost = True
                break
        if ghost:
            results['ghost_char_files'].append(str(p.relative_to(ROOT)))
        # line endings
        has_crlf = b"\r\n" in data
        has_lf = b"\n" in data
        if has_crlf and has_lf and not (has_crlf and not has_lf):
            # mixed if both CRLF and lone LFs
            # refine: mixed if CRLF exists and there are LFs not as part of CRLF
            if b"\r\n" in data:
                # remove CRLF and see if remaining contains LF
                without_crlf = data.replace(b"\r\n", b"")
                if b"\n" in without_crlf:
                    results['mixed_line_endings'].append(str(p.relative_to(ROOT)))
        # final newline
        if len(data) > 0 and not data.endswith(b"\n"):
            results['no_final_newline'].append(str(p.relative_to(ROOT)))

# Print summary
print(f"Tarama tamamlandı. Dosya sayısı: {results['files_scanned']}")
print()

def pr(title, lst):
    print(f"{title}: {len(lst)}")
    for i, f in enumerate(lst[:50], 1):
        print(f"  {i}. {f}")
    if len(lst) > 50:
        print(f"  ... ve {len(lst)-50} daha")
    print()

pr('BOM bulunan dosyalar', results['bom_files'])
pr('Hayalet / kontrol karakteri bulunan dosyalar', results['ghost_char_files'])
pr('Karışık satır sonu olan dosyalar', results['mixed_line_endings'])
pr('Sonunda yeni satır olmayan dosyalar', results['no_final_newline'])

# Save detailed report
out = ROOT / 'scan_report.txt'
with out.open('w', encoding='utf-8') as fh:
    fh.write(f"Tarama tamamlandı. Dosya sayısı: {results['files_scanned']}\n\n")
    for k, v in results.items():
        if k == 'files_scanned':
            continue
        fh.write(f"{k}: {len(v)}\n")
        for item in v:
            fh.write(f" - {item}\n")
        fh.write('\n')

print(f"Detaylı rapor kaydedildi: {out}")
