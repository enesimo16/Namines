# 19 — Kaldırılacak / geliştirilecek özellikler

## Özellik kararları tablosu

| Feature | Amaç | Kullanıcı değeri | Teknik durum | UX | Security | **Karar** |
|---|---|---|---|---|---|---|
| Canvas (şema tuvali) | Görsel tasarım | Yüksek | Olgun | İyi | — | **KEEP** |
| `.nsl` dili + Compile | Metinle şema | Orta | Olgun | — | ⚠️ Auth yok (BACK-002) | **IMPROVE** |
| Branch (sunucu taraflı) | Şema dallanması | Yüksek | Testli | ? | ✅ | **KEEP** |
| Change Request + onay | Yönetişim | **Çok yüksek** | Testli | ? | ✅ | **KEEP** — ana varlık |
| Risk sınıflandırma + etki | Güvenli değişiklik | Çok yüksek | Testli | ? | ✅ | **KEEP** |
| Gateway (REST API) | Otomatik API | Yüksek | Olgun | — | ✅ İzin + denetim | **KEEP** |
| Gateway API anahtarları | Erişim kontrolü | Yüksek | Olgun | ? | ✅ | **KEEP** |
| Vault (yedek) | Veri güvenliği | **Çok yüksek** | Canlı doğrulandı | İyi | ✅ | **KEEP** — ana varlık |
| Ground (yönetilen DB) | Hızlı başlangıç | Orta | v1, düşük limit | ? | ✅ | **REWORK** — aşağıda |
| Neon sağlayıcısı | Harici barındırma | Orta | Canlı doğrulandı | ? | ✅ | **KEEP + genişlet** |
| Desk (CRUD arayüzü) | Veri yönetimi | Yüksek | Testli | ⚠️ Boş durum yok | ✅ | **IMPROVE** |
| Desk SQL konsolu | Sorgu | Yüksek | Temel | ⚠️ Geçmiş yok | ⚠️ Denetim yok | **IMPROVE** |
| `/api/executor/execute` | DDL uygulama | Yüksek | Çalışıyor | ⚠️ Onay yok | ❌ **SEC-001** | **REWORK** |
| Eject / kod üretimi | Kilitlenmeme | Orta | 19 hedef | ? | — | **KEEP** |
| AI şema üretimi | Hızlı başlangıç | Orta | Olgun | ? | ✅ Kota var | **KEEP** |
| AI DBA / etki açıklayıcı | Anlaşılırlık | Yüksek | Olgun | ? | ✅ | **KEEP** |
| MCP sunucusu + CLI | Ajan ekosistemi | Orta | Olgun | — | ? | **KEEP** |
| GitHub bot + webhook | Entegrasyon | Orta | Olgun | — | ✅ İmza doğrulaması | **KEEP** |
| Paylaşım / sosyal önizleme | Pazarlama | Düşük | Olgun | ? | ⚠️ XSS yolu (SEC-004) | **IMPROVE** |
| **Personal Access Token ekranı** | — | **Sıfır** | **Sahte** | Yanıltıcı | ❌ **SEC-003** | **REMOVE** |

---

## REMOVE — kaldırılacak

### R-01 — "Personal Access Token" ekranı
**Konum:** `frontend/components/canvas/panels/AIPreferencesModal.tsx:471-497`

Jeton tarayıcıda `Math.random()` ile üretiliyor, sunucuya gitmiyor, hiçbir yerde
doğrulanmıyor, "revoke" hiçbir şeyi iptal etmiyor. Gerçek karşılığı
(`GatewayKeyController`) zaten var.

**Karar:** Kaldır, kullanıcıyı gerçek anahtar ekranına yönlendir.
**Efor:** S · **Öncelik:** **P0**

> Alternatif: gerçek uca bağla. Ama iki API anahtarı kavramı olan bir ürün
> kullanıcıyı karıştırır. Kaldırmak daha temiz.

---

## REWORK — yeniden tasarlanacak

