# 22 — İş Modeli, Fiyatlandırma, Birim Ekonomisi

---

## 1. Fiyatlandırma yapısı

| | **Free** | **Pro** | **Team** | **Enterprise** |
|---|---|---|---|---|
| **Fiyat** | $0 | **$19/ay** | **$39/kullanıcı-ay** | Özel (≥$1.500/ay) |
| Yıllık indirim | — | $190/yıl (2 ay bedava) | $390/kişi-yıl | görüşülür |

> ⚠️ **Bu tablo hedef modeli anlatıyor; UYGULANAN fiyat farklı.** Bugün kodda
> Pro **$15/ay** ($150/yıl) ve Team **$40/ay** ($400/yıl) — Team, kullanıcı
> başına değil **3 koltuklu tek fiyat** olarak satılıyor, çünkü koltuk başına
> faturalama henüz uygulanmadı. Tek fiyat kaynağı
> `Namines.Core/Analysis/PricingCatalog.cs`; gerekçeler
> [second-phase/17](../second-phase/17-ILK-TEMAS-VE-FIYAT.md)'de. Buradaki
> tablo, koltuk başına faturalama ve Enterprise geldiğinde ulaşılacak yeri
> tarif ediyor.
| **Kime** | Öğrenci, hobi, deneme | Solo geliştirici, freelancer | Ürün ekibi (3-30 kişi) | Kurumsal |

### Tasarım (Design Plane)
| Özellik | Free | Pro | Team | Ent |
|---|---|---|---|---|
| Proje | 3 | Sınırsız | Sınırsız | Sınırsız |
| Tablo/proje | 25 | Sınırsız | Sınırsız | Sınırsız |
| Tüm DDL motorları | ✔ | ✔ | ✔ | ✔ |
| Codegen hedefleri | 5 | Tümü | Tümü | Tümü + özel |
| AI çağrısı/ay | 20 | 500 | 2.000 (havuz) | Sınırsız / BYOK |
| Sürüm geçmişi | 7 gün | 90 gün | Sınırsız | Sınırsız |
| Şablon/blueprint | ✔ | ✔ | ✔ | + özel |

### Veri (Data Plane)
| Özellik | Free | Pro | Team | Ent |
|---|---|---|---|---|
| Ephemeral sandbox | 3/gün, 60 dk | 20/gün | Sınırsız | Sınırsız |
| Managed DB | ✖ | 1 × 0.5 GB | 5 × 10 GB | Özel |
| Branch DB | ✖ | 2 | 20 | Sınırsız |
| BYODB bağlantısı | 1 (salt-okunur) | 3 | 20 | Sınırsız |
| Yedek saklama | — | 7 gün | 30 gün | Özel |
| Bölge seçimi | ✖ | ✖ | ✔ | ✔ + TR |
| Namines Bridge (on-prem) | ✖ | ✖ | 1 | Sınırsız |

### Uygulama (App Plane)
| Özellik | Free | Pro | Team | Ent |
|---|---|---|---|---|
| **Console (admin panel)** | Salt-okunur, Namines markalı | ✔ tam CRUD | ✔ + RBAC | ✔ + self-host |
| Console kullanıcısı | 1 | 3 | 25 | Sınırsız |
| Özel alan adı | ✖ | `{slug}.namines.app` | Kendi alan adın | Kendi alan adın |
| Beyaz etiket | ✖ | ✖ | ✔ | ✔ |
| **Gateway API** | 10K istek/ay | 500K/ay | 5M/ay | Özel |
| GraphQL | ✖ | ✔ | ✔ | ✔ |
| Audit log | ✖ | 7 gün | 90 gün | Sınırsız + export |
| Console Eject | ✖ | ✔ | ✔ | ✔ |

### İşbirliği & Platform
| Özellik | Free | Pro | Team | Ent |
|---|---|---|---|---|
| Gerçek zamanlı işbirliği | 2 kişi | 2 kişi | Sınırsız | Sınırsız |
| Branch & merge | ✖ | ✔ | ✔ | ✔ |
| Namines Bot (GitHub) | ✖ | 1 repo | Sınırsız | Sınırsız |
| Yorumlar | ✖ | ✔ | ✔ | ✔ |
| SSO/SAML/SCIM | ✖ | ✖ | ✖ | ✔ |
| SLA | ✖ | ✖ | %99.9 | %99.95 + sözleşme |
| Destek | Topluluk | E-posta (48 sa) | Öncelikli (8 sa) | Özel + Slack |
| Self-hosted | ✖ | ✖ | ✖ | ✔ |

---

## 2. Fiyatlandırma stratejisi gerekçesi

