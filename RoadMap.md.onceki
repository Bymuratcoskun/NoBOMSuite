# NoBOMSuite (DevGuardSuite) - Çoklu AI Ajan Entegrasyonlu Nihai Yol Haritası

> 🎉 **GÜNCELLEME (v1.0.0 YAYINDA):** Aşağıdaki yol haritasında bulunan tüm fazlar ve hedefler **başarıyla tamamlanmıştır!** 
> NoBOMSuite artık açık kaynaklı, üretim ortamına (production) hazır ve topluluğun kullanımına açık bağımsız bir araçtır.

Bu dosya, projenin ilk satırından küresel bir ekosisteme dönüşmesine kadar izlenecek tüm adımları ve çoklu AI ajan mimarisini içeren resmi kılavuzdur.

---

## 🚀 FAZ 1: Laboratuvar Ortamı ve Çekirdek Motor (Core Engine)
- [x] **Adım 1.1: Bozuk Dosya Fabrikasını Kurmak (Test Fixtures)**
  - [x] C# ile test amaçlı küçük bir konsol uygulaması yaz.
  - [x] Klasör içine yapay olarak şu hatalı dosyaları enjekte eden kodu yaz: `test_bom.txt`, `test_crlf.sh`, `test_ghost.js`, `test_newline.py`.
- [x] **Adım 1.2: Çekirdek Kütüphaneyi Oluşturmak (`SanitizerKit.Core`)**
  - [x] Bellek optimizasyonu için `Span<byte>` yapılarını kullan.
  - [x] Modüler tarayıcı ajanlarını (`BomScanner`, `LineEndingScanner`, `GhostCharScanner`, `NewlineScanner`) kodla.

## 🛠️ FAZ 2: Evrensel Komut Satırı Aracı (CLI) & Güvenlik Ağları
- [x] **Adım 2.1: Akıllı Yedekleme ve Geri Alma (Backup & Rollback)**
  - [x] Değiştirilecek dosyaları saptayıp gizli `.nobom/backups/[Seans_ID]/` klasörüne yapılarıyla yedekle, `manifest.json` haritasını üret ve `Rollback()` fonksiyonunu yaz.
- [x] **Adım 2.2: Çok Formatlı Günlükleme (Logging System)**
  - [x] `INFO`, `WARNING`, `ERROR`, `DEBUG` günlük seviyelerini tanımla. Terminal için renkli çıktı modunu ve otomasyonlar için `--format json` parametresini kodla.
- [x] **Adım 2.3: Native AOT Derleme**
  - [x] .NET runtime gerektirmeyen tek bir bağımsız binary çıktısı için `.csproj` Native AOT ayarlarını tamamla.

## 💻 FAZ 3: Merkezi Yönetim Paneli ve Arka Plan Muhafızları (Masaüstü GUI)
- [x] **Adım 3.1: Avalonia UI ile Masaüstü Arayüzü Tasarımı**
  - [x] Sürükle-Bırak alanı, İnteraktif Kontrollü Mod ekranı ve Gerçek Zamanlı Akan Canlı Konsol tasarımlarını yap.
- [x] **Adım 3.2: Sistem Çekmecesi Ajanı (System Tray Daemon)**
  - [x] Uygulamayı sistem tepsisine küçülecek şekilde kurgula. `FileSystemWatcher` ile projeyi yerelde izle, `CTRL+S` anında bildirim (Toast) fırlat.
- [x] **Adım 3.3: Tek Tıkla Git Hook Entegrasyonu**
  - [x] Arayüze tek tıkla `.git/hooks/pre-commit` dosyasına bizim tetikleyicimizi yazan butonu ekle.

## 🔌 FAZ 4: Çift Katmanlı Koruma ve IDE Eklentileri
- [x] **Adım 4.1: Yerel Haberleşme Köprüsü (IPC - Named Pipes)**
  - [x] Masaüstü Uygulaması ile Editör Eklentisinin konuşması için `Named Pipes` altyapısını kur.
- [x] **Adım 4.2: Çapraz Sorgulama ve Çakışma Önleme (Race Condition)**
  - [x] Editör ve Merkez arasında dosya durumlarını çift taraflı doğrulayan kilit mekanizmasını yönet.
- [x] **Adım 4.3: Merkezi Dağıtım Sihirbazı (Deployment Wizard)**
  - [x] Masaüstü programından "VS Code Eklentisini Kur" dendiğinde otomatik kurulum otomasyonunu tamamla.
- [x] **Adım 4.4: Editör Teşhis (Diagnostics) Dinleyicisi**
  - [x] Editörün yerleşik analizörlerini dinleyerek unutulan noktalı virgül (`;`) veya parantez (`]`) hatalarını anlık yakala ve arayüze fırlat.

