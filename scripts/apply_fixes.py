#!/usr/bin/env python3
from pathlib import Path
import shutil
import time
import sys

ROOT = Path(__file__).resolve().parents[1]
IGNORES = {".git", "bin", "obj", "node_modules", ".autofix_backup"}

# Place backups outside the repository to avoid recursion
backup_dir = ROOT.parent / f".autofix_backup_{int(time.time())}"
backup_dir.mkdir(parents=True, exist_ok=True)

def should_ignore(path: Path):
    for part in path.parts:
        if part in IGNORES:
            return True
    return False

def is_binary(data: bytes) -> bool:
    if b"\x00" in data:
        return True
    text_chars = b"\n\r\t\f\b" + bytes(range(0x20, 0x100))
    nontext = sum(1 for b in data if b not in text_chars)
    return (nontext / max(1, len(data))) > 0.30

fixed_files = []

for p in ROOT.rglob("*"):
    if not p.is_file() or should_ignore(p):
        continue
    try:
        data = p.read_bytes()
    except Exception:
        continue
    if is_binary(data):
        continue

    original = data
    changed = False
    # Remove UTF-8 BOM
    if data.startswith(b"\xef\xbb\xbf"):
        data = data[3:]
        changed = True
    # Normalize CRLF -> LF
    if b"\r\n" in data:
        data = data.replace(b"\r\n", b"\n")
        changed = True
    # Remove zero-width space U+200B
    if b"\xe2\x80\x8b" in data:
        data = data.replace(b"\xe2\x80\x8b", b"")
        changed = True
    # Remove other C0 control chars except \t, \n, \r
    new_bytes = bytearray()
    removed_ctrl = False
    for b in data:
        if b < 0x20 and b not in (0x09, 0x0A, 0x0D):
            removed_ctrl = True
            continue
        new_bytes.append(b)
    if removed_ctrl:
        data = bytes(new_bytes)
        changed = True
    # Ensure final newline
    if len(data) > 0 and not data.endswith(b"\n"):
        data = data + b"\n"
        changed = True

    if changed:
        # Backup original
        rel = p.relative_to(ROOT)
        dest = backup_dir / rel
        dest.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(p, dest)
        # Write fixed
        try:
            p.write_bytes(data)
            fixed_files.append(str(rel))
            print(f"Fixed: {rel}")
        except Exception as e:
            print(f"Failed to write {rel}: {e}")

# Summary
print("---")
print(f"Backups stored in: {backup_dir}")
print(f"Files fixed: {len(fixed_files)}")
for f in fixed_files[:200]:
    print(f" - {f}")

if len(fixed_files) == 0:
    print("Nothing to fix.")
else:
    print("Auto-fix complete.")
