# 23 — Pazara Çıkış (GTM) & Büyüme

> Faz 1'in en büyük eksiği: **dağıtım kanalı yoktu.** Landing sayfası jenerik, SEO içeriği yok, paylaşılabilir artefakt yok, entegrasyon yok. İyi ürün + sıfır dağıtım = sıfır kullanıcı.

---

## 1. Konumlandırma ve mesaj

**H1 (landing):**
> **Change the schema. Everything else updates itself.**
> *Şemayı değiştir. Gerisi kendini günceller.*

**Alt başlık:**
> Namines veri modelinden canlı bir veritabanı, tam bir REST/GraphQL API ve kullanıma hazır bir yönetim paneli üretir — ve şema değiştikçe hepsini senkron tutar. İstediğin an kod olarak eject et.

**Üç faydaya bölünmüş mesaj:**

| Kime | Acı | Vaat |
|---|---|---|
| Solo geliştirici / indie hacker | "Backend kurmak 2 hafta alıyor, ben ürünü yapmak istiyorum" | 5 dakikada çalışan backend |
| Ürün ekibi | "Şema değişince DB, API, admin panel ve tipler 4 ayrı yerde elle güncelleniyor" | Tek kaynak, otomatik senkron |
| Kurumsal .NET ekibi | "Migration'ları üretimde uygulamaktan korkuyoruz" | Risk analizli, rollback'li, denetlenebilir migration |

---

## 2. Büyüme döngüleri

### Döngü 1: Paylaşılabilir şema sayfaları (viral)
```
Kullanıcı şema tasarlar
  → "Paylaş" → namines.com/s/{slug} (public, SEO'lu, güzel görünen sayfa)
  → Stack Overflow / Reddit / blog / dokümantasyonda paylaşılır
  → Ziyaretçi "Fork this schema" butonunu görür
  → Kayıt olur → kendi şemasını yapar → paylaşır
```
**dbdiagram'ın büyüme motoru tam olarak budur.** Faz 1'de share vardı ama salt-okunur ve SEO'suz — viral döngü yoktu.

Gerekli: OG image otomatik üretimi, `<meta>` etiketleri, embed iframe, "Fork" butonu, `namines.com/s/*` sitemap'te.

### Döngü 2: DBA rozeti (README viral)
```
[![Namines DBA Score](https://api.namines.com/v1/ai/dba/badge.svg?p=abc)](https://namines.com/s/abc)
```
GitHub README'lerinde görünür. Faz 1'de bu endpoint zaten vardı — **kullanılmıyordu**. Rozeti tanıt.

### Döngü 3: GitHub Bot (iş akışı yerleşmesi)
```
Bir geliştirici Namines Bot'u repo'ya kurar
  → Her PR'da bot yorumu görünür
  → Ekipteki diğer 5 geliştirici bot'u görür
  → "Bu ne?" → tıklar → kayıt olur
```
Team planının ana edinim kanalı. Her kurulum ortalama 4-8 kişiye görünür.

### Döngü 4: Eject edilen projeler
Eject edilen paketlerin README'sinde ve `namines.lock` dosyasında Namines referansı. Bu repo'lar GitHub'da görünür.

### Döngü 5: Blueprint Hub (içerik + SEO)
```
namines.com/hub/ecommerce-schema
namines.com/hub/multi-tenant-saas-schema
namines.com/hub/hospital-management-schema
```
Faz 1'deki 5 şablon → 100+ blueprint. Her biri bir SEO landing sayfası. Topluluk katkısı kabul edilir.

---

## 3. SEO stratejisi

### Programatik sayfalar (yüksek hacim)

| Sayfa deseni | Örnek | Tahmini adet |
|---|---|---|
| `/schema/{domain}` | "E-ticaret veritabanı şeması" | 200 |
| `/convert/{from}-to-{to}` | "MySQL to PostgreSQL schema converter" | 30 |
| `/tools/{tool}` | "Free online ERD diagram tool" | 15 |
| `/compare/{us}-vs-{them}` | "Namines vs dbdiagram" | 12 |
| `/orm/{orm}-schema-generator` | "Prisma schema generator" | 10 |
| `/hub/{blueprint}` | Blueprint sayfaları | 100+ |
| `/s/{slug}` | Kullanıcı şemaları | Sınırsız (UGC) |