## 🌍 FAZ 5: Evrensel Ekosistem, Web ve Yama Fabrikası
- [x] **Adım 5.1: WebAssembly (Wasm) ile Tarayıcı Desteği (`vscode.dev`)**
  - [x] Çekirdek C# motorunu `Wasm` olarak derle ve `vscode.dev` Web Extension altyapısını kur.
- [x] **Adım 5.2: Tüm Programlama Dilleri İçin C-API ve Sarıcılar (Wrappers)**
  - [x] Native AOT ile kodu C-uyumlu `.so` / `.dll` olarak ihraç et. Python ve Node.js sarıcı paketlerini hazırla.
- [x] **Adım 5.3: Bağımsız Yama Fabrikası (Standalone Patch Generator)**
  - [x] Kullanıcının özel hata kurallarını bağımsız bir Python/Bash scriptine veya Native AOT ile 1MB'lık mini bir çalıştırılabilir dosyaya dönüştür.

## 📦 FAZ 6: Taşınabilir (Portable) Sürüm ve Son Dağıtım
- [x] **Adım 6.1: Akıllı Taşınabilir Sürüm Sihirbazı**
  - [x] Masaüstü uygulamasından ihraç edilen taşınabilir dosyanın yanına mevcut `.bomconfig` ayarlarını otomatik ekle.
- [x] **Adım 6.2: Büyük Gün**
  - [x] GitHub reposunu aç, lisansı belirle ve topluluk odaklı "Reçete Havuzu (Recipe Hub)" dökümantasyonunu yayınla.

## 🛡️ FAZ 7: Güvenlik Tahkimatı ve Gizlilik Güvencesi
- [x] **Adım 7.1: Tedarik Zinciri ve Kod Bütünlüğü Güvenliği**
  - [x] GitHub Actions CI/CD hatlarını kurarak kurulum paketlerinin tamamen izole bulut sunucularda derlenmesini sağla. SHA-256 üretimini otomatize et.
- [x] **Adım 7.2: %100 Çevrimdışı (Offline-First) ve Gizlilik İlkesi**
  - [x] Uygulamadan tüm analitik ve telemetri sistemlerini uzak tut; tüm kod analiz süreçlerinin yerelde dönmesini güvenceye al.
- [x] **Adım 7.3: Windows Antivirüs ve Yanlış Alarm Yönetimi**
  - [x] Nihai `.exe` çıktısını Microsoft Security Intelligence portalına bildirerek Windows Defender beyaz listesine (whitelist) aldır.

## 🤖 FAZ 8: Çoklu Yapay Zeka (AI) Ajan Mimarisi (Rol Ayrılıklı & Sıfır Maliyetli)
- [x] **Adım 8.1: API Güvenlik Duvarı ve Veri Maskeleme Modülü**
  - [x] Kullanıcının şifreli yerel API anahtarını yöneten katmanı kodla.
  - [x] Kod kesitleri buluta gönderilmeden önce çalışan yerel bir regex filtresi yaz; kodun içindeki özel veri, şifre ve anahtarları yerelde otomatik maskele (Örn: `db_pass = "[MASKED_BY_DEVGUARD]"`).
- [x] **Adım 8.2: Ajan 1 - Teşhis ve Genel Bilgilendirme Ajanı (The Diagnostics Agent)**
  - [x] Sadece hatanın teorik analizi ve dökümantasyon bilgisi üzerine özelleştirilmiş prompt mimarisini kur.
  - [x] Koda asla doğrudan dokunmamasını sağla; kullanıcıya hatanın kök nedenini genel bilgi olarak açıklamasını sağla.
- [x] **Adım 8.3: Ajan 2 - Çözüm, Öneri ve Kural Üretim Ajanı (The Fix & Recipe Agent)**
  - [x] Sadece kod üretimi, refaktör ve NoBOMSuite için özel yama/reçete üretimi üzerine odaklanmış prompt yapısını kurgula.
  - [x] Kullanıcıya interaktif olarak "Öneriyi Gör" veya "Kodu Güvenle Enjekte Et" seçeneklerini sunan arayüz bağını kur.
- [x] **Adım 8.4: Ajan 3 - Güvenlik ve Gizlilik Gardiyanı Ajanı (The Security Guard Agent)**
  - [x] Ajan 2'nin ürettiği kod çözümünü veya yamayı kullanıcıya göstermeden önce denetleyen bağımsız güvenlik kontrol ajanını tasarla.
  - [x] Çözüm kodunda güvenlik açığı, zararlı kod patenti veya halüsinasyon olup olmadığını denetle; "GÜVENLİ" onayı vermediği çözümleri arayüze bastırma.
