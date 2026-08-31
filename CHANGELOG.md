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

### Yayın öncesi hata analizinde bulunanlar (2026-08-31)

Üç kusur koşturularak arandı, ikisi gerçek çıktı:

- 🔴 **Kaydedilmemiş değişiklik varken diske yazılıyordu.** Komut dosyayı
  diskten okuyup diske yazıyor; editörde kaydedilmemiş değişiklik varsa iki hâl
  ayrışıyor ve kullanıcı Ctrl+S yaptığı anda tampon diski eziyor — **BOM geri
  geliyor**, ama "BOM kaldırıldı" bildirimi çoktan gösterilmiş oluyor. İşlem
  sessizce geri alınıyor ve başarılı görünüyordu. Artık dosyaya **dokunulmuyor**,
  kullanıcıya söyleniyor. (Kaydetmeyi onun adına biz yapmıyoruz.)
- 🟡 **Büyük dosyada editör bloke oluyordu.** BOM taraması senkron; 60 MB'lık
  dosyada okuma+tarama **224 ms** ölçüldü. Artık `devguard.enFazlaDosyaMB`
  (varsayılan 8) tavanı var — tavan üstü dosya taranmıyor ama **sessizce
  atlanmıyor**, Sorunlar panelinde bildiriliyor. Okuma hatası da (izin, ağ
  sürücüsü) aynı şekilde artık sessiz yutulmuyor.
- ⚪ **Uzantı hesabı** `slice(lastIndexOf('.'))` yanlıştı ("Makefile" → `"e"`).
  `path.extname`'e geçildi — ama **gözlemlenebilir arıza üretmediği mutasyonla
  kanıtlandı**: yanlış değer her zaman `/` içerdiği için muafiyet listesiyle
  asla eşleşmiyordu. Savunma amaçlı düzeltme; kusur diye sayılmadı.

### Bilinen sınırlar

- Komut başlıkları Türkçe; İngilizce yerelleştirme planlanıyor
- Yalnız Linux x64 — Windows/macOS native derlemeleri henüz doğrulanmadı,
  bu yüzden **test edilmemiş ikili yayınlamak yerine** yayınlanmadı
- Depodaki Avalonia masaüstü uygulaması eklentiye dahil değildir ve bitmemiştir
