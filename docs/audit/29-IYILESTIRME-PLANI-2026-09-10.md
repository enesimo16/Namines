# 29 — İyileştirme planı: güvenlik, üretime hazırlık, kod kalitesi ve ürünler

**Tarih:** 10.09.2026
**Başlangıç noktası:** 2026-09-09 denetimi + düzeltme turu (`d95cd5d`)
**Sınırlar:** [28-DENETIMIN-SINIRLARI-2026-09-10.md](28-DENETIMIN-SINIRLARI-2026-09-10.md)

---

## Bugünkü puanlar

| Alan | Puan | Hedef |
|---|---|---|
| Güvenlik | **84** | 95 |
| Üretime hazırlık | **79** | 90 |
| Kod kalitesi | **78** | 88 |

Gerekçeler: [26-production-readiness.md](26-production-readiness.md)

---

# 1. Güvenlik: 84 → 95

| # | İş | Neden | Efor | Backlog |
|---|---|---|---|---|
| 1 | **Sızdırılmış parola kontrolü (HIBP)** | 12 karakter yapıldı ama `Password123456` hâlâ geçiyor. Karmaşıklık kuralı değil, **liste kontrolü** çözer | S | B-58 |
| 2 | **MFA (TOTP)** | Bytebase'de Enterprise'da var; kurumsal satışta kapı. Identity TOTP sağlayıcısı **zaten pakette** | M | B-30 |
| 3 | ~~Gateway yetkisini filtreye taşı~~ → **konvansiyon testi** | Karar değişti, gerekçe aşağıda | ~~L~~ **S** | B-23 |
| 4 | **SSRF: egress allowlist** | DNS rebinding kodla tam kapanmıyor; doğrulanan IP ile bağlanılan IP farklı olabilir | S (altyapı) | B-27 |
| 5 | **SOC 2 / HIPAA hazırlığı** | Bytebase **SOC 2 Type 2 + HIPAA** sertifikalı. Kurumsalda bu bir ön şart, özellik değil | XL | — |

### B-23 kararı değişti: refactor yerine test

Plan "Gateway yetkisini filtreye taşı" diyordu (L efor). Uygulamadan önce
**ölçüldü**: `GatewayController`'ın 15 ucunun **her biri** gövdesinde bir
yetki kontrolü çağırıyor. Yani **bugün bir açık yok** — denetim de bunu
söylüyordu ("yapı gelecekteki açığı davet ediyor").

Bu durumda 1.447 satırlık en karmaşık controller'ı yeniden yapılandırmak,
**var olmayan bir açığı kapatmak için gerçek bir regresyon riski** almak
olurdu. Üstelik bu oturumda uygulamayı tıklayarak doğrulama imkânı yok.

**Alınan yol:** aynı korumayı test seviyesinde kurmak.
`ApiControllerConventionTests`'e iki test eklendi:

1. **Her uç ya `[Authorize]` taşır ya da gerekçesiyle "bilerek açık"
   listesindedir.** Yeni bir uç kimlik doğrulaması olmadan eklendiğinde test
   kırılıyor; geliştirici ya özniteliği eklemek ya da listeye **gerekçe
   yazmak** zorunda kalıyor. Unutmak artık sessiz değil.
2. **`GatewayController`'ın her ucu gövdesinde yetki çağırır.** Sınıf
   `[AllowAnonymous]` olduğu için öznitelik koruması yok; tek savunma o çağrı.

Kazanılan: aranan "varsayılan kapalı" özelliği. Ödenmeyen: regresyon riski.

Filtre refactor'ü tamamen iptal değil — `GatewayController`'ı bölme işiyle
(BACK-004) **birlikte** yapılmalı, tek başına değil.

### Konvansiyon testinin ilk avı: `DockerController.StreamLogs`

Test yazılır yazılmaz dört uç yakaladı. Üçü benim listedeki metot adı
hatalarımdı. Dördüncüsü gerçek bir ödünleşim:

`GET /api/docker/stream/{jobId}` kimlik doğrulaması **yapmıyor**. Kodda
gerekçesi yazılı ve öncülü **doğru**: akış `EventSource` ile tüketiliyor
(`DockerSandboxPanel.tsx:129`) ve EventSource `Authorization` başlığı
gönderemiyor. Onun yerine sunucu üretimi GUID `jobId` bir **capability URL**
görevi görüyor.

**Ama öncül eksik.** EventSource `Authorization` gönderemez, fakat **cookie
gönderebilir** — ve bu uygulamada kimlik zaten `httpOnly` cookie'de. CORS
politikası da `AllowCredentials()` taşıyor (doğrulandı).

Yani çözüm iki satır:

```ts
new EventSource(url, { withCredentials: true })   // DockerSandboxPanel.tsx:129
```
```csharp
[Authorize]                                        // DockerController.StreamLogs
```

**Neden bu oturumda YAPILMADI:** Docker sandbox akışı uçtan uca
çalıştırılamadı (iş başlatmak Docker + gerçek bir derleme gerektiriyor).
Bu oturumda iki kez, doğrulanmadan "çalışıyor" sanılan şeyin ölü olduğu
görüldü (HIBP kontrolü, kurtarma kodları). Aynı hatayı üçüncü kez yapmamak
için değişiklik **uygulanmadı, hazır hâlde kaydedildi**.

**Risk değerlendirmesi:** capability URL sızabilir (proxy logu, tarayıcı
geçmişi, `Referer`) ve akış kullanıcının DDL'ini içerebilen derleme loglarını
yayıyor. Şiddet MEDIUM. Sandbox akışı denenebildiği anda uygulanmalı.

### Neden bu sıra

1 ve 2 **bounded ve test edilebilir** — bitip bitmediği belli. 3 yapısal ama
1.447 satırlık en karmaşık controller'a dokunuyor; test desteğiyle yapılmalı.
4 altyapı kararı, kod tek başına yetmiyor. 5 bir proje, bir görev değil.

### Zaten kapatılanlar (2026-09-09)

JWT fallback anahtarı · Mermaid XSS · sahte jeton özelliği · denetimsiz keyfi
SQL · cross-site CSRF · jeton iptali · hesap kilitleme · parola uzunluğu ·
`Billing` rol tuzağı · `[JsonIgnore]` · `Quote()` sınırlayıcı kaçırma

---

# 2. Üretime hazırlık: 79 → 90

| # | İş | Neden | Efor |
|---|---|---|---|
| 1 | **`docker-compose.prod.yml`'i gerçekten çalıştır** | Yazıldı ama **hiç denenmedi.** Denenmemiş bir dağıtım tanımı, tanım değildir | S |
| 2 | **Control DB yedeği + geri yükleme tatbikatı** | Vault **kullanıcının** DB'sini koruyor; Namines'in kendi kayıtlarının yedeği ayarlanmamış | M |
| 3 | **Alarm (alerting)** | OpenTelemetry + Prometheus kurulu, metrik akıyor — ama kimse uyarılmıyor | M |
| 4 | **RPO / RTO tanımla** | "Ne kadar veri kaybını göze alıyoruz, ne kadar sürede ayağa kalkmalıyız" yazılı değil | S |
| 5 | **İleriye uyumlu migration kuralı** | Geri alma bugün mümkün değil: eski sürüm yeni şemaya çarpar. Kural: kolon ekle, hemen silme; iki aşamalı yeniden adlandırma | M |

### En kritik olan 1. madde

`docker-compose.prod.yml` ve `deploy/URETIM-CALISTIRMA.md` denetim sonucunda
yazıldı. İkisi de **doğru görünüyor** ama hiçbiri çalıştırılmadı. Bu deponun
kendi dersi burada geçerli:

> "Çalışıyor görünüyor" ile "gerçekten çalışıyor" farklı şeyler. (`AGENTS.md`)

---

# 3. Kod kalitesi: 78 → 88

