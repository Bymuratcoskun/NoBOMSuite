# NoBOMSuite (DevGuard) — Yol Haritası

> ### ⚠️ 2026-08-30 ÖLÇÜM TURU
>
> Bu dosya daha önce **her maddeyi `[x]` işaretli** ve tepesinde *"tüm fazlar
> başarıyla tamamlanmıştır"* yazan bir sürümdeydi. 2026-08-30'da her madde tek
> tek ölçüldü: **kod okundu, derlendi, çalıştırıldı.** Sonuç: bazıları gerçekten
> bitmişti, bazıları hiç çalışmıyordu, bazıları da bugün bitirildi.
>
> Yapılmamış işin yapılmış görünmesi, bir projeyi geriye götüren en pahalı
> desendir. Bu yüzden artık her satırın yanında **nasıl doğrulandığı** yazıyor.
>
> **İşaretler**
> `[x]` ölçüldü, çalışıyor · `[~]` kısmen · `[ ]` yapılmadı ya da çalışmıyor
>
> Önceki sürüm: `RoadMap.md.onceki`

---

## 🚀 FAZ 1: Laboratuvar Ortamı ve Çekirdek Motor

- [ ] **1.1 Bozuk Dosya Fabrikası (Test Fixtures)**
  - `test_bom.txt` / `test_crlf.sh` / `test_ghost.js` / `test_newline.py` üreten
    kod **bulunamadı**. `TestFixtures.csproj` bugün xUnit test projesidir,
    bozuk-dosya üreteci değil. *(ölçüm: kaynak taraması)*
  - Not: testler bozuk içeriği kendi içinde üretiyor; ayrı fabrikaya ihtiyaç
    kalmamış olabilir — o zaman bu madde SİLİNMELİ, işaretlenmemeli.
- [x] **1.2 Çekirdek Kütüphane (`SanitizerKit.Core`)**
  - `Span<byte>` kullanımı ve dört tarayıcı yerinde.
  - 2026-08-30'da **üç tarayıcı daha** eklendi: `BidiScanner`,
    `ReplacementCharScanner`, `InvisibleWhitespaceScanner`; ayrıca ikili dosya
    tespiti (`IkiliTespit`) — bu olmadan tarama `.pyc` dosyalarında %35 yanlış
    alarm veriyordu. *(ölçüm: 52 test geçiyor)*

## 🛠️ FAZ 2: CLI ve Güvenlik Ağları

- [x] **2.1 Yedekleme ve Geri Alma** — `manifest.json` üretimi ve `Rollback()`
  kodda mevcut. *(ölçüm: kaynak; canlı geri-alma DENENMEDİ)*
- [x] **2.2 Çok Formatlı Günlükleme** — `INFO/WARNING/ERROR/DEBUG` ve
  `--format json` mevcut. *(ölçüm: kaynak + CLI testleri)*
- [~] **2.3 Native AOT Derleme**
  - C-API paylaşımlı kütüphane (`.so`) **bugün üretildi ve çalıştırıldı**.
  - CLI'nin tek-dosya AOT çıktısı bu turda doğrulanmadı.

## 💻 FAZ 3: Masaüstü Panel ve Arka Plan Muhafızları

- [~] **3.1 Masaüstü Arayüz** — Avalonia **DEĞİL**: Avalonia sökülmüş, arayüz
  **GTK4/GirCore**. Pencere açılıyor, sürükle-bırak 2026-08-29'da ilk kez
  çalıştı. *(ölçüm: operatör canlı denedi)*
- [~] **3.2 Sistem Çekmecesi Ajanı**
  - `FileSystemWatcher` var (`BackgroundWatcher.cs`). *(ölçüm: kaynak)*
  - **Tepsi simgesi ve bildirim (Toast) kodu YOK.** *(ölçüm: kaynak taraması)*
- [x] **3.3 Tek Tıkla Git Hook** — `.git/hooks/pre-commit` yazıcı mevcut
  (`MainWindow.cs:1151`). *(ölçüm: kaynak)*

## 🔌 FAZ 4: Çift Katmanlı Koruma ve IDE Eklentileri

- [x] **4.1 IPC (Named Pipes)** — `IpcServer.cs` / `IpcClient.cs` mevcut.
  *(ölçüm: kaynak; canlı iki-uçlu haberleşme DENENMEDİ)*
- [x] **4.2 Çakışma Önleme / Kilit** — `FileLockManager.cs` mevcut. *(kaynak)*
- [x] **4.3 VS Code Eklentisi** — 2026-08-30'da **gerçekten yapıldı**.
  - Manifesto sıfırdan yazıldı: `engines.vscode`, `contributes`,
    `activationEvents` — üçü de YOKTU, paket sıradan bir npm modülüydü.
  - Tanılamalar (BOM + görünmez karakter) Sorunlar paneline; iki komut;
    sağ tık menüleri; dört ayar.
  - VSIX paketlendi, **kuruldu ve operatör tarafından canlı denendi**.
  - ⚠️ "Masaüstü programından tek tuşla eklenti kur" sihirbazı HÂLÂ YOK;
    kurulum elle (`code --install-extension`).
- [ ] **4.4 Editör Teşhis Dinleyicisi** — eklenti `onDidChangeDiagnostics`
  dinlemiyor; editörün kendi analizörlerine bağlanmıyor. *(ölçüm: extension.js)*

## 🌍 FAZ 5: Evrensel Ekosistem

- [ ] **5.1 WebAssembly** — **derlenmiyor**: `NETSDK1147: wasm-tools iş yükü
  yüklü değil`. *(ölçüm: `dotnet build SanitizerKit.Wasm.csproj`)*
- [x] **5.2 C-API ve Sarıcılar** — 2026-08-30'da uçtan uca çalıştırıldı.
  - `.so` üretildi, üç sembol de ihraç edildi (`nm` ile doğrulandı).
  - 🔴 Bulunan kusur: `index.js` **ilk günden beri** `remove_bom` sembolünü
    çağırıyordu ama C-API'de karşılığı YOKTU — koffi eksik sembolde patlar,
    yani Node sarmalayıcısı hiçbir zaman çalışmamıştı. Köprü yazıldı.
  - *(ölçüm: Node'dan scanBom/scanGhostChars/removeBom canlı çağrıldı)*
- [~] **5.3 Yama Fabrikası** — `GeneratePythonPatch` / `GenerateBashPatch`
  mevcut. *(kaynak)* "1 MB'lık Native AOT mini çalıştırılabilir" kısmı
  doğrulanmadı.

## 📦 FAZ 6: Taşınabilir Sürüm ve Dağıtım

- [~] **6.1 Taşınabilir Sürüm Sihirbazı** — buton ve işleyici mevcut
  (`ExportPortable_Click`). *(kaynak; ÇALIŞTIRILMADI)*
- [~] **6.2 Büyük Gün**
  - GitHub deposu açık: `github.com/Bymuratcoskun/NoBOMSuite` ✅
  - `RECIPE_HUB.md` mevcut ✅
  - `LICENSE` (MIT) 2026-08-30'da eklendi — `package.json` "MIT" diyordu ama
    karşılığı olan dosya depoda yoktu. Telif sahibi satırı gözden geçirilmeli.
  - README rozeti `yourusername` yer tutucusundaydı; 2026-08-30'da düzeltildi.

## 🛡️ FAZ 7: Güvenlik ve Gizlilik

- [x] **7.1 CI/CD** — 2026-08-30'da ayağa kaldırıldı.
  - Hatlar depo **kökünde** duruyordu; `.github/workflows/` dizini yoktu, yani
    yazıldıkları günden beri **hiç koşmamışlardı**.
  - Taşımadan önce üç uyumsuzluk düzeltildi, yoksa kırmızı bir hat kurulacaktı:
    (1) `.NET 8.0.x` isteniyordu, projeler `net10.0`;
    (2) `NoBOMSuite.sln` diye bir dosya YOKTU — mevcut `NoBOMSuite.slnx`
        ise **bomboştu** (`<Solution></Solution>`), yani "derleme başarılı"
        hiçbir şey derlemediği için başarılıydı;
    (3) `SanitizerKit.CLI/SanitizerKit.CLI.csproj` gibi var olmayan alt-dizin
        yolları — projeler kökte.
  - Çözüme beş proje eklendi; `dotnet build` beşini de derliyor, `dotnet test`
    52 testi çözüm üzerinden koşuyor. YAML sözdizimi doğrulandı.
  - Wasm proje BİLEREK çözüm dışında (bkz. 5.1).
- [x] **7.2 %100 Çevrimdışı** — 2026-08-30'a kadar **YANLIŞ BİR İDDİAYDI**.
  - Dört AI ajanı da `strictOfflineMode: false` yazıp kullanıcının
    `StrictOfflineMode` ayarını (varsayılanı `true`) sessizce eziyordu.
  - Artık çevrimdışı kip açıkken bulut yolu hiç açılmıyor — tek bir istek bile
    kurulmuyor. Regresyon testiyle kilitli.
- [ ] **7.3 Windows Defender Beyaz Liste** — yapıldığına dair kanıt yok;
  Windows çıktısı bu ortamda üretilmiyor.

## 🤖 FAZ 8: Çoklu AI Ajan Mimarisi

- [x] **8.1 API Güvenlik Duvarı ve Maskeleme** — `LocalAiFirewall` şifreli
  anahtar + `[MASKED_BY_DEVGUARD]` maskeleme çalışıyor. *(testli)*
- [x] **8.2 Ajan 1 — Teşhis** — 2026-08-30'da **canlı kanıtlandı**.
  - Yeni `LocalOpenAI` kipiyle yerel 14B'ye bağlandı (`127.0.0.1:8090`),
    2 dk 16 sn'de gerçek kök-neden analizi döndürdü **ve rolüne uydu**
    (çözümü yazmadı, Ajan 2'ye devretti).
  - Aylarca "API anahtarı yok" diye ertelenen engel bağlayıcı değilmiş:
    eksik olan yalnızca lehçe farkıydı.
- [~] **8.3 Ajan 2 — Çözüm/Reçete** — kod ve prompt mimarisi mevcut, ortak
  taşıyıcıya bağlı; **canlı çalıştırılmadı**.
- [~] **8.4 Ajan 3 — Güvenlik Gardiyanı** — aynı durum; mock testi var, canlı
  koşu yok.

---

## 🎯 HEDEF: VS Code Marketplace'te yayın

Operatör kararı (2026-08-30): *"Bir ara onu komple elden geçirip Microsoft
markete gönderelim test etsinler. Açık kaynak kodlu projeydi."*

Eklenti zaten paketleniyor ve kuruluyor. Yayın için eksikler:

- [x] `LICENSE` dosyası (MIT) — eklendi
- [x] `repository` alanı — eklendi
- [ ] **Yayıncı hesabı** — Azure DevOps organizasyonu + Personal Access Token,
      sonra `vsce publish`
- [ ] **İkon** (128×128 PNG) ve `galleryBanner`
- [ ] **README'yi eklenti vitrini olarak yaz** — şu an masaüstü uygulamasını
      anlatıyor, eklentiyi değil; Marketplace sayfası bu dosyayı gösterir
- [ ] **CHANGELOG.md**
- [ ] Sürüm `0.1.0` → yayına uygun bir numara
- [ ] Çoklu platform: şu an VSIX yalnız `linux-x64` (koffi'nin native ikilisi
      ve bizim `.so` platforma bağlı). Windows/macOS için ayrı hedefler gerekir
      — ya da yayın ilk turda yalnız Linux olur.

## Sıradaki en değerli işler (ölçüme dayalı)

1. **Ajan 2 ve 3'ü canlı koştur** — altyapı hazır, yerel 14B bağlı; tek
   eksik gerçek bir çağrı ve rol uyumunun ölçülmesi.
4. **Tepsi simgesi + bildirim** — 3.2'nin eksik yarısı.
5. **Editör teşhis dinleyicisi (4.4)** — eklenti artık var, bağlanacak yer hazır.

## Doğrulanmamış olarak KALANLAR (kod var, çalıştırılmadı)

`Rollback()` · IPC canlı haberleşme · taşınabilir ihracat · yama fabrikası
mini-exe · CLI tek-dosya AOT. Bunlar `[x]` değil çünkü **kodun varlığı
çalıştığını kanıtlamaz** — bu projede tam bu varsayım yüzünden bir gün
kaybedildi (bkz. `remove_bom`: ilk günden beri çağrılıyordu, hiç yoktu).
