import os
import shutil
import json
import argparse
from datetime import datetime
from pathlib import Path
import devguard_wrapper

# Taranması gereksiz olan, zaman kaybettirecek ve sistem dizinleri
IGNORED_DIRS = {".git", "node_modules", "bin", "obj", ".vs", "venv", "__pycache__", ".nobom"}

# Renkli Loglama Sistemi (Çok Formatlı Günlükleme)
class Colors:
    HEADER = '\033[95m'
    OKBLUE = '\033[94m'
    OKCYAN = '\033[96m'
    OKGREEN = '\033[92m'
    WARNING = '\033[93m'
    FAIL = '\033[91m'
    ENDC = '\033[0m'

def log_info(msg): print(f"{Colors.OKCYAN}[BİLGİ]{Colors.ENDC} {msg}")
def log_success(msg): print(f"{Colors.OKGREEN}[BAŞARILI]{Colors.ENDC} {msg}")
def log_warning(msg): print(f"{Colors.WARNING}[UYARI]{Colors.ENDC} {msg}")
def log_error(msg): print(f"{Colors.FAIL}[HATA]{Colors.ENDC} {msg}")
def log_bom(msg): print(f"{Colors.FAIL}[BOM]{Colors.ENDC} {msg}")
def log_step(msg): print(f"{Colors.HEADER}[*]{Colors.ENDC} {msg}")

def is_text_file(filepath: Path) -> bool:
    """Dosyanın metin (text) tabanlı olup olmadığını basitçe analiz eder."""
    try:
        with open(filepath, 'rb') as f:
            chunk = f.read(1024)
        # Eğer ilk 1KB içinde null-byte (\x00) varsa kuvvetle muhtemel binary dosyasıdır (resim, dll, vb.)
        if b'\x00' in chunk:
            return False
        return True
    except Exception:
        return False

def scan_project(directory_path: str, auto_clean: bool = False):
    root_path = Path(directory_path).resolve()
    if not root_path.exists() or not root_path.is_dir():
        log_error(f"Geçersiz dizin: {root_path}")
        return []

    log_step(f"Tarama başlatılıyor: {root_path}")
    bom_files = []
    scanned_count = 0

    for filepath in root_path.rglob("*"):
        # Eğer dosya yolu yoksayılmış bir klasörün içinden geçiyorsa atla
        if any(part in IGNORED_DIRS for part in filepath.parts):
            continue
        
        if filepath.is_file() and is_text_file(filepath):
            try:
                scanned_count += 1
                content = filepath.read_bytes()
                
                # C# wrapper'ımızı kullanarak hızlıca tarama yapıyoruz
                if devguard_wrapper.scan_bom(content):
                    bom_files.append(filepath)
            except Exception as e:
                log_warning(f"{filepath} okunamadı. Hata: {e}")

    print(f"\n{Colors.HEADER}--- Tarama Sonuçları ---{Colors.ENDC}")
    log_info(f"Taranan Dosya Sayısı: {scanned_count}")
    log_info(f"BOM Tespit Edilen Dosya Sayısı: {len(bom_files)}")
    
    for bom_file in bom_files:
        log_bom(f"{bom_file.relative_to(root_path)}")

    if auto_clean and bom_files:
        print()
        log_step("Temizleme ve Yedekleme İşlemi Başlatılıyor...")
        session_id = datetime.now().strftime("%Y%m%d_%H%M%S")
        backup_dir = root_path / ".nobom" / "backups" / session_id
        manifest = {}
        cleaned_count = 0

        for filepath in bom_files:
            try:
                # 1. Klasör yapısını koruyarak yedeği al
                rel_path = filepath.relative_to(root_path)
                backup_path = backup_dir / rel_path
                backup_path.parent.mkdir(parents=True, exist_ok=True)
                shutil.copy2(filepath, backup_path)
                manifest[str(filepath)] = str(backup_path)

                # 2. Native Wrapper ile BOM'u temizle ve dosyayı ez
                content = filepath.read_bytes()
                cleaned_content = devguard_wrapper.remove_bom(content)
                filepath.write_bytes(cleaned_content)
                
                cleaned_count += 1
                log_success(f"Temizlendi: {rel_path}")
            except Exception as e:
                log_error(f"{filepath} temizlenemedi: {e}")

        if manifest:
            manifest_path = backup_dir / "manifest.json"
            with open(manifest_path, "w", encoding="utf-8") as f:
                json.dump(manifest, f, indent=4, ensure_ascii=False)
            print()
            log_info(f"{cleaned_count} dosya temizlendi.")
            log_info(f"Yedekler ve geri alma haritası (manifest) şuraya kaydedildi:\n        {backup_dir}")

    return bom_files

def rollback_project(directory_path: str, session_id: str):
    root_path = Path(directory_path).resolve()
    backup_dir = root_path / ".nobom" / "backups" / session_id
    manifest_path = backup_dir / "manifest.json"

    log_step(f"Geri alma (Rollback) işlemi başlatılıyor: Seans {session_id}")

    if not manifest_path.exists():
        log_error(f"Manifest dosyası bulunamadı: {manifest_path}")
        return

    try:
        with open(manifest_path, "r", encoding="utf-8") as f:
            manifest = json.load(f)
        
        restored_count = 0
        for original_str, backup_str in manifest.items():
            original_path = Path(original_str)
            backup_path = Path(backup_str)

            if not backup_path.exists():
                log_warning(f"Yedek dosyası kayıp, atlanıyor: {backup_path}")
                continue

            try:
                shutil.copy2(backup_path, original_path)
                log_success(f"Geri yüklendi: {original_path.relative_to(root_path)}")
                restored_count += 1
            except Exception as e:
                log_error(f"Dosya geri yüklenemedi {original_path}: {e}")

        print()
        log_info(f"Geri alma tamamlandı. {restored_count} dosya orijinal haline döndürüldü.")
    except Exception as e:
        log_error(f"Rollback sırasında kritik hata: {e}")

if __name__ == "__main__":
    parser = argparse.ArgumentParser(description="NoBOMSuite - Dizin Tarayıcı ve Temizleyici")
    parser.add_argument("path", nargs="?", default=".", help="Taranacak hedef dizin (Varsayılan: .)")
    parser.add_argument("--clean", action="store_true", help="Tespit edilen BOM hatalarını güvenlik yedeği alarak otomatik temizle")
    parser.add_argument("--rollback", type=str, metavar="SEANS_ID", help="Belirtilen Seans ID'ye ait yedekleri geri yükler")
    
    args = parser.parse_args()
    
    if args.rollback:
        rollback_project(args.path, args.rollback)
    else:
        # Taramayı başlat (ve istenmişse temizle)
        found_bom_files = scan_project(args.path, auto_clean=args.clean)
    