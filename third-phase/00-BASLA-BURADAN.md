# Üçüncü Faz — Namines Ground · Vault · Desk

> **Bu faz ne DEĞİL:** Namines'in (şema tasarım düzlemi) devamı değil. Üç ayrı
> **ürün** — kullanıcıya üç ayrı isimle (Namines Ground/Vault/Desk) sunulan,
> ama mimarisi ikiye ayrılan üç yetenek.
>
> **Mimari (2026-09-07'de düzeltildi):** İlk yazımda üçü de "ayrı mikroservis"
> olarak tanımlanmıştı, gerekçe olarak `second-phase/14-AYRI-URUN-DEVELOPMENT-
> HOSTING.md` gösterilmişti. O doküman aslında **farklı bir konu** hakkında
> (kullanıcının kendi uygulamasını barındırma riski, Shopify tarzı) ve
> Vault/Ground'a hiç uygulanmıyordu — yalnızca Desk için geçerli bir emsal
> teşkil ediyor (Desk'in kendi kullanıcı arayüzü ve kullanıcı kitlesi var).
>
> Gerçek karar:
> - **Namines Desk** → ayrı mikroservis (`services/desk/`), kendi deploy
>   birimi. Sebebi: kendi UI'ı, kendi kullanıcı kitlesi (operasyon ekibi),
>   ana backend'e yalnızca HTTP ile bağlı — bu turda canlı doğrulandı.
> - **Namines Vault** ve **Namines Ground** → ana backend'in **içinde** .NET
>   modülleri (`Namines.Vault.*`, `Namines.Ground.*`), ayrı deploy birimi
>   DEĞİL. Sebebi: ikisinin de kendi kullanıcı arayüzü yok (Desk üzerinden
>   tetikleniyorlar) ve ikisi de ana backend'in ZATEN sahip olduğu bağlantı
>   şifreleme bileşenine (`AesGcmConnectionSecretProtector`) muhtaç — ayrı
>   servis olsalardı bu ya kopyalanır (güvenlik riski) ya da her işlemde
>   ana API'ye geri HTTP çağrısı atılırdı (kazancı olmayan maliyet).
>   Ayrıntı: [`namines-vault/00-GENEL-BAKIS.md`](../namines-vault/00-GENEL-BAKIS.md) §1,
>   [`namines-ground/00-GENEL-BAKIS.md`](../namines-ground/00-GENEL-BAKIS.md) §1.
>
> **İzolasyon Vault/Ground'da da korunuyor** — süreç seviyesinde değil, kod
> seviyesinde: kendi isim alanı, kendi klasörü, kendi rota öneki
> (`/api/vault/*`, `/api/ground/*`), kendi arayüzü. Kullanıcı arayüzünde ve
> API sözleşmesinde hâlâ **"Namines Vault"** / **"Namines Ground"** olarak
> adlandırılıyorlar — mimari fark kullanıcıya görünmez, geliştiriciye nettir.

---

## Üç servis

| Servis | Ne yapar | Durum |
|--------|----------|-------|
| **Namines Ground** | Namines projelerine yönetilen canlı veritabanı (Supabase benzeri) | Başlanmadı |
| **Namines Vault** | Gerçek veritabanından `.bak` / `pg_dump` / `mysqldump` yedeği | Başlanmadı |
| **Namines Desk** | Barındırılan, deterministik CRUD arayüzü (`namines.com/<user>/<proje>`) | **Başlanıyor** |

Sıra bilinçli: **Desk → Vault → Ground.** Gerekçe [§3](#3-neden-bu-sıra).

---

## 1. Kanıtlanan temel (2026-09-01)

Desk'e kod yazmadan önce, dayanacağı API gerçek bir PostgreSQL'e karşı
uçtan uca denendi. **Hiçbiri varsayım değil, hepsi canlı çalıştırıldı.**

Ortam: `namines-namines-control-db-1` container'ı içinde `desk_demo` veritabanı
(yeni imaj çekilmedi — disk %98 doluydu), backend `Security__AllowPrivateDbHosts=true`
ile Development'ta.

| Ne | Uç | Sonuç |
|----|----|-------|
| Listeleme | `POST /api/gateway/list` | 4 satır, sayfalama + `totalCount` doğru |
| Kayıt ekleme | `POST /api/gateway/create` | `affectedRows:1`, üretilen `id` geri döndü |
| Güncelleme | `POST /api/gateway/update` | `affectedRows:1` |
| Silme | `POST /api/gateway/delete` | `affectedRows:1` |
| **Bağımsız doğrulama** | `psql` ile DOĞRUDAN tabloya bakıldı | Satır gerçekten silinmişti — API'nin sözüne güvenilmedi |
| Şema keşfi | `POST /api/dbintrospect` | Tablo + kolon + tip + uzunluk + PK/FK/nullable |

**Sonuç: Desk'in ihtiyaç duyduğu backend'in tamamı zaten yazılmış.** Desk yeni bir
API değil, var olan API'ye **arayüz**. Bu, işi "Streamlit'i sıfırdan yaz"dan
"hazır API'ye panel yaz"a indiriyor.

### Deterministik olabilmesinin sebebi

`dbintrospect` kolon başına şunları döndürüyor: `type`, `length`, `isPK`, `isFK`,
`isNullable`, `defaultValue`, `identity`. Bu, arayüzü **AI'sız** üretmeye yeter:

- `isPK` → güncelleme/silmede hangi kolonun anahtar olduğu
- `type` + `length` → hangi giriş bileşeni (metin / sayı / onay kutusu / tarih)
- `isNullable` → alan zorunlu mu
- `identity` / `defaultValue` → ekleme formunda gizlenecek alanlar
- `isFK` + ilişkiler → açılır liste

---

## 2. Yolda bulunan ve düzeltilen gerçek hata

`dbintrospect` **`relations: []` döndürüyordu** — veritabanında gerçek bir FK
olmasına rağmen.

- Kolon sorgusu yalnızca `IS_FK` (evet/hayır) çekiyordu; **neye işaret ettiğini
  hiç sormuyordu.**
- `BuildSchemaAsync` da yalnızca `Tables` dolduruyordu; `Relations` **hiçbir
  motorda** doldurulmuyordu.

**Bu sadece Desk'i değil, ana ürünü de etkiliyordu:** kullanıcı canlı
veritabanından şema çektiğinde tuvalde tablolar geliyor ama **ilişki çizgileri
hiç gelmiyordu.**

Düzeltme: `BuildSchemaAsync`'e isteğe bağlı ikinci bir ilişki sorgusu +
`LoadRelationsAsync` + `ParseReferentialAction` eklendi. PostgreSQL için
`information_schema` yerine `pg_catalog` kullanıldı çünkü bileşik FK'larda
kaynak/hedef kolon eşleşmesinin **sırası** garanti değil.

Canlı doğrulandı (üç ayrı yol):

| Senaryo | Beklenen | Ölçülen |
|---------|----------|---------|
| Basit FK | `vehicles.customer_id → customers.id` | ✔ |
| CASCADE / SET NULL | `onDelete:Cascade`, `onUpdate:SetNull` | ✔ |
| **Bileşik FK** | `b_code→branch_code` ve `s_no→service_no` çaprazlanmadan | ✔ |

> ⚠️ **Yalnızca PostgreSQL düzeltildi.** MSSQL/MySQL/MariaDB/Oracle/SQLite hâlâ
> `relationSql` almıyor, yani onlarda `Relations` boş dönmeye devam ediyor.
> Yazıp doğrulamadan "yapıldı" demek bu projenin standardına aykırı olurdu
> ([CHECKLIST.md](../new-phase/CHECKLIST.md) girişindeki kural). Her motor,
> kendi canlı veritabanına karşı denendiğinde kapatılacak.

---

## 2b. Bağlantı nerede duruyor — ve neden değişmek zorunda kaldı

Tasarım düzlemi bağlantı dizesini **bilinçli olarak hiç saklamıyordu**; her
istekte bir kez kullanılıp atılıyordu. Tasarım aracı için doğru bir karardı.

Ama Desk **barındırılan** bir panel. Bağlantı saklanmazsa tarayıcının her
istekte veritabanı parolasını göndermesi gerekirdi — **parola istemcide yaşardı.**
Kabul edilemez.

Seçilen yol: bağlantı sunucuda **AES-256-GCM** ile şifreli durur, API
anahtarından çözülür, tarayıcı hiç görmez.

- `IConnectionSecretProtector` (Core) — arayüz; anahtar yönetimi değişebilsin diye
- `AesGcmConnectionSecretProtector` (Infrastructure) — PBKDF2 (100k tur) ile
  türetilmiş anahtar, `v1:` sürüm etiketi (rotasyon yolu açık)
- `CloudProject.EncryptedConnectionString` + `ConnectionDbType`
- `PUT/DELETE /api/gateway/keys/project/{id}/connection`
- `GatewayController.ResolveConnectionAsync` — istekte bağlantı varsa onu
  kullanır (**mevcut canvas davranışı hiç değişmedi**), yoksa anahtardan çözer

> ⚠️ `Security:ConnectionEncryptionKey` tanımlı değilse servis **açıkça durur**,
> sessizce şifresiz saklamaz. En az 32 karakter zorunlu.

### Uçtan uca kanıt (2026-09-01)

| Adım | Sonuç |
|------|-------|
| Bağlantı kaydedildi | `connected:true` |
| **DB'de düz metin mi?** | `v1:XCSaxjP09MF4VA6/:rhImi+…` — **şifreli**, düz metin yok |
| Anahtar üretildi + `customers`'a okuma izni verildi | `nmn_dgm1Ing9…` |
| **Bağlantı dizesi GÖNDERMEDEN, sadece anahtarla `list`** | **4 satır gerçek veri geldi** |
| İzin verilmeyen tablo (`vehicles`) | **403** — "not allowed to read" |
| Geçersiz anahtar | **401** |
| Anahtar yok + oturum yok | **401** |

Yani Desk'in güvenlik modeli çalışıyor: panel yalnızca anahtarın açıkça izin
verdiği tabloları görebiliyor, parolayı hiç görmüyor.

### Yolda düzeltilen ikinci hata

`AuthDbContextFactory` (tasarım-zamanı, `dotnet ef` bunu kullanıyor) bağlantıyı
`postgres/postgres` olarak **sabit kodluyordu**, oysa control DB `namines`
kullanıcısıyla açılıyor. Sonuç: `dotnet ef database update` her seferinde
"password authentication failed" veriyordu. Ortam değişkeni → doğru varsayılan
sırasına çevrildi.

---

## 3. Neden bu sıra

| | Gerçek durum | Efor | Risk |
|---|---|---|---|
| **Desk** | Backend %100 hazır (yukarıda kanıtlandı) | En küçük | Düşük — yeni altyapı yok |
| **Vault** | Kod var ama **yanlış şeyi** yedekliyor (bkz. §4) | Orta | Orta |
| **Ground** | Sıfırdan: provisioning, kota, izolasyon, yedek, nöbet | En büyük | **Yüksek** |

**Ground hakkında dürüst uyarı:** `second-phase/14`'te barındırmayı reddetme
gerekçelerinin (7/24 nöbet, kötüye kullanım, yasal sorumluluk, boşta yanan
kaynak) **tamamı** yönetilen veritabanı için de geçerli — üstelik daha ağır,
çünkü veri kaybı ürünü bitirir. İlk sürümde kendi Postgres'ini işletmek yerine
**Neon'u arkada sağlayıcı olarak kullanıp** üstüne kota/izolasyon katmanı yazmak
aynı ürünü nöbet riski olmadan verir. (Neon hesabı
[34-SENDEN-BEKLENENLER.md](../new-phase/34-SENDEN-BEKLENENLER.md)'de 🟢.)

---

## 4. Vault: mevcut kod yanlış şeyi yedekliyor

`DockerBackupService.RunSandboxAndBackupAsync(jobId, **sqlContent**, dbType, …)`
— parametre **DDL**. Yani: DDL'den geçici bir container kuruyor, o **boş**
container'ı yedekliyor. Bu bir **şema yedeği**, veri yedeği değil.

Vault'un yapması gereken tersi: kullanıcının **canlı** veritabanına bağlan →
`BACKUP DATABASE` (MSSQL) / `pg_dump` / `mysqldump` çalıştır.

Önemli sonuç: **Docker'ın rolü burada sandbox değil, sadece bu istemci
araçlarını taşınabilir şekilde çalıştırmak.** Yani Docker zorunlu değil —
ileride binary'ler paketlenirse tamamen çıkarılabilir.

---

## 5. Klasör ve modül disiplini

> §1 (üstteki kutu) neyin neden değiştiğini anlatıyor — burada yalnızca
> sonuçtaki klasör yapısı.

```
namines/
  backend/
    Namines.API/            # controller'lar — Vault/Ground kendi öneklerinde
    Namines.Core/
    Namines.Infrastructure/
    Namines.Vault/          # YENİ — Namines Vault modülü, kendi isim alanı
    Namines.Ground/         # YENİ — Namines Ground modülü, kendi isim alanı
  frontend/
  services/
    desk/                   # TEK gerçek mikroservis — ayrı deploy birimi
      package.json
      docker-compose.yml
      README.md
  third-phase/              # bu klasör
```

**Desk için (gerçek mikroservis):** `Namines.Core`'a **proje referansı YOK**,
iletişim yalnızca HTTP. Kabul kriteri: `cd services/desk && npm run dev` —
ana backend kapalıyken de UI ayağa kalkar (veriye erişemez, ama açılır).

**Vault/Ground için (modül):** `Namines.Vault.*` / `Namines.Ground.*` isim
alanı dışına sızmaz, kendi arayüzü (`IBackupProvider` / `IDatabaseProvider`)
üzerinden çağrılır, kendi rota önekinde yaşar (`/api/vault/*`,
`/api/ground/*`). "Sahte modül" olmasını engelleyen kural aynı ruhta ama
süreç sınırı değil, **kod sınırı**: bu iki isim alanı birbirine ya da
`Namines.API`'nin ilgisiz controller'larına doğrudan referans veremez —
yalnızca kendi arayüzleri ve `Namines.Core`'un paylaşılan modelleri
(`CloudProject` gibi) üzerinden konuşurlar.

---

## 6. Çalışma disiplini

1. **Önce yürüyen iskelet.** Tek bir uçtan uca yol (HTTP → gerçek DB → yanıt)
   canlı kanıtlanmadan ikinci özellik yazılmaz.
2. **Kanıt = çalışan komut + görülen çıktı.** "Test geçti" tek başına kanıt
   değil; §1 ve §2'deki tablolar bu formatta.
3. **Motor bazında dürüstlük.** Doğrulanmamış motor "yapıldı" sayılmaz (§2'deki
   uyarı kutusu bunun örneği).

---

## 7. Streamlit / Next.js paketlerinin kaldırılması

Kaldırılacak küme: `ScaffolderService.cs`, `ScaffolderController.cs`,
`CoderAIPackagerService.cs`, `CoderAIController.cs`, `DownloadHubPanel.tsx`,
`Dockerfile.streamlit-base`.

**Ama henüz değil.** Desk çalışır hâle gelene kadar dursunlar; yoksa arada
özelliksiz kalınır. Desk'in ilk sürümü kanıtlandığı gün tek commit'te temizlenir.

Ayrıca: "Next.js Enterprise Panel — **PREMIUM**" kartı `disabled` + "Coming soon"
durumda, ve Stripe fiyat kimlikleri olmadığı için zaten satılamıyor. Yani şu an
kullanıcıya **var olmayan bir ücretli katman** gösteriliyor. Bu bir kod işi değil,
ürün kararı — temizlikte birlikte ele alınacak.

---

## 8. Sıradaki adım (Desk)

Backend tarafı kanıtlandı. Kalan iş **arayüz**:

1. `services/desk/web/` — kendi `package.json`'ı olan ayrı Next.js uygulaması
2. Rota: `/<kullanıcı>/<proje>` + anahtar
3. Şemayı `dbintrospect`'ten okuyup **deterministik** form üret
   (`type`→bileşen, `isNullable`→zorunluluk, `isPK`→anahtar, `isFK`→açılır liste)
4. `list` / `create` / `update` / `delete` bağla

**Henüz yazılmadı** — bu dosyadaki her şey backend'in kanıtlanmasıydı.

### Desk için açık kalan sorular

- `create`/`update`/`delete` uçları hâlâ istekte bağlantı bekliyor; şu an yalnızca
  `list` anahtardan çözüyor. Desk'in tam CRUD'u için aynı desen diğer uçlara da
  uygulanacak (bilinçli olarak tek uçta kanıtlandı, sonra yayılacak).
- Rotadaki "kullanıcı adı" nereden gelecek? Bugün `ApplicationUser.UserName` var
  ama URL'de benzersizlik garantisi ayrıca doğrulanmalı.

---

## 9. Desk v0.1 — YAPILDI (2026-09-01)

`services/desk/` — kendi `package.json`'ı, kendi portu (3200), kendi
`Dockerfile`/`docker-compose.yml`'i, kendi `README.md`'si olan **ayrı** Next.js
uygulaması. `Namines.Core`'a ya da ana `frontend/`'e **hiçbir kod referansı yok**;
iletişim yalnızca `/api/gateway/*` HTTP sözleşmesi üzerinden.

### Deterministik katman (`lib/schema.ts`)

Saf fonksiyonlar, **AI yok**. Aynı veritabanı her zaman aynı arayüzü üretir:

| Kolon bilgisi | Sonuç |
|---|---|
| `bool`/`bit` | onay kutusu |
| `timestamp`/`datetime` | tarih-saat girişi |
| `int`/`numeric`/`decimal` | sayı girişi |
| `text`/`json` veya `length > 255` | çok satırlı |
| `isNullable = false` | zorunlu (`*`) |
| `isPK` + otomatik artan | ekleme formunda gizli |
| Bileşik PK | tablo **salt-okunur** |

**Bileşik PK neden salt-okunur:** Gateway'in `update`/`delete` uçları TEK bir
`pkColumn` alıyor. Düzenlenebilir göstermek, kaydetme anında sessizce YANLIŞ
SATIRI güncellemek olurdu.

### Tarayıcıdan uçtan uca doğrulandı

| Ne | Sonuç |
|---|---|
| Anahtarla giriş → şema okundu | 2 tablo, kolon tipleriyle |
| Form üretimi | `BOOLEAN`→checkbox, `TIMESTAMP`→datetime, `id`→gizli, NOT NULL→zorunlu |
| **Ekleme** | `psql` ile doğrulandı — satır gerçekten yazıldı |
| **Güncelleme** | `full_name` + `is_active` gerçekten değişti |
| **Silme** | satır gerçekten gitti |
| FK gösterimi | `customer_id→customers` (§2'deki düzeltmenin meyvesi) |
| Yazma izni yok | arayüz salt-okunur, sebebi yazılı |

### Yolda yapılan ve düzeltilen hata

Toplu regex ile guard temizlerken `if (...)` satırı silinip
`return BadRequest(...)` satırı **öksüz kalmıştı** — Detail/Update/Delete
koşulsuz 400 dönüyordu. Derlemedeki "ulaşılamayan kod" uyarıları işaretti,
ilk seferde kaçırıldı; UI'da düzenleme denenince yakalandı. Guard'lar
bağlantı hariç, gerçekten zorunlu alanlar için doğru biçimde geri eklendi.

### Developer Package kaldırıldı

`DownloadHubPanel` sekmesi (indirilebilir Streamlit paketi + satılamayan
"Next.js Enterprise — PREMIUM / Coming soon" kartı) compile ekranından
çıkarıldı; yerine sol panelde **Namines Desk (beta)** bağlantısı var.
Yön değişimi: *indirilen* panel yerine *barındırılan* panel.

> Arka uç dosyaları (`ScaffolderService`, `CoderAIPackagerService` ve
> controller'ları) HENÜZ SİLİNMEDİ — başka uçlar onlara bağlı olabilir,
> ayrı bir temizlik turu istiyor.

### Sıradaki (Desk)

- FK açılır listesi (hedef biliniyor, veri çekilmiyor)
- Filtreleme / sıralama (Gateway destekliyor, arayüzü yok)
- `/kullanıcı/proje` rotası — kullanıcı adının URL benzersizliği doğrulanmalı

---

## 10. Desk v1 — plan yazıldı

v0.1 (deterministik CRUD) çalışıyor. **v1'in kapsamı ayrı bir klasöre alındı:**
[`namines_desk/`](../namines_desk/00-GENEL-BAKIS.md) — her panel kendi
dokümanında.

| # | Doküman | Konu |
|---|---|---|
| 00 | [Genel Bakış](../namines_desk/00-GENEL-BAKIS.md) | Kapsam, elimizde ne var / ne yok |
| 01 | [Kimlik ve Oturum](../namines_desk/01-KIMLIK-VE-OTURUM.md) | Anahtar yerine JWT |
| 02 | [Projects](../namines_desk/02-PROJECTS.md) | Ana ekran + Import |
| 03 | [Canvas](../namines_desk/03-CANVAS.md) | Şema görünümü + drift |
| 04 | [Data (CRUD)](../namines_desk/04-DATA-CRUD.md) | v0.1'in kaydı + v1 eklentileri |
| 05 | [Deployments](../namines_desk/05-DEPLOYMENTS.md) | Şema sürüm geçmişi |
| 06 | [Logs](../namines_desk/06-LOGS.md) | Denetim kaydı |
| 07 | [Analytics](../namines_desk/07-ANALYTICS.md) | Gerçek veriden metrikler |
| 08 | [Tasarım Sistemi](../namines_desk/08-TASARIM-SISTEMI.md) | Vercel düzeni + Namines paleti |
| 09 | [Yol Haritası](../namines_desk/09-YOL-HARITASI.md) | Sıra + kabul kriterleri |

### Planı şekillendiren üç bulgu

1. **Anahtar kavramı kalkıyor.** Kullanıcı geri bildirimi ("key nereden alınacak
   belli değil") haklıydı. Gateway zaten oturum yolunu destekliyor; Desk JWT ile
   çalışacak, API anahtarları asıl sahibine (dış uygulamalar) kalacak.
   Gereken tek backend değişikliği: oturum yolunda `projectId` kabul etmek —
   **yetki doğrulamasıyla birlikte.**

2. **Vercel'in metrikleri kopyalanamaz.** Edge Requests / Fluid CPU / Fast Origin
   Transfer, Vercel'in kendi altyapısından gelir; Namines kimsenin uygulamasını
   çalıştırmıyor. `NaminesMetrics` sayaçları proje bazlı değil ve DB'de değil;
   `UsageEvent` ise **kullanıcı** bazlı (`ProjectId` alanı yok). Analytics
   ekranı `GatewayAuditEntry` üzerine kuruluyor — o tabloda `ProjectId` var.

3. **"Pushla direkt değişsin" v1 dışı.** Şema değişikliğini gerçek veritabanında
   çalıştırmak `ALTER TABLE` demek ve **geri alınamaz**. Migration üretimi, risk
   analizi ve onay politikası hazır; eksik olan **yedek**. Bu yüzden bu özellik
   Vault'tan sonra.

---

## 11. Desk v2 — bitti; Vault ve Ground — plan yazıldı (2026-09-06)

Desk v1 (D1-D7) ve Desk v2 (E1-E4, E5.1-E5.2) tamamlandı — bitiş durumu
[`namines_desk/11-DESK-V2-TAMAMLANDI.md`](../namines_desk/11-DESK-V2-TAMAMLANDI.md)'de.

Sıradaki iki servisin planı, Desk'in kendi planlama disipliniyle
(`namines_desk/00-GENEL-BAKIS.md` formatı) ayrı klasörlere yazıldı — henüz
kod yok, bu bir planlama turu:

| Servis | Doküman |
|---|---|
| Desk v2.1 pano kabuğu | [`namines_desk/12-DESK-PANO-KABUGU.md`](../namines_desk/12-DESK-PANO-KABUGU.md) |
| Namines Vault | [`namines-vault/00-GENEL-BAKIS.md`](../namines-vault/00-GENEL-BAKIS.md) |
| Namines Ground | [`namines-ground/00-GENEL-BAKIS.md`](../namines-ground/00-GENEL-BAKIS.md) |

Her iki plan da §3'teki sırayı (**Vault önce, Ground sonra**) ve §6'daki
çalışma disiplinini (yürüyen iskelet önce, kanıt = çalışan komut, motor
bazında dürüstlük) aynen miras alıyor.