| Karar | Gerekçe |
|---|---|
| **Free'de Console salt-okunur** | Değeri gösterir ama ekip kullanamaz → yükseltme baskısı doğru yerde |
| **Free'de managed DB yok** | En pahalı kaynak; ephemeral sandbox ile "dene" ihtiyacı zaten karşılanıyor |
| **Free'de AI 20 çağrı ama tam kalite** | Faz 1'in "kalitesiz modele düş" yaklaşımı kötü ilk izlenim üretiyordu ([09](09-AI-LAYER.md)) |
| **Pro proje bazlı değil, kullanıcı bazlı** | Solo geliştirici çok proje açar; proje başına ücret onu cezalandırır |
| **Team koltuk bazlı** | Console kullanıcıları ekipte büyür → doğal genişleme geliri |
| **$19 / $39** | dbdiagram ($9-14) üstünde, Supabase ($25) civarında, Retool ($50) altında — üç kat daha fazla iş yapıyoruz |
| **Yıllıkta 2 ay bedava** | Nakit akışı + churn azaltma |

---

## 3. Birim ekonomisi

### Kullanıcı başına aylık maliyet

| Kalem | Free | Pro | Team (kişi başı) |
|---|---|---|---|
| AI | $0.15 | $0.55 | $0.70 |
| Managed DB | $0 | $2.20 | $1.80 |
| Ephemeral sandbox | $0.08 | $0.15 | $0.20 |
| Gateway compute | $0.02 | $0.25 | $0.60 |
| Console hosting | $0.01 | $0.08 | $0.15 |
| Storage + transfer | $0.02 | $0.12 | $0.25 |
| Control plane payı | $0.05 | $0.10 | $0.15 |
| Destek (amorti) | $0 | $0.30 | $1.20 |
| Stripe komisyonu | $0 | $0.85 | $1.43 |
| **Toplam** | **$0.33** | **$4.60** | **$6.48** |
| **Gelir** | $0 | $19.00 | $39.00 |
| **Brüt marj** | −$0.33 | **$14.40 (%76)** | **$32.52 (%83)** |

### Ücretsiz kullanıcı ekonomisi
- Ücretsiz kullanıcı başına $0.33/ay
- %4 dönüşüm varsayımı → her ücretli kullanıcı 25 ücretsiz kullanıcıyı sübvanse ediyor: 25 × $0.33 = **$8.25**
- Pro'nun $14.40 brüt marjı bunu karşılıyor ama **marj $6.15'e düşüyor**
- **Sonuç:** Dönüşüm oranı %3'ün altına düşerse ücretsiz katman kısıtlanmalı. Bu, izlenmesi gereken en kritik iş metriği.

### LTV / CAC

| Metrik | Pro | Team |
|---|---|---|
| ARPU | $19 | $39 × ort. 6 koltuk = $234 |
| Aylık churn (hedef) | %5 | %2 |
| Ortalama ömür | 20 ay | 50 ay |
| Brüt marj | %76 | %83 |
| **LTV** | **$289** | **$9.711** |
| Hedef CAC | < $95 (LTV/CAC ≥ 3) | < $3.200 |
| Geri ödeme süresi | < 7 ay | < 14 ay |

---

## 4. Gelir projeksiyonu (muhafazakâr)

> **Varsayım uyarısı:** Bu rakamlar bir plandır, tahmin değil. Ürün-pazar uyumu doğrulanmadan hiçbiri gerçek değildir.

| Ay | Ücretsiz | Pro | Team org (ort. 6 koltuk) | MRR | Maliyet | Net |
|---|---|---|---|---|---|---|
| 3 | 300 | 5 | 0 | $95 | $150 | −$55 |
| 6 | 1.200 | 30 | 2 | $1.038 | $420 | +$618 |
| 9 | 3.000 | 85 | 8 | $3.487 | $900 | +$2.587 |
| 12 | 6.000 | 180 | 20 | $8.100 | $1.850 | +$6.250 |
| 18 | 15.000 | 420 | 55 | $20.850 | $4.200 | +$16.650 |
| 24 | 30.000 | 800 | 120 | $43.280 | $8.500 | +$34.780 |

**24. ay:** ~$520K ARR. Tek geliştirici için bu noktada 2-3 kişilik ekip finanse edilebilir.

**Kritik varsayımlar (bunlar yanlışsa tablo çöker):**
1. Ücretsiz → Pro dönüşümü %3
2. Pro aylık churn %5
3. Team org başına ortalama 6 koltuk
4. Aylık organik büyüme %25-35 (GTM'e bağlı, [23](23-GTM.md))

---

## 5. Aşırı kullanım ve kullanım bazlı kalemler

Plan limitleri aşıldığında **hizmet durmaz** — aşırı kullanım ücretlendirilir (opt-in):

| Kaynak | Dahil (Pro) | Aşırı kullanım |
|---|---|---|
| AI çağrısı | 500/ay | $0.03/çağrı |
| API isteği | 500K/ay | $1.50/100K |
| DB depolama | 0.5 GB | $0.30/GB-ay |
| Branch DB | 2 | $4/adet-ay |
| Console kullanıcısı | 3 | $6/kullanıcı-ay |
| Veri transferi | 10 GB | $0.10/GB |

Kullanıcı harcama limiti koyabilir (varsayılan: aşırı kullanım kapalı, limit dolunca uyarı).

---

## 6. Lisans stratejisi (Faz 1: MIT — yeniden değerlendirilmeli)

| Bileşen | Önerilen lisans | Gerekçe |
|---|---|---|
| **NSL spesifikasyonu + parser** (`Namines.Nsl`, `@namines/nsl`) | **MIT** | Standart olması için tamamen açık olmalı — DBML'in başarı formülü bu |
| **Compiler** (DDL/ORM backend'leri) | **Apache 2.0** | Topluluk katkısı çeker, patent koruması var |
| **CLI** | Apache 2.0 | Benimseme |
| **Console / Gateway / Control Plane** | **BSL 1.1** (4 yıl sonra Apache 2.0) | Rakip SaaS kurulmasını engeller, kaynak yine görünür — Sentry/HashiCorp modeli |
| **Bridge agent** | Apache 2.0 | Kurumsal güven için denetlenebilir olmalı |
| Şablonlar / blueprint'ler | MIT | |

**Neden bu ayrım:** Faz 1'in tamamen MIT olması, birinin kodu alıp aynı SaaS'ı kurmasını engellemiyor. Ama çekirdeği kapatmak da benimsemeyi öldürür. **Açık çekirdek + korumalı platform** doğru denge.

> ⚠️ Lisans değişikliği geriye dönük olmaz. Faz 1 kodu MIT olarak yayınlandı ve öyle kalacak. Yeni bileşenler için karar bu.

---

## 7. Dönüşüm mekanikleri

| Tetikleyici | Mesaj | Nereye götürür |
|---|---|---|
| 3. proje oluşturmaya çalışma | "Ücretsiz planda 3 proje. Pro ile sınırsız." | Pro |
| Managed DB isteme | "Kalıcı veritabanı Pro'da. Ephemeral ile şimdi dene." | Pro |
| Console'da kayıt eklemeye çalışma | "Free'de Console salt-okunur. Pro ile ekibin veriyi yönetsin." | **Pro (en güçlü tetikleyici)** |
| AI kredisi bitme | "20/20 kullanıldı. Pro'da 500. Veya kendi anahtarını bağla." | Pro veya BYOK |
| 3. kişiyi davet etme | "İşbirliği 2 kişiye kadar ücretsiz. Team ile sınırsız." | Team |
| 4. Console kullanıcısı | "Team'de 25 kullanıcı." | Team |
| Branch oluşturma | "Branch'ler Pro'da." | Pro |
| Özel alan adı isteme | "Kendi alan adın Team'de." | Team |
| SSO sorma | Satış görüşmesi | Enterprise |

**Kural:** Hiçbir zaman sert duvar değil — her zaman "şu an ne yapabilirsin" + "yükseltirsen ne olur". Kullanıcı hiçbir zaman verisine erişimini kaybetmez (downgrade'de bile salt-okunur erişim kalır).

---

## 8. Enterprise satış paketi

| Bileşen | Detay |
|---|---|
| Fiyat | $1.500-8.000/ay (koltuk + hacim) |
| Sözleşme | Yıllık, peşin |
| Zorunlu özellikler | SSO/SAML, SCIM, audit export, self-host seçeneği, Bridge, TR/EU veri ikametgâhı, DPA, SLA |
| Satış döngüsü | 2-4 ay |
| **Türkiye'ye özgü argümanlar** | KVKK uyumu, TR bölge, Türkçe destek, .NET/MSSQL derinliği, on-prem Bridge |
| Hedef segment | Banka/sigorta IT, holding, e-ticaret, yazılım evi, kamu tedarikçisi |

**Türkiye pazarı neden önemli:** Rakiplerin hiçbiri Türkçe konuşmuyor, KVKK'ya cevap vermiyor, .NET/SQL Server odaklı değil ve TR'de veri tutmuyor. Bu dört madde tek başına bir satış hendeği. Kurucunun burada olması avantaj, dezavantaj değil.

---

## 9. İzlenecek finansal metrikler

| Metrik | Hedef | Kırmızı çizgi |
|---|---|---|
| MRR büyümesi | %20/ay | < %8 |
| Ücretsiz → ücretli dönüşüm | %4 | < %2.5 |
| Net gelir retention | > %110 | < %95 |
| Brüt marj | > %78 | < %65 |
| AI maliyeti / gelir | < %6 | > %12 |
| Aylık churn (Pro) | < %5 | > %8 |
| CAC geri ödeme | < 7 ay | > 12 ay |
| Aktif kullanıcı başına maliyet | < $1.20 | > $2.50 |
| Nakit vadesi (runway) | > 12 ay | < 6 ay |
