# 16 — Rakip analizi

> ## ✅ VERİ TOPLANDI (2026-09-09)
>
> İlk denetimde `WebSearch` / `WebFetch` araçları erişilemeyen bir modele bağlı
> olduğu için çalışmamıştı ve bu rapor **boş bırakılmıştı** — uydurulmuş bir
> rakip tablosu yazmaktansa. Veri sonradan **tarayıcı paneli** ile resmî fiyat
> sayfalarından toplandı.
>
> **Kaynaklar ve erişim tarihi (2026-09-09):**
> - [bytebase.com/pricing](https://www.bytebase.com/pricing/)
> - [atlasgo.io/pricing](https://atlasgo.io/pricing)
> - [supabase.com/pricing](https://supabase.com/pricing)
>
> Aşağıdaki her fiyat ve özellik iddiası bu sayfalardan **birebir okundu**.
> Okunmayan hiçbir şey yazılmadı.

---

## Baş rakip: Bytebase

Namines'in konumlandığı kategorinin (veritabanı değişiklik yönetişimi) olgun
oyuncusu. **Beklediğimizden çok daha yakın bir rakip.**

### Planlar

| Plan | Fiyat | Sınırlar |
|---|---|---|
| **Community** | **Ücretsiz, süresiz** | 20 kullanıcıya kadar, 10 veritabanı instance'ı |
| **Pro** | **$20 / kullanıcı / ay** | Sınırsız kullanıcı, 10 instance |
| **Enterprise** | Fiyat sorulacak | Özel instance sayısı |

Sertifikalar: **SOC 2 Type 2 ve HIPAA**. Air-gapped kurulum destekleniyor.

### ÜCRETSİZ planda (Community) neler var — dikkat

Bu liste, Namines'in bazı "farklılaştırıcı" saydığı şeylerin aslında rakibin
**bedava** katmanında olduğunu gösteriyor:

| Özellik | Namines'te |
|---|---|
| Git tabanlı şema sürüm kontrolü | ✅ (branch) |
| Declarative şema migration | ✅ |
| Şema karşılaştırma ve senkronizasyon | ✅ |
| **Dağıtım öncesi SQL incelemesi — 200+ kural** | 🟡 Risk sınıflandırması var, kural sayısı bu ölçekte değil |
| **Otomatik yedek + tek tıkla geri alma** | ✅ Vault |
| Çoklu veritabanında toplu değişiklik | ❌ |
| Rollout politikası (kademeli, zamanlanmış) | ❌ |
| Veritabanı changelog'u | ✅ |
| Web tabanlı SQL editörü | ✅ Desk |
| **Sorgu geçmişi ve kaydedilmiş betikler** | ❌ **Yok** |
| **Veri dışa aktarma** | ❌ **Yok** |
| **MCP sunucusu** | ✅ — **ama artık farklılaştırıcı değil** |
| IAM (kullanıcı, rol, izin) | ✅ |
| Terraform sağlayıcısı (policy as code) | ❌ |
| AI yardımı | ✅ |

### Enterprise'a SAKLANAN özellikler — Namines'in ticari açığı burada

| Bytebase'de yalnızca Enterprise | Namines'te |
|---|---|
| **Onay iş akışı (Approval Workflow)** | ✅ **Temel üründe** |
| **Risk değerlendirmesi (Risk Assessment)** | ✅ **Temel üründe** |
| Dinamik veri maskeleme | 🟡 Kısmî (Gateway maskeleme) |
| Just-in-Time erişim | ❌ |
| Veri sınıflandırma | ❌ |
| Özel roller | 🟡 5 sabit rol |
| SCIM / LDAP / 2FA | ❌ |
| Denetim kaydı — sınırsız saklama | 🟡 Pro'da 7 gün, Namines'te sınırsız |

**Bu tablo, denetimin bulduğu en net ticari fırsat.** Bytebase, onay akışını ve
risk değerlendirmesini "fiyat sorulacak" katmanına koymuş. Namines aynı iki
yeteneği temel üründe sunuyor.

---

## Atlas (Ariga)

Şema-as-code yaklaşımı; CLI ve CI/CD odaklı.

| Plan | Fiyat | Not |
|---|---|---|
| **Starter** | Ücretsiz | Yalnızca MySQL, MariaDB, PostgreSQL, SQLite; sınırlı inceleme/diff |
| **Pro (Team)** | **$9 / geliştirici / ay** + CI/CD ve veritabanı kullanım ücreti | 50 koltuk sınırı |
| **Enterprise** | Fiyat sorulacak | 20 veritabanından başlıyor |

**Güçlü yanları:** migration linting, drift tespiti, **CVE taraması ve güvenlik
grafiği**, kolon seviyesinde köken (lineage) grafiği, ORM entegrasyonları
(Go, Python, JS, Java, C#), Kubernetes operatörü, Terraform sağlayıcısı.

**Namines'e göre:** Atlas bir **geliştirici aracı**, ürün değil. ERD
görselleştirmesi var ama **düzenlenebilir bir tasarım tuvali yok**. Onay akışı
ve ekip yönetişimi Atlas'ın odağı değil.

**Fiyat çıpası dikkat çekici:** $9/geliştirici/ay, Bytebase Pro'nun yarısından az.

---

## Supabase

**Namines'in Ground'uyla karşılaştırılan taraf** — ve karşılaştırma Ground'un
aleyhine.

| Plan | Fiyat | Ne veriyor |
|---|---|---|
| **Free** | $0 | 500 MB veritabanı, 2 aktif proje, 1 hafta hareketsizlikte **duraklatılıyor** |
| **Pro** | **$25 / ay**'dan | 8 GB disk, **günlük yedek (7 gün saklama)**, e-posta desteği |
| **Team** | **$599 / ay**'dan | SOC2 & ISO 27001, SSO, 14 gün yedek saklama |
| **Enterprise** | Özel | SLA, PrivateLink, 7/24 destek |

Ayrıca kullanım bazlı compute fiyatlaması ($10 Micro → $410 2XL).

**Çıkarım:** Supabase bir **yönetişim** ürünü değil, bir **altyapı** ürünü.
Yedekleme sunuyor ama onay akışı, risk sınıflandırması ya da change request
kavramı yok.

Bu, `19-remove-improve-features.md`'deki **RW-02** kararını destekliyor:
Ground'la Supabase'e karşı altyapı yarışına girmek yanlış. Supabase'in Free
planı bile (500 MB, 2 proje) Ground'un v1 limitlerinin üstünde ve arkasında
yıllarca altyapı yatırımı var.

**Doğru hamle:** Supabase'e **bağlan**, onunla yarışma.

---

## Konumlandırmaya etkisi — dürüst değerlendirme

### Geri çekilmesi gereken iddialar

| Önceki iddia | Gerçek |
|---|---|
| "MCP sunucusu erken giriş / farklılaştırıcı" | ❌ Bytebase **Community'de** (bedava) MCP sunucusu veriyor |
| "Sorgu geçmişi eksikliği küçük bir boşluk" | ❌ Rakibin **ücretsiz** katmanında var — temel beklenti |
| "Yedekleme farklılaştırıcı" | 🟡 Bytebase Community'de "otomatik yedek + tek tıkla geri alma" var. Namines'in farkı yedeğin **kanıtlanması** — ama bu, doğrulanması gereken bir fark |

### Ayakta kalan gerçek farklar

| Fark | Kanıt |
|---|---|
| **Onay akışı + risk değerlendirmesi temel üründe** | Bytebase bunları Enterprise'a saklıyor |
| **Düzenlenebilir görsel şema tuvali** | Bytebase'in fiyat sayfasında yok; Atlas yalnızca "ERD visualization" (görüntüleme) diyor |
| **Otomatik REST API (Gateway) + tablo bazlı izin** | Ne Bytebase'de ne Atlas'ta var (Supabase'in alanı) |
| **Geri yüklenebilirliğin KANITLANMASI** | Rakiplerin hiçbiri "yedeği temiz bir sunucuya gerçekten geri yükleyip doğruluyoruz" demiyor — **doğrulanmalı** |
| **Kod üretimi / eject** | Kategoride benzeri yok |

### Fiyatlandırmaya etkisi

Rakip çıpaları:
- Atlas Pro: **$9/geliştirici/ay**
- Bytebase Pro: **$20/kullanıcı/ay**
- Supabase Pro: **$25/ay** (kullanıcı başına değil)

Ve Bytebase'in **20 kullanıcı / 10 instance ücretsiz** katmanı, küçük ekipler
için fiyatla rekabeti fiilen kapatıyor.

**Sonuç:** Namines düşük uçta fiyatla kazanamaz. Kazanabileceği yer,
Bytebase'in "fiyat sorulacak" dediği yetenekleri (onay akışı, risk
değerlendirmesi) erişilebilir bir fiyata sunmak.

---

## Hâlâ doğrulanmamış olanlar

Bu tur pazarın büyük kısmını kapattı ama şunlar açık:

- [ ] Bytebase'in "otomatik yedek + tek tıkla geri alma"sı geri yüklenebilirliği
      **doğruluyor mu**, yoksa yalnızca yedek alıp mı bırakıyor? Namines'in en
      güçlü iddiası buna bağlı.
- [ ] Bytebase'in görsel şema tasarımı gerçekten yok mu (fiyat sayfasında
      görünmüyor, ama ürün turuna bakılmalı)?
- [ ] DBeaver / DataGrip / TablePlus web sürümleri ve ekip özellikleri
      (kategori C — yarışılmayacaksa düşük öncelik)
- [ ] Neon ve PlanetScale branching yetenekleri (Ground konumlandırması için)
- [ ] Kullanıcı yorumları / topluluk görüşü — hiçbir rakip için toplanmadı
