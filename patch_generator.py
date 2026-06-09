import os
import sys

STANDALONE_TEMPLATE = """#!/usr/bin/env python3
import os
import re
import sys
from pathlib import Path

# Otomatik Üretilmiş DevGuard Yaması
# Kural: {rule_name}

REGEX_PATTERN = r'{regex_pattern}'
REPLACE_TARGET = '{replace_target}'

def apply_patch(directory):
    count = 0
    pattern = re.compile(REGEX_PATTERN)
    for root, _, files in os.walk(directory):
        for file in files:
            filepath = Path(root) / file
            if file.endswith(('.py', '.js', '.ts', '.cs', '.txt', '.md', '.json', '.yml')):
                try:
                    content = filepath.read_text(encoding='utf-8')
                    if pattern.search(content):
                        new_content = pattern.sub(REPLACE_TARGET, content)
                        filepath.write_text(new_content, encoding='utf-8')
                        print(f"[YAMALANDI] {filepath}")
                        count += 1
                except Exception:
                    pass
    print(f"Toplam {count} dosya yamalandı.")

if __name__ == '__main__':
    target_dir = sys.argv[1] if len(sys.argv) > 1 else '.'
    apply_patch(target_dir)
"""

def generate_patch_script(rule_name: str, regex_pattern: str, replace_target: str, output_path: str):
    """Verilen regex kuralını bağımsız bir Python yamasına dönüştürür."""
    # Kaçış karakterlerini (escape chars) düzeltmek için ham metin formatına çevir
    script_content = STANDALONE_TEMPLATE.format(
        rule_name=rule_name,
        regex_pattern=regex_pattern.replace('\\', '\\\\').replace("'", "\\'"),
        replace_target=replace_target.replace('\\', '\\\\').replace("'", "\\'")
    )
    
    with open(output_path, "w", encoding="utf-8") as f:
        f.write(script_content)
    
    if os.name == 'posix':
        os.chmod(output_path, 0o755)
        
    print(f"[BAŞARILI] Bağımsız yama dosyası üretildi: {output_path}")