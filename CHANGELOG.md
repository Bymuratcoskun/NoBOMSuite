# Değişiklik günlüğü

Bu proje [Semantic Versioning](https://semver.org/lang/tr/) kullanır.

## [0.1.0] — 2026-08-31

İlk yayın. Linux x64.

### Eklendi

- **BOM tespiti** — UTF-8 byte-order mark; `.sln` dosyaları muaf (Visual Studio
  bunları bilerek BOM'lu yazar, uyarmak yanlış alarmdır)
- **Görünmez karakter tespiti** — Zero-Width Space, Zero-Width Non-Joiner,
  Zero-Width Joiner, Word Joiner, Soft Hyphen; her biri adıyla ve **imleç
  konumuyla** bildirilir
- **Açılışta ve kaydettikçe tarama** — bulgular Sorunlar panelinde görünür
- **Çalışma alanı taraması** — tek komutla
- **Tek tıkla BOM kaldırma** — öncesi/sonrası bayt sayısı raporlanır
- Ayarlar: `devguard.etkin`, `devguard.kaydettesTara`, `devguard.taramaDeseni`,
  `devguard.haricDesen`

### Tasarım kararları

- **Varsayılan kapsam dar tutuldu.** Geniş kapsamlı bir deneme 22.665 dosyada
  364.000'den fazla eşleşme verdi; bulgular doğruydu ama bağlam yanlıştı —
  ZWNJ/ZWJ Farsça, Arapça ve Hint dillerinde meşru yazım karakterleridir.
  DevGuard bir **kod** hijyeni aracıdır; `data/` ve `datasets/` varsayılan
  olarak hariç tutulur.
- **Tarama çekirdeği native C**, C ABI üzerinden çağrılır — komut satırı
  aracıyla aynı motor, yeniden yazım değil.
- Eklentideki hayalet karakter listesi çekirdektekiyle **testle kilitlidir**
  (`npm run test:parite`): iki taraf aynı şeyi görmezse test düşer.

### Bilinen sınırlar

- Komut başlıkları Türkçe; İngilizce yerelleştirme planlanıyor
- Yalnız Linux x64 — Windows/macOS native derlemeleri henüz doğrulanmadı,
  bu yüzden **test edilmemiş ikili yayınlamak yerine** yayınlanmadı
- Depodaki Avalonia masaüstü uygulaması eklentiye dahil değildir ve bitmemiştir