### Hedef anahtar kelimeler

| Küme | Örnek | Rekabet |
|---|---|---|
| Yüksek niyet | "database schema generator ai", "auto generate admin panel from database" | Orta |
| Araç | "erd diagram tool online free", "sql schema visualizer" | Yüksek |
| Dönüştürme | "prisma schema generator", "dbml to sql" | Düşük ✅ |
| Problem | "how to safely drop a column in production postgres" | Düşük ✅ |
| .NET nişi | "ef core migration best practices", "sql server schema design tool" | **Düşük ✅** |
| Türkçe | "veritabanı şeması oluşturma", "sql şema tasarım aracı" | **Çok düşük ✅** |

### İçerik takvimi (haftada 2 yazı)

**Otorite kuran teknik yazılar (bunlar backlink çeker):**
1. "Why your ORM's default CASCADE is a production incident waiting to happen"
2. "Zero-downtime schema migrations in PostgreSQL: the complete guide"
3. "We ran 275 generated DDL scripts against 11 real database engines. Here's what broke."
4. "The hidden cost of `SELECT COUNT(*)` on large tables"
5. "SQL Server's multiple cascade paths error, explained"
6. "Benchmarking LLMs on database schema design" ← **eval verimizle, kimsede yok**
7. "Expand-contract: renaming a column without downtime"
8. "Row-Level Security: a practical guide for multi-tenant apps"

**Türkçe içerik (rekabetsiz):**
1. "Veritabanı tasarımında en sık yapılan 10 hata"
2. "KVKK uyumlu veritabanı şeması nasıl tasarlanır"
3. "EF Core migration'larını üretimde güvenle uygulama"

---

## 4. Lansman planı

| Faz | Ne zaman | Kanal | Hedef |
|---|---|---|---|
| **Alfa** | v2.0 hazır olunca | 30 davetli, Discord | Geri bildirim, kırılma noktaları |
| **Beta** | +4 hafta | Waitlist, Twitter/X, Türk geliştirici toplulukları | 500 kayıt |
| **Product Hunt** | +8 hafta | PH + HN + Reddit | Top 5, 1.500 kayıt |
| **Hacker News** | PH'den 1 hafta sonra | "Show HN: Namines — schema to live backend with admin UI" | Ön sayfa |
| **Dev.to / Hashnode** | Sürekli | Teknik yazılar | Organik |
| **YouTube** | Aylık | Demo + eğitim | Uzun kuyruk |

**Product Hunt hazırlığı:** 60 saniyelik demo videosu (fikirden çalışan admin panele), 6 ekran görüntüsü, kurucu hikâyesi, PH-özel 3 ay Pro indirimi, lansman günü 8 saat aktif yanıt.

**HN başlığı önemli:** "Show HN: I built X" değil, somut ve teknik ol. HN kitlesi vaadi değil, mühendisliği ödüllendirir — golden-file test yaklaşımı ve eval verisi HN'de iyi karşılanır.

---

## 5. Topluluk

| Kanal | Amaç | Kaynak |
|---|---|---|
| **Discord** | Destek, geri bildirim, blueprint paylaşımı | Günlük 30 dk |
| **GitHub Discussions** | Özellik istekleri, NSL spesifikasyon tartışması | Haftalık |
| **NSL spesifikasyonu açık** | Standart olma iddiası; katkı çeker | Aylık |
| **Twitter/X `@naminesdev`** | Build-in-public, changelog | Haftada 3 post |
| **LinkedIn (TR)** | Kurumsal erişim, Türkiye pazarı | Haftada 1 |
| **Türk geliştirici toplulukları** | Yerel benimseme | Aylık |
| **Aylık changelog** | Retention + "yaşayan ürün" sinyali | Aylık |

**Build in public:** MRR, kullanıcı sayısı ve öğrenilenleri açıkça paylaş. Bu, solo kurucular için kanıtlanmış bir dağıtım stratejisidir ve maliyeti sıfırdır.

---

## 6. Entegrasyon ve dağıtım ortaklıkları

