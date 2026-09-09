# 17 — Özellik karşılaştırma matrisi

> ## ✅ RAKİP SÜTUNLARI DOLDURULDU (2026-09-09)
>
> Kaynaklar, resmî fiyat sayfalarından tarayıcıyla okundu:
> [bytebase.com/pricing](https://www.bytebase.com/pricing/) ·
> [atlasgo.io/pricing](https://atlasgo.io/pricing) ·
> [supabase.com/pricing](https://supabase.com/pricing)
>
> **Gösterim:** `C` = Bytebase Community (ücretsiz) · `P` = Pro · `E` = Enterprise.
> Boş hücre = *o sayfada görünmüyor* — "yok" demek değil.
>
> **DBeaver sütunu bilerek yok:** kategori C'de yarışılmama kararı verildi
> (bkz. [15](15-product-analysis.md)), veri toplamaya değmedi.

---

## A. Şema ve tasarım

| # | Özellik | **Bizde** | Bytebase | Atlas | Supabase | Öncelik |
|---|---|---|---|---|---|---|
| 1 | **Görsel ER tuvali (düzenlenebilir)** | ✅ Canvas | — | ERD yalnızca **görüntüleme** | ✅ | **FARK** |
| 2 | Metin tabanlı şema dili | ✅ `.nsl` | ✅ C | ✅ HCL (çekirdek) | — | — |
| 3 | Çok motorlu DDL üretimi | ✅ 6 motor, golden-file korumalı | ✅ C | Starter 4 motor, Pro tümü | Yalnızca Postgres | — |
| 4 | Şema introspection | ✅ 6 motor (MSSQL/Oracle FK ⚠️) | ✅ C | ✅ | ✅ | P1 |
| 5 | AI ile şema üretimi | ✅ | ✅ C (AI yardımı) | — | — | — |
| 6 | ER diyagramı dışa aktarma | ✅ Mermaid + görsel | — | ✅ | — | — |

## B. Değişiklik yönetişimi — **ürünün kalbi**

| # | Özellik | **Bizde** | Bytebase | Atlas | Supabase | Öncelik |
|---|---|---|---|---|---|---|
| 7 | Git tabanlı şema sürüm kontrolü / branch | ✅ | ✅ **C** | ✅ | — | — |
| 8 | Change Request akışı | ✅ | ✅ C | 🟡 CI/CD üzerinden | — | — |
| 9 | **Risk değerlendirmesi** | ✅ **temel üründe** | **E only** | ✅ P (linting) | — | **TİCARİ FARK** |
| 10 | **Onay iş akışı** | ✅ **temel üründe** | **E only** | — | — | **TİCARİ FARK** |
| 11 | Etki analizi | ✅ `SchemaImpactAnalyzer` | ✅ **C — 200+ SQL kuralı** | ✅ P (kolon lineage) | — | Geride |
| 12 | AI etki açıklayıcı | ✅ | ✅ C | — | — | — |
| 13 | "Affected Code" statik tarama | ✅ | — | ✅ P (lineage) | — | — |
| 14 | Denetim kaydı (değişiklik) | ✅ sınırsız | ✅ C changelog; P 7 gün, E sınırsız | ✅ P | — | — |
| 15 | Denetim kaydı (keyfi SQL) | ✅ **düzeltildi** | ✅ C | ✅ P | — | ✅ |
| 16 | Migration üretimi | ✅ | ✅ C | ✅ (çekirdek) | ✅ CLI | — |
| 17 | Ephemeral container'da DDL testi | ✅ "Run Tests" | — | ✅ P | — | — |
| — | **Rollout politikası (kademeli/zamanlanmış)** | ❌ | ✅ C | ✅ | — | P2 |
| — | **Çoklu veritabanında toplu değişiklik** | ❌ | ✅ C | ✅ | — | P2 |
| — | **Drift tespiti** | ❌ | — | ✅ **P (öne çıkan özellik)** | — | P2 |

## C. Veri erişimi ve API

| # | Özellik | **Bizde** | Bytebase | Atlas | Supabase | Öncelik |
|---|---|---|---|---|---|---|
| 18 | **Otomatik REST API** | ✅ Gateway | — | — | ✅ (çekirdek) | **FARK** |
| 19 | API anahtarı + tablo bazlı izin | ✅ | 🟡 IAM | — | ✅ RLS | **FARK** |
| 20 | Gateway denetim kaydı | ✅ | ✅ C | — | — | — |
| 21 | OpenAPI üretimi | ✅ | — | — | ✅ | — |
| 22 | Doğal dil sorgu | ✅ `query-nl` | ✅ C (AI) | — | — | — |
| 23 | GraphQL | ❌ | — | — | ✅ | P3 |
| 24 | Gerçek zamanlı abonelik | ❌ | — | — | ✅ | P3 |

## D. SQL istemcisi — **en zayıf alanımız**

| # | Özellik | **Bizde** | Bytebase | Atlas | Supabase | Öncelik |
|---|---|---|---|---|---|---|
| 25 | Web tabanlı SQL editörü | 🟡 Temel | ✅ C | — | ✅ | — |
| 26 | **Sorgu geçmişi** | ❌ | ✅ **C — ücretsiz** | — | ✅ | **P1 ↑** |
| 27 | **Kaydedilmiş sorgular** | ❌ | ✅ **C — ücretsiz** | — | ✅ | **P1 ↑** |
| 28 | Otomatik tamamlama | ❌ | ✅ | — | ✅ | P2 |
| 29 | Execution plan görüntüleme | ❌ | — | — | ✅ | P2 |
| 30 | Veri ızgarasında düzenleme | ✅ Desk | ✅ C | — | ✅ | — |
| 31 | **Veri dışa aktarma** | ❌ | ✅ **C — ücretsiz** | — | ✅ | **P1 ↑** |
| 32 | Klavye kısayolları / komut paleti | ❌ | ? | — | ✅ | P2 |
| — | Dinamik veri maskeleme | 🟡 Kısmî | **E only** | — | — | — |
| — | Just-in-Time erişim | ❌ | **E only** | — | — | P3 |

## E. Yedekleme ve süreklilik

| # | Özellik | **Bizde** | Bytebase | Atlas | Supabase | Öncelik |
|---|---|---|---|---|---|---|
| 33 | Elle yedek | ✅ PG/MySQL/MariaDB canlı doğrulandı | ✅ C | — | ✅ | — |
| 34 | Zamanlanmış yedek | ✅ çoklu instance güvenli | ✅ C (otomatik) | — | ✅ Pro: günlük, 7 gün | — |
| 35 | Yedek şifreleme (AES-256-GCM) | ✅ | ? | — | ? | — |
| 36 | Nesne depoya yazma (S3/MinIO) | ✅ gerçek MinIO'ya karşı doğrulandı | ? | — | — | — |
| 37 | **Geri yüklenebilirlik KANITI** | ✅ **temiz sunucuya gerçek restore** | ❓ "tek tıkla geri alma" var, **doğrulama iddiası yok** | — | ❓ | **Muhtemel FARK** |
| 38 | Saklama politikası | ✅ | ✅ | — | ✅ 7/14 gün | — |
| 39 | Kısmi (tek tablo) geri yükleme | ❌ | ? | — | — | P2 |
| 40 | PITR | ❌ | — | — | ✅ (add-on) | P3 |

**37. satır hâlâ en değerli hücre** — ama artık bir soru işaretiyle: Bytebase'in
geri almasının restorability doğrulaması yapıp yapmadığı bilinmiyor. Ürün turuna
bakılmalı.

## F. Barındırma ve altyapı

| # | Özellik | **Bizde** | Bytebase | Atlas | Supabase | Öncelik |
|---|---|---|---|---|---|---|
| 41 | Yönetilen veritabanı sağlama | 🟡 Ground v1, düşük limit | — | — | ✅ **çok önde** | Yarışma |
| 42 | Harici sağlayıcı (Neon) entegrasyonu | ✅ canlı doğrulandı | — | — | — | **FARK** |
| 43 | Supabase entegrasyonu | ❌ | — | — | — | **P2 — stratejik** |
| 44 | Self-hosted | ✅ prod compose | ✅ (air-gapped) | ✅ E | ✅ | ✅ |
| — | Terraform sağlayıcısı | ❌ | ✅ **C** | ✅ | — | P2 |
| — | Kubernetes operatörü | ❌ | ✅ | ✅ | — | P3 |

## G. Ekip, kimlik ve uyum

| # | Özellik | **Bizde** | Bytebase | Atlas | Supabase | Öncelik |
|---|---|---|---|---|---|---|
| 45 | Organizasyon + roller | ✅ 5 rol | ✅ C (IAM); özel roller E | ✅ | ✅ | — |
| 46 | Davet akışı | ✅ | ✅ | ✅ | ✅ | — |
| 47 | **MFA / 2FA** | ❌ | **E only** | — | ✅ | P2 |
| 48 | **SSO** | ❌ | P: Google/GitHub · E: OIDC/LDAP | ✅ E | ✅ Team ($599/ay) | P2 |
| 49 | Jeton iptali | ✅ **düzeltildi** | ✅ | — | ✅ | ✅ |
| 50 | Faturalama (Stripe) | ✅ | — | — | — | — |
| — | **SOC 2 / HIPAA** | ❌ | ✅ **SOC 2 Type 2 + HIPAA** | — | ✅ Team: SOC2+ISO27001 | P3 |
| — | SCIM / kullanıcı sağlama | ❌ | E only | — | — | P3 |

## H. Ekosistem

| # | Özellik | **Bizde** | Bytebase | Atlas | Supabase | Öncelik |
|---|---|---|---|---|---|---|
| 51 | CLI | ✅ | ✅ | ✅ (çekirdek) | ✅ | — |
| 52 | **MCP sunucusu** | ✅ | ✅ **C — ücretsiz** | — | ✅ | ❌ **Fark DEĞİL** |
| 53 | GitHub entegrasyonu | ✅ bot + webhook + PR yorumu | ✅ | ✅ | ✅ | — |
| 54 | Kod üretimi / eject (19 hedef) | ✅ | — | — | 🟡 tip üretimi | **FARK** |
| 55 | TypeScript SDK | ✅ | — | — | ✅ | — |
| — | ORM entegrasyonları (Go/Py/JS/Java/C#) | 🟡 eject ile | — | ✅ **P (öne çıkan)** | 🟡 | P3 |
| — | CVE taraması / güvenlik grafiği | ❌ | — | ✅ P | — | P3 |

---

## Fiyat çıpaları

| Ürün | Ücretsiz katman | İlk ücretli katman |
|---|---|---|
| **Bytebase** | **20 kullanıcı, 10 instance — süresiz** | $20 / kullanıcı / ay |
| **Atlas** | Sınırlı (4 motor) | **$9 / geliştirici / ay** + kullanım |
| **Supabase** | 500 MB, 2 proje (1 hafta sonra duraklatılır) | $25 / ay |

**Bytebase'in ücretsiz katmanı, küçük ekipler için fiyat rekabetini fiilen
kapatıyor.** Namines düşük uçta fiyatla kazanamaz.

---

## Sonuç

### ÖNDEYİZ — kanıtlı

| Ne | Neden |
|---|---|
| **Risk değerlendirmesi + onay akışı temel üründe** | Bytebase ikisini de **Enterprise'a** (fiyat sorulacak) saklıyor. **En net ticari fark.** |
| Düzenlenebilir görsel şema tuvali | Bytebase'de yok; Atlas yalnızca görüntüleme |
| Otomatik REST API + tablo bazlı izin | Ne Bytebase'de ne Atlas'ta |
| Eject / kod üretimi / SDK | Kategoride benzeri yok |
| Neon entegrasyonu | Kimsede yok |
| Geri yüklenebilirlik kanıtı | ❓ Muhtemel — doğrulanmalı |

### GERİDEYİZ — düşündüğümüzden fazla

| Ne | Acı gerçek |
|---|---|
| Sorgu geçmişi, kaydedilmiş sorgu, dışa aktarma | Üçü de Bytebase'in **ÜCRETSİZ** katmanında. "Eksik özellik" değil, **temel beklenti**. |
| 200+ SQL inceleme kuralı | Bytebase Community'de; bizim risk sınıflandırmamız bu ölçekte değil |
| Terraform sağlayıcısı, K8s operatörü | Hem Bytebase'de hem Atlas'ta |
| Drift tespiti, rollout politikası, toplu değişiklik | Rakiplerde var, bizde yok |
| SOC 2 / HIPAA | Bytebase'de var; kurumsal satışta kapı |
| MFA / SSO | Kurumsalda zorunlu |
| Yönetilen barındırma | Supabase Free bile Ground v1'in üstünde |

### GERİ ÇEKİLEN iddia

**MCP sunucusu farklılaştırıcı değil.** Bytebase onu ücretsiz katmanda veriyor.
İlk denetimde "ajan ekosistemine erken giriş" diye sayılmıştı; veri çürüttü.

### Öncelik değişikliği

**B-21 (sorgu geçmişi), B-22 (kaydedilmiş sorgular) ve B-44 (dışa aktarma)
yükseltildi.** Rakibin ücretsiz katmanında olan bir şeyin yokluğu, demoda ilk
sorulan soru olur — ve üçü de küçük işler.