| # | İş | Etki | Efor |
|---|---|---|---|
| 1 | **`frontend` store testleri** | Tek en büyük kaldıraç. `useSchemaStore` (804), `useProjectHistoryStore` (711), `templates.ts` (1.942) — üçü de **saf TypeScript**, React gerekmeden test edilir | L |
| 2 | **128 lint hatasını temizle, sonra CI'a ekle** | **Sıra önemli:** şimdi eklersen build kırmızı olur ve ekip kapatmayı öğrenir | L |
| 3 | **Tek E2E akışı (Playwright)** | `AGENTS.md`'nin kendi dersi: "857 test yeşilken uygulama hiç başlamıyordu" — bunu yakalayan tek test türü | M |
| 4 | **Üç dev dosyayı böl** | Gateway 1.447 · Scaffolder 1.760 · AIPreferencesModal 1.521. `Generators/Eject/` altında bölme deseni **zaten var** | M×3 |
| 5 | **Docker.DotNet sürüm çatışması** | Bu oturumda gerçekten patladı (`TypeLoadException`), `Lazy<T>` ile geçici kurtarıldı — çatışma duruyor | M |

### Asimetri kaydı

Aynı depoda iki standart var ve ikisi de diğerinin iyi olduğu yerde kötü:

| | `frontend/` | `services/desk/` |
|---|---|---|
| Test | **0** | 99 |
| Ayrı `tsc --noEmit` | ❌ | ✅ |
| Ham `fetch` kullanımı | Var | Yok |

Karşılıklı öğrenme en ucuz kazanç.

---

# 4. Ürün ürün ne yapmalı

## Namines Desk — **en acil, en ucuz**

Bytebase'in **ücretsiz** katmanında olup Desk'te olmayan üç şey:

> **sorgu geçmişi · kaydedilmiş sorgular · veri dışa aktarma**

Üçü de küçük iş, üçü de aynı altyapıyı paylaşıyor. Bunlar "eksik özellik"
değil **temel beklenti** — demoda ilk sorulacak soru.

| İş | Efor | Backlog |
|---|---|---|
| Sorgu geçmişi | S | B-21 |
| Kaydedilmiş sorgular (ekip paylaşımlı — burada farklılaşabiliriz) | S | B-22 |
| CSV/JSON dışa aktarma + **`CanExport` izni** | M | B-44 |
| Otomatik tamamlama | M | — |
| Klavye kısayolları / komut paleti | M | — |

**Güvenlik notu:** dışa aktarma **ayrı bir izin** olmalı. Okuma yetkisi olan
herkesin tüm tabloyu indirebilmesi, veri sızıntısının en sessiz yoludur.

## Namines Vault — **en güçlü varlık, yeterince anlatılmıyor**

| İş | Neden | Efor |
|---|---|---|
| **Bytebase'in geri almasının doğrulama yapıp yapmadığını öğren** | Ürünün ana mesajı buna bağlı. "Doğrulamıyor" ise mesaj budur; "doğruluyor" ise farklılaştırıcı listesinden çıkar | XS |
| Kısmi (tek tablo) geri yükleme | `-Fc` formatı **zaten bunun için** seçilmiş, kodda yazılı | M |
| Yedeği arka plan işine taşı (202 Accepted) | 50 GB'lık DB'de istek proxy zaman aşımına düşer; kayıt zaten `Running` durumuyla açılıyor | M |
| Çoklu anahtar desteği | Bugün rotation **manuel** — eski anahtar saklanmak zorunda | M |
| MSSQL/Oracle | ❌ **Mimari sınır, kapatılmayacak.** `BACKUP DATABASE TO DISK` ve `expdp` dosyayı sunucunun kendi diskine yazar | — |

**Bugün doğrulanmış:** PostgreSQL, MySQL, MariaDB — üçünde de
yedek → veriyi boz → geri yükle → bağımsız istemciyle kontrol.

## Namines Ground — **yarışmayı bırak**

