# 15 — Ürün analizi

> ## ✅ RAKİP VERİSİ GELDİ (2026-09-09) — bazı puanlar DEĞİŞTİ
>
> Bu rapor önce rakip verisi **olmadan** yazıldı. Veri toplandıktan sonra
> (bkz. [16](16-competitor-analysis.md)) şu değerlendirmeler düzeltildi:
>
> | Özellik | Önceki | Düzeltilmiş | Neden |
> |---|---|---|---|
> | MCP sunucusu | Avantaj **4** | **1** | Bytebase onu **ücretsiz** katmanda veriyor |
> | Vault (yedek) | **5** | **4** | Bytebase Community'de de "otomatik yedek + tek tıkla geri alma" var. Farkımız yedeğin **kanıtlanması** — ve o henüz doğrulanmadı |
> | Change Request + onay | **5** | **5** ✅ doğrulandı | Bytebase bunu **Enterprise'a** saklıyor — fark GERÇEK ve ticari |
> | Risk sınıflandırması | **5** | **5** ✅ doğrulandı | Aynı |
> | Canvas | **3** | **4** | Bytebase'de düzenlenebilir görsel tuval **yok**; Atlas yalnızca ERD görüntüleme |
> | Ground | **2** | **1** | Supabase'in **ücretsiz** planı bile Ground v1'in üstünde |
>
> **Ana tez değişmedi ve güçlendi:** ürün DBeaver kategorisinde değil,
> Bytebase kategorisinde. Ama o kategoride rakip sanıldığından olgun.

## En önemli bulgu: bu ürün sanılan kategoride değil

Denetim talebi bu projeyi bir **"Database Management / Database GUI"** ürünü
olarak tanımlıyor ve DBeaver, DataGrip, TablePlus, pgAdmin ile karşılaştırılmasını
istiyor.

**Kod bunu desteklemiyor.** Depodaki `AGENTS.md` ürünün yönünü açıkça yazıyor:

> Ürün artık "AI ile database üret" değil, **"AI ile database/backend
> lifecycle'ını güvenle yönet."**

Özellik envanteri de bunu doğruluyor:

| DBeaver sınıfı bir üründe olan | Namines'te | Namines'te olup DBeaver'da olmayan |
|---|---|---|
| SQL editörü | 🟡 Desk'te var, temel | Şema tasarım tuvali (görsel ER) |
| Sorgu geçmişi | ❌ | Sürüm kontrollü şema (branch) |
| Kaydedilmiş sorgular | ❌ | Change Request + onay politikası |
| Veri düzenleme ızgarası | 🟡 Desk CRUD | Risk sınıflandırması + etki analizi |
| Bağlantı yöneticisi | ✅ | Otomatik REST API (Gateway) |
| Toplu içe/dışa aktarma | 🟡 Import var | Kod üretimi / eject (SDK, panel) |
| ER diyagramı | ✅ | Yönetilen veritabanı sağlama (Ground) |
| — | — | Şifreli yedek + doğrulanmış geri yükleme (Vault) |
| — | — | MCP sunucusu + CLI + AI ajanı |

**Gerçek kategori:** şema tasarımı + değişiklik yönetişimi + otomatik API +
yönetilen barındırma. Yani rakipler **Supabase, Neon, PlanetScale, Prisma,
Hasura, Atlas/Bytebase** — masaüstü DB istemcileri değil.

**Neden önemli:** DBeaver'a göre konumlanırsa ürün "eksik bir DBeaver" gibi
görünür (sorgu geçmişi yok, kısayol yok, offline yok). Bytebase/Supabase'e göre
konumlanırsa **kendi güçlü yanlarıyla** yarışır.

---

## Özellik envanteri ve değerlendirme

Puanlar 1-5. "Teknik durum" bu denetimin kanıt seviyesinden geliyor.