| Ortak | Neden | Öncelik |
|---|---|---|
| **GitHub Marketplace** | Bot'un keşfedilmesi | P0 |
| **Vercel Integrations** | "Add Namines to your project" | P1 |
| **Neon / Supabase partner sayfası** | Onların kullanıcı tabanı | P1 |
| **VS Code eklentisi** | `.nsl` syntax + inline önizleme | P1 |
| **JetBrains eklentisi** | .NET/Java kitlesi | P2 |
| **npm `create-namines-app`** | `npx create-namines-app` → 60 sn'de proje | P1 |
| **Docker Hub resmi imaj** | Self-host keşfi | P2 |
| **Zapier / n8n / Make** | Console webhook'ları | P3 |

`npx create-namines-app` özellikle önemli: geliştiricinin ilk temas noktası terminal olur, kayıt gerektirmeden değer görür.

---

## 7. Onboarding (activation optimizasyonu)

**Hedef: kayıttan sonra 5 dakika içinde `console_row_created`.**

```
1. Giriş: "Ne inşa ediyorsun?" tek input   (kayıt İSTEMEDEN)
2. AI şemayı üretir, canvas'ta gösterir     (~15 sn)  ← ilk değer
3. "Bunu canlıya al" tek buton
4. Kayıt iste (burada, değer görüldükten SONRA)
5. Ephemeral DB provision + Console açılır  (~25 sn)
6. Console'da rehberli ilk kayıt ekleme     ← AHA MOMENTİ
7. "60 dakikan var — kalıcı yapmak için Pro"
```

**Kritik:** Kayıt duvarı 4. adımda, 1. adımda değil. Faz 1'de AI özellikleri auth gerektiriyordu — bu doğru bir güvenlik kararıydı ama activation'ı öldürür. Çözüm: ilk üretim anonim ve deterministik/ucuz model + katı IP rate limit ile yapılır.

**Boş durum sorunu:** Yeni Console'da veri yok → değersiz görünür. Çözüm: Data Factory ile otomatik 50 örnek satır seed'le, "bunlar örnek veri, temizle" butonu koy.

---

## 8. Fiyatlandırma sayfası mesajı

Üç sütun değil, **iki soru**:
1. "Sadece tasarlıyor musun?" → Free yeter
2. "Veriyi yönetmen ve ekibine açman gerekiyor mu?" → Pro / Team

Karşılaştırma tablosu altta, ROI hesaplayıcı üstte:
> *"Bir backend + admin panel geliştirmek ortalama 3 hafta sürüyor. Ortalama geliştirici maliyeti üzerinden bu ≈ X. Namines Pro yılda $190."*

---

## 9. İlk 90 gün — haftalık plan

| Hafta | Odak |
|---|---|
| 1-2 | Landing sayfası yeniden yazımı, waitlist, analitik kurulumu |
| 3-4 | 6 SEO yazısı, blueprint sayfaları, OG image altyapısı |
| 5-6 | Alfa (30 kullanıcı), geri bildirim döngüsü, onboarding iterasyonu |
| 7-8 | Beta açılışı, Discord, build-in-public başlangıcı |
| 9-10 | Demo videosu, Product Hunt varlıkları, basın kiti |
| 11 | **Product Hunt lansmanı** |
| 12 | **Hacker News**, sonuçları değerlendir |
| 13 | GitHub Marketplace, `create-namines-app` yayını |

---

## 10. Ne YAPMAMALI

| Yapma | Neden |
|---|---|
| Ücretli reklam (ilk 12 ay) | CAC bilinmiyor, bütçe yok, organik doymamış |
| Konferans sponsorluğu | Erken aşamada ROI yok |
| Enterprise satışı kovalamak (ilk 9 ay) | Ürün hazır değil, döngü uzun, dikkat dağıtır |
| Her özellik isteğini yapmak | Kapsam patlaması — Faz 1'in hatası |
| "AI-powered" diye pazarlamak | Emtia, farklılaştırmıyor. **Sonuç** pazarla: "çalışan backend" |
| Rakiplerle kavga etmek | dbdiagram'a saygı göster, ondan import et |
| Türkiye pazarını görmezden gelmek | En kolay ilk 100 kullanıcı orada, rekabet yok |