Supabase'in **ücretsiz** planı (500 MB, 2 proje) Ground v1'in üstünde ve
arkasında yıllarca altyapı yatırımı var. Bu savaş bu ekip boyutunda kazanılmaz.

**Öneri — bir ürün kararı, kod kararı değil:**

| Bugün | Olması gereken |
|---|---|
| "Kendi Supabase'imiz" | **"5 dakikada dene" zemini** |
| Üretim barındırma iddiası | Üretim için kullanıcıyı **bağla**: Neon (✅ canlı doğrulandı), Supabase (❌ yapılacak) |
| Altyapıya yatırım | Bağlayıcılara yatırım |

Konumlanma: *"Platformunu değiştirme, üstüne yönetişim koy."*

`IDatabaseProvider` soyutlaması bunu **zaten destekliyor** — Neon sağlayıcısı
örnek teşkil ediyor.

## Namines (çekirdek + frontend)

| İş | Neden | Efor |
|---|---|---|
| Store testleri | Kod tabanının %31'i doğrulanmıyor | L |
| Demo → hesap geçişinde işi koru | Hunideki en büyük delik: kullanıcı ikna olduğu anda sıfırdan başlamak zorunda kalıyor | M |
| **Mobil hedefini açıkça yaz** | Canvas mobilde çalışmaz, Desk çalışır. Bunu yazmak, her şeyi yarım yamalak duyarlı yapmaktan dürüst | XS |
| İki uygulamanın dilini birleştir | `frontend` İngilizce, `desk` Türkçe | M |
| Bundle: `mermaid` + `sql.js` dinamik import | **Önce ölç** | S |

---

# 5. Rekabet gerçeği — kararları buna göre ver

| Rakip | Ücretsiz katman | İlk ücretli |
|---|---|---|
| **Bytebase** (baş rakip) | **20 kullanıcı, 10 instance — süresiz** | $20 / kullanıcı / ay |
| **Atlas** | Sınırlı (4 motor) | **$9 / geliştirici / ay** |
| **Supabase** | 500 MB, 2 proje | $25 / ay |

### Ayakta kalan gerçek farklarımız

1. **Risk değerlendirmesi + onay iş akışı temel üründe** — Bytebase ikisini de
   **Enterprise'a** (fiyat sorulacak) saklıyor. **En net ticari fark.**
2. Düzenlenebilir görsel şema tuvali — Bytebase'de yok, Atlas yalnızca görüntüleme
3. Otomatik REST API + tablo bazlı izin — ne Bytebase'de ne Atlas'ta
4. Eject / kod üretimi / TypeScript SDK — kategoride benzeri yok
5. Geri yüklenebilirlik kanıtı — ❓ doğrulanmalı

### Geri çekilen iddia

**MCP sunucusu farklılaştırıcı değil** — Bytebase onu ücretsiz katmanda veriyor.

### Fiyat gerçeği

Bytebase'in ücretsiz katmanı, küçük ekipler için fiyat rekabetini fiilen
kapatıyor. **Düşük uçta fiyatla kazanılamaz.** Kazanılacak yer: rakibin
Enterprise'a sakladığını erişilebilir fiyata sunmak.

---

# 6. Önerilen sıra

| Sıra | Ne | Neden önce |
|---|---|---|
| **1** | Güvenlik: HIBP → MFA → Gateway filtresi | Kullanıcı verisi riski her şeyden önce gelir |
| 2 | Desk: sorgu geçmişi + kaydedilmiş sorgular + export | Rakibin bedava verdiği şey; en görünür açık |
| 3 | Üretim yığınını gerçekten çalıştır + DR tatbikatı | Denenmemiş dağıtım tanımına güvenilmez |
| 4 | `frontend` store testleri + tek E2E | Regresyonlar bugün yalnızca kullanıcı tarafından fark ediliyor |
| 5 | Ground kararı + Supabase bağlayıcısı | Strateji kararı verilmeden kod yazılmamalı |

**Bu belge yazıldığı gün 1. maddeden başlandı.**