| Özellik | Kullanıcı değeri | İş değeri | Karmaşıklık | Teknik durum | Rekabet avantajı |
|---|---|---|---|---|---|
| **Şema tasarım tuvali (Canvas)** | 4 | 4 | 4 | ✅ Olgun | 3 — benzerleri var |
| **6 motora DDL üretimi** | 4 | 3 | 4 | ✅ Golden-file korumalı | **4** — çoğu rakip tek motor |
| **Şema introspection** | 5 | 4 | 4 | 🟡 MSSQL/Oracle FK doğrulanmadı | 3 |
| **Branch (sunucu taraflı)** | 4 | 4 | 5 | ✅ Testli | **5** — asıl farklılaştırıcı |
| **Change Request + onay** | 5 | 5 | 4 | ✅ Testli | **5** — Bytebase dışında ender |
| **Risk sınıflandırması + etki analizi** | 5 | 5 | 4 | ✅ | **5** |
| **Gateway (otomatik REST API)** | 4 | 5 | 5 | ✅ İzin modeli + denetim | 4 — Supabase/Hasura'nın işi |
| **Vault (şifreli yedek)** | 5 | 4 | 4 | ✅ **Canlı doğrulandı** (PG/MySQL/MariaDB) | **5** — geri yüklenebilirlik KANITLANIYOR |
| **Ground (yönetilen DB)** | 4 | 5 | 5 | 🟡 v1, düşük limitler | 2 — Neon/Supabase çok önde |
| **Eject / kod üretimi** | 3 | 3 | 4 | ✅ 19 hedef | 3 |
| **AI şema üretimi** | 3 | 3 | 3 | ✅ | 2 — herkeste var |
| **AI DBA / etki açıklayıcı** | 4 | 4 | 3 | ✅ | 4 |
| **MCP sunucusu + CLI** | 3 | 4 | 3 | ✅ | **4** — ajan ekosistemine erken giriş |
| **Desk (barındırılan CRUD)** | 4 | 3 | 3 | ✅ Testli | 3 |
| **Paylaşım / sosyal önizleme** | 2 | 3 | 2 | ✅ | 2 |
| **Personal Access Token** | **0** | **0** | 1 | ❌ **SAHTE** | — |

---

## "Bu ürün neden kullanılmalı?" — dürüst cevap

Bugün en savunulabilir cevap **tek cümlede**:

> *Veritabanı şemanı git gibi dallandır, değişikliği riskine göre onaya sok,
> yedeğinin gerçekten geri yüklenebildiğini kanıtla — altı motorda.*

Bu cümledeki her iddianın koddan karşılığı var ve çoğu test/canlı doğrulama ile
destekli. **Bu, ürünün gerçek konumu.**

### ✅ Rakip verisiyle keskinleştirilmiş hâli

Veri geldikten sonra bu cümle daha da daraltılabilir — ve daralttıkça
güçleniyor:

> *Bytebase'in "fiyat sorulacak" katmanına sakladığı **onay akışı ve risk
> değerlendirmesini** temel üründe veriyoruz — üstelik şemayı görsel olarak
> tasarlayabildiğiniz bir tuvalle ve yedeğinizin gerçekten geri yüklendiğini
> kanıtlayarak.*

Bu cümlenin üç parçası da doğrulandı:
1. Bytebase'in fiyat sayfası Approval Workflow ve Risk Assessment'ı
   **Enterprise** sütununda gösteriyor.
2. Ne Bytebase'in ne Atlas'ın fiyat sayfasında düzenlenebilir bir görsel tuval
   yok (Atlas "ERD visualization" diyor — görüntüleme).
3. Geri yüklenebilirlik kanıtı Namines'te canlı doğrulandı; rakiplerin böyle bir
   iddiası **görünmüyor** (ama yokluğu kanıtlanmadı — ürün turuna bakılmalı).

Buna karşılık şu cümleler **savunulamaz** ve pazarlamada kullanılmamalı:
- "AI ile veritabanı tasarla" — herkeste var, farklılaştırmıyor.
- "6 veritabanını yönet" — Vault 3'ünü destekliyor, introspection'ın 2'si
  doğrulanmamış.
- "API anahtarı yönetimi" — Gateway'de gerçek, ama Ayarlar ekranındaki sahte.

---

## Kategori riski

Ürün **çok fazla şey** yapıyor: tasarım + yönetişim + API + barındırma + yedek +
kod üretimi + AI + MCP. 100k satırda tek kişilik/küçük ekip için bu, her alanda
rakiplerin gerisinde kalma riski demek.

**Öneri:** Bir alanı **kazanılacak** olarak seç, gerisini "yeterince iyi" tut.
Kanıta göre kazanılacak alan **değişiklik yönetişimi + doğrulanmış yedek**:
orada hem kod olgun, hem rakip az, hem de ödeme isteği yüksek (uyum bütçesi).
