# 🍲 NoBOMSuite Reçete Havuzu (Recipe Hub)

🎉 **Topluluğa Hoş Geldiniz!**

NoBOMSuite (DevGuard), sadece yerleşik `BomScanner` veya `GhostCharScanner` modüllerine bağımlı kalmaz. Güçlü `PatchGenerator` altyapısı sayesinde geliştiriciler kendi **Özel Onarım Reçetelerini** oluşturabilir ve bu havuzda paylaşabilirler.

## 📌 Reçete Nasıl Eklenir?
Yeni bir reçete eklemek için bu dosyayı (Pull Request ile) güncelleyebilir veya arayüzdeki "Yama İhraç Et" (Patch Generator) butonunu kullanarak elde ettiğiniz `.py` / `.sh` yamalarını toplulukla paylaşabilirsiniz.

---

## 🌟 Popüler Topluluk Reçeteleri

### 1. Hardcoded Parola Temizleyici
Kodun içinde unutulmuş veritabanı şifrelerini bulur ve güvenli formata (`[MASKED]`) dönüştürür.
* **Regex Kuralı:** `password\s*=\s*'"['"]`
* **Değişim Hedefi:** `password = "[MASKED_BY_DEVGUARD]"`
* **Yazar:** @bymuratcoskun

### 2. Konsol Loglarını (console.log) Silme
Üretime (production) çıkmadan önce tüm gereksiz log mesajlarını koddan kazır.
* **Regex Kuralı:** `console\.log\((.*?)\);?`
* **Değişim Hedefi:** `/* log removed */`
* **Yazar:** @community

### 3. Eski Tip "var" Değişkenlerini "let/const" Yapma
Eski JavaScript projelerini modernize ederken `var` kullanımlarını `let` olarak günceller.
* **Regex Kuralı:** `\bvar\s+([a-zA-Z0-9_]+)\s*=`
* **Değişim Hedefi:** `let $1 =`
* **Yazar:** @devguard-team

---

## 🛠️ Reçeteleri Masaüstü Arayüze Ekleme
Yukarıdaki reçeteleri NoBOMSuite Kumanda Merkezi üzerinden veya taşınabilir `.bomconfig` dosyanıza şu şekilde ekleyebilirsiniz:

```json
"CustomRules": {
    "password\\s*=\\s*['\"](.*?)['\"]": "password = \"[MASKED_BY_DEVGUARD]\""
}
```
