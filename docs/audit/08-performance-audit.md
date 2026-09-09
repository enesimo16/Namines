# 08 — Performans denetimi

> ⚠️ **Bu rapor yapısaldır, ölçümsel değildir.** Yük testi yapılmadı, profil
> çıkarılmadı, bundle analiz edilmedi. Aşağıdakiler koddan görülebilen
> yapısal risklerdir; her biri "önce ölç" notuyla işaretlendi.

---

## PERF-001 — Gateway'de N+1 riski: KONTROL ALTINDA

`GatewayService.ExpandAsync` (satır 684-710) ilişkili kayıtları çekerken
**satır başına sorgu atmıyor**; toplanan anahtarlarla tek bir `IN (...)` sorgusu
kuruyor:

```csharp
$"SELECT * FROM {Quote(dbType, expand.Table)} " +
$"WHERE {Quote(dbType, expand.ToColumn)} IN ({string.Join(", ", names)})"
```

Bu, N+1'in doğru çözümü. Ayrıca FK'si NULL olan satırlarda bile alanın
oluşturulduğu ve **neden** oluşturulduğu yorumda yazılı (istemci tarafında tip
belirsizliği olmasın diye).

**Bulgu yok.** Aksine örnek gösterilecek bir uygulama.

---

## PERF-002 — Yedekleme belleğe toplanmıyor: GÜÇLÜ YAN

Vault, `System.IO.Pipelines` ile üç aşamalı akış kullanıyor:
**üret → dönüştür (şifrele) → tüket (depola)**. Bir gigabaytlık dump bile
belleğe hiç toplanmıyor.

Bu, yedekleme özelliklerinde en sık yapılan hatanın (dump'ı `byte[]`'e okumak)
yapısal olarak imkânsız kılınması demek.

**Bulgu yok.**

---

## PERF-003 — Yedekleme isteği senkron: kullanıcı bekliyor

### Finding
`POST /api/vault/{id}/backups` yedek bitene kadar HTTP isteğini açık tutuyor.

### Location
`backend/Namines.API/Controllers/VaultController.cs:142-163`

### Current State
Bu oturumdaki canlı ölçüm: küçük bir MariaDB veritabanı için ~2,8 saniye
(`createdAt` 13:04:59.578 → `completedAt` 13:05:02.375).

### Problem
Boyutla doğrusal büyür. 50 GB'lık bir veritabanında istek dakikalarca açık
kalır; araya giren her proxy/load balancer zaman aşımı bunu keser ve kullanıcı
"yedek başarısız" görür — oysa yedek sunucuda devam ediyordur.

`namines-vault/02-ERISIM-VE-DEPLOY.md` bunu **bilinen sınır** olarak zaten
kaydediyor.

### Severity **MEDIUM** · ### Effort M · ### Priority P2
### Recommendation
Uç 202 Accepted + `backupId` dönsün; istemci durumu yoklasın. Kayıt zaten
`Running` durumuyla oluşturuluyor — altyapı hazır, yalnızca uç davranışı
değişecek.

---

## PERF-004 — Frontend bundle: `mermaid` + `sql.js`

Bkz. [FE-007](04-frontend-audit.md#fe-007--bundle-ve-performans-ölçülmedi).

`mermaid` (^11.15) ve `sql.js` (^1.14, WASM) statik olarak içe aktarılıyorsa
ilk yüklemenin büyük bir payını oluşturur — ve ikisi de yalnızca belirli
rotalarda gerekiyor.

### Recommendation
`next/dynamic` ile `ssr: false`. **Önce `@next/bundle-analyzer` ile ölç.**
### Effort S · ### Priority P2

---

## PERF-005 — `templates.ts` 1.942 satır statik veri

`frontend/lib/templates.ts` — şablon tanımları. Statik olarak import ediliyorsa
her sayfa yüklemesinde JS bundle'a giriyor, oysa yalnızca `/new` rotasında
gerekli.

### Recommendation
Dinamik import ya da bir API ucundan servis et.
### Severity **LOW** (doğrulanmadı) · ### Effort S · ### Priority P3

---

## PERF-006 — Control DB sorguları

24 indeks doğru yerlerde (bkz. [DB-001](05-database-audit.md#db-001--i̇ndeks-kapsamı-sağlıklı)).
`SELECT *` deseni EF Core kullanıldığı için otomatik olarak sınırlı; yanıtlar
elle projekte ediliyor.

**Ölçülmedi** ama yapısal alarm yok.

---

## PERF-007 — Sayfalama üst sınırı — ❌ YANLIŞ POZİTİF

İlk taramada "doğrulanmadı" olarak işaretlenmişti. Teyit edildi: **tavan zaten var.**

`Namines.Infrastructure/Services/GatewayService.cs:64`

```csharp
pageSize = Math.Clamp(pageSize, 1, 200); // sınırsız sayfa boyutu = kaza ile tüm tabloyu dökme riski
```

`page` da `Math.Max(1, page)` ile korunuyor. Bulgu geri çekildi; kayda,
denetimin kendi yanlış pozitifi olarak bırakıldı.

**Ders:** "doğrulanmadı" etiketli her madde teyit edilmeli — bu maddede
teyit, bir düzeltme değil bir geri çekme üretti. Denetimin diğer
"doğrulanmadı" maddeleri de aynı gözle okunmalı.

---

## Ölçülmesi gerekenler (öncelik sırasıyla)

1. `@next/bundle-analyzer` ile ilk yükleme boyutu — **1 saat**
2. En sık kullanılan 5 ucun p95 gecikmesi (OpenTelemetry zaten kurulu,
   Prometheus exporter var) — **yarım gün**
3. 1M satırlık bir tabloda Gateway listesi — **yarım gün**
4. Eşzamanlı 100 istek altında bağlantı havuzu davranışı — **1 gün**

**Not:** OpenTelemetry (`AddNaminesObservability`) ve Prometheus exporter
**zaten kurulu**. Yani (2) için altyapı hazır; yalnızca ölçüm yapılmamış.