### RW-01 — `/api/executor/execute`

**Bugün:** Kimliği doğrulanmış herkes, herhangi bir bağlantıya, herhangi bir SQL'i,
denetimsiz ve onaysız çalıştırıyor.

**Olması gereken:** Kullanıcının **kayıtlı projesine** bağlı, risk
sınıflandırmasından geçen, denetime yazılan, yıkıcı işlemde onay isteyen bir uç.

Ürünün geri kalanı bu standardı zaten uyguluyor — bu uç ondan sapıyor.
**Efor:** M · **Öncelik:** **P0**

### RW-02 — Ground'un konumu

**Bugün:** "Kendi Supabase'imiz olsun" hedefiyle yazılmış, ama v1 çok düşük
limitlerle geçici bir zemin.

**Sorun:** Supabase/Neon/PlanetScale ile altyapı yarışına girmek, bu ekip
boyutunda kazanılabilir bir savaş değil. Kaynak orada harcanırsa asıl
farklılaştırıcı (yönetişim) gelişmez.

**Öneri:** Ground'u **"5 dakikada dene" zemini** olarak konumla, üretim
barındırma iddiasından vazgeç. Üretim için kullanıcıyı Neon/Supabase'e
**bağla** — `IDatabaseProvider` soyutlaması bunu zaten destekliyor ve Neon
sağlayıcısı canlı doğrulanmış durumda.

Bu bir kod kararı değil **ürün kararı**; kod değişikliği asgari.
**Öncelik:** P1 (strateji)

---

## IMPROVE — geliştirilecek

| # | Özellik | Ne yapılacak | Efor | Öncelik |
|---|---|---|---|---|
| I-01 | Desk ekranları | 7 ekrana boş durum ekle | S | **P1** |
| I-02 | Desk SQL konsolu | Sorgu geçmişi + kaydedilmiş sorgu | S | P1 |
| I-03 | Compile uçları | `[Authorize]` veya rate limit (BACK-002) | XS | P1 |
| I-04 | Paylaşım sayfası | Mermaid `securityLevel: 'strict'` (SEC-004) | XS | **P0** |
| I-05 | Vault | Kısmi geri yükleme; yedeği arka plan işine taşı | M | P2 |
| I-06 | Introspection | MSSQL + Oracle FK sorgularını **canlı doğrula** | M | **P1** |
| I-07 | Hata mesajları | `ClassifyConnectionFailure`'ı ortak servise taşı | S | P1 |
| I-08 | `frontend` | Store'lar için test yaz | L | P1 |

### I-06 hakkında not

`DURUM.md` MySQL introspection'ında iki gerçek hata bulunduğunu kaydediyor:
FK ilişkileri hiç okunmuyordu ve `AUTO_INCREMENT` yanlış kolondan okunuyordu.
MySQL düzeltilip doğrulandı; **MSSQL ve Oracle aynı kod yolundan geçti ama
doğrulanmadı.**

Yani "6 motor destekliyoruz" iddiasının iki motorda **bilinen bir hata sınıfı
açık olabilir**. Bu, ürün iddiası ile kanıt arasındaki en büyük boşluk.

---

## "Özellik olsun diye" eklenmiş bir şey var mı?

Denetim bunu özellikle aradı. Sonuç:

**Personal Access Token ekranı dışında yok.** Her özelliğin ya bir kullanıcı
problemi ya da bir ürün stratejisi karşılığı var ve çoğu belgede gerekçelendirilmiş.

Bu, denetimin beklemediği bir sonuç. 100k satırlık bir üründe tek bir ölü özellik,
olağandışı derecede iyi.

**Ancak:** Özellik sayısı **çok fazla**. Hiçbiri gereksiz değil ama hepsi birden
sürdürülemez. Bu bir "kaldır" sorunu değil, bir **odak** sorunu — bkz.
[15-product-analysis.md](15-product-analysis.md) "Kategori riski".
