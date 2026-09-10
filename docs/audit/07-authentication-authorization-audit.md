# 07 — Kimlik doğrulama ve yetkilendirme denetimi

> ## ✅ DURUM (2026-09-09)
> | Bulgu | Durum |
> |---|---|
> | AUTH-001 Jeton iptali yok | ✅ `sstamp` claim + `SecurityStampValidation` + `POST /auth/revoke-all-sessions` |
> | AUTH-002 Kaba kuvvet / kilitleme | ✅ Kilitleme açıldı (5 deneme / 15 dk) + login `sensitive` rate limit'e alındı |
> | AUTH-003 MFA yok | ✅ **TOTP + kurtarma kodları** (10.09.2026, canlı doğrulandı) |
> | AUTHZ-001 `Billing` enum tuzağı | ✅ `OrgRoleExtensions.IsAtLeast` eklendi, testlerle sabitlendi |
> | AUTHZ-002 IDOR taraması | ✅ Açık bulunamadı (değişiklik gerekmedi) |
> | AUTHZ-003 Gateway izin modeli | ✅ Zaten güçlüydü |
> | AUTHZ-004 Cross-site CSRF | ✅ `CsrfProtectionMiddleware` + frontend başlığı |

## Kimlik doğrulama — mevcut tasarım

| Öğe | Durum | Konum |
|---|---|---|
| Şema | JWT Bearer | `Program.cs` |
| **MFA** | **TOTP + 8 kurtarma kodu** | `AuthController` `mfa/*` |
| **Jeton iptali** | **`sstamp` claim'i, 60 sn önbellek** | `SecurityStampValidation.cs` |
| Jeton taşıyıcı | `Authorization` header **veya** `namines_token` httpOnly cookie | `Program.cs:196-205` |
| `localStorage`'da JWT | **YOK** — bilinçli | `frontend/store/useAuthStore.ts:74` |
| Issuer / Audience / Lifetime / Signing key doğrulaması | Dördü de **açık** | `Program.cs:182-190` |
| Parola hash | ASP.NET Identity (PBKDF2) | `AddEntityFrameworkStores<AuthDbContext>` |
| Parola politikası | **12 karakter + HIBP sızdırılmış-parola kontrolü**; karmaşıklık kuralı bilerek yok (NIST 800-63B) | `Program.cs`, `PwnedPasswordValidator.cs` |

**Güçlü yan:** JWT'nin `localStorage`'a yazılmaması ve cookie fallback'i,
XSS'in doğrudan jeton hırsızlığına dönmesini engelliyor. Bu tercih kodda
gerekçesiyle yazılmış — kaza değil, karar.

---

## AUTH-001 — Jeton iptali (revocation) yok

### Finding
Çıkarılmış bir JWT'yi süresi dolmadan geçersiz kılmanın yolu yok.

### Current State
`AuthController.cs:510` bir `logout` ucu var; cookie'yi silmesi bekleniyor.
Ama jeton **kendisi** geçerli kalır: kopyalanmış bir jeton (XSS öncesi
çalınmış, log'a düşmüş, proxy'de görülmüş) süresi dolana kadar çalışır.

Depoda bir `RevokedTokens` / `TokenVersion` tablosu **yok** (34 `DbSet` tarandı).

### Problem
"Çalınmış jeton revoke edilebilir mi?" sorusunun cevabı **hayır**. Bir olay
anında yapılabilecek tek şey JWT imzalama anahtarını değiştirmek — ki bu
**herkesi** çıkışa zorlar.

### Risk
Sızıntı sonrası müdahale imkânsız; uyum (compliance) gereksinimi olan bir
müşteri için satış engelleyici.

### Severity **MEDIUM** (kurumsal satışta HIGH)

### Recommendation
En ucuz doğru çözüm: kullanıcı kaydında bir `SecurityStamp` / `TokenVersion`
tut, JWT'ye claim olarak koy, doğrulamada karşılaştır. ASP.NET Identity'nin
`SecurityStamp`'i **zaten var** — yalnızca claim'e eklenip kontrol edilmesi
gerekiyor. Parola değişimi ve "tüm oturumları kapat" bunu otomatik çözer.

### Effort M · ### Priority P1

---

## AUTH-002 — Kaba kuvvet / hesap kilitleme

### Finding
`Namines.API` genelinde `Lockout` kelimesi **hiç geçmiyor** (tarandı, 0 eşleşme).
Yani ASP.NET Identity'nin hesap kilitleme mekanizması yapılandırılmamış ve
varsayılan olarak `SignInManager.PasswordSignInAsync(..., lockoutOnFailure)`
çağrılmadıkça devreye girmez.

### Current State
`sensitive` politikası kullanıcı kimliğine göre bölünüyor; **giriş yapmamış**
bir çağıran için IP'ye düşüyor (`Program.cs:232-234`). Yani dağıtık bir kaba
kuvvet (IP başına 5/dk) hâlâ mümkün.

### Severity **MEDIUM**

### Recommendation
1. ASP.NET Identity lockout'u aç: `MaxFailedAccessAttempts = 5`,
   `DefaultLockoutTimeSpan = 15 dk`.
2. `login` ucunu `sensitive` politikasına **e-posta bazlı** partition ile bağla
   (IP değil e-posta — dağıtık saldırıyı da yakalar).

### Effort S · ### Priority P1

---

## AUTH-003 — MFA yok

Kayıt/giriş akışında çok faktörlü doğrulama yok. Bir veritabanı yönetim
ürünü için bu, kurumsal segmentte **beklenen** bir özellik.

**Severity:** LOW (bugünkü hedef kitle için) / HIGH (kurumsal satışta)
**Recommendation:** ASP.NET Identity TOTP sağlayıcısı zaten pakette
(`AddDefaultTokenProviders`). Uç + UI işi.
**Effort:** M · **Priority:** P2

### ✅ Yapıldı (10.09.2026)

Yeni uçlar — `AuthController`:

| Uç | Ne yapıyor |
|---|---|
| `POST /api/auth/mfa/setup` | Anahtar üretir, `otpauth://` URI'si döner. **Etkinleştirmez** |
| `POST /api/auth/mfa/enable` | Kodu doğrular, açar, **8 kurtarma kodu** döner (bir kez) |
| `POST /api/auth/mfa/disable` | **Geçerli kod zorunlu** — çalınmış jeton MFA'yı kapatamaz |
| `GET /api/auth/mfa/status` | Açık mı, kaç kurtarma kodu kaldı |

`login` artık isteğe bağlı `twoFactorCode` alıyor.

#### Alınan tasarım kararları

**Ayrı bir "MFA jetonu" adımı yok.** İki aşamalı akışta sunucu, parolası
doğrulanmış ama MFA'sı tamamlanmamış kullanıcı için ikinci bir jeton türü
üretmek zorunda kalır — o jeton da çalınabilir, saklanmalı, süresi
yönetilmeli. Kodu aynı istekte almak o yüzeyi tamamen ortadan kaldırıyor.

**MFA sorgusu paroladan SONRA.** Önce sorulsaydı, parolayı bilmeyen biri bile
bir hesapta MFA açık olup olmadığını öğrenirdi — hedef seçmeye yarayan bilgi.

**Yanlış MFA kodu kilitleme sayacına yazılıyor.** Aksi hâlde parolası sızmış
bir hesapta saldırgan altı haneyi sınırsız deneyebilirdi; bir milyon olasılık +
sınırsız deneme = kırılmış MFA.

**Kurulum, doğrulanana kadar etkinleştirmiyor.** Doğrulamadan açmak,
kullanıcıyı kuramadığı bir uygulamaya bağlayıp hesabından kilitleyebilirdi.

**Kapatma geçerli kod istiyor.** Yalnızca oturum yeterli olsaydı MFA kendi
kendini savunamazdı.

#### ⚠️ Canlı denemede yakalanan gerçek hata

Kurtarma kodları **çalışmıyordu**. Sebep: `NormalizeCode` tireleri siliyordu
(TOTP için doğru — uygulamalar kodu "123 456" gösteriyor), ama Identity
kurtarma kodlarını `xxxxx-xxxxx` biçiminde üretip **aynen saklıyor**.

Sonuç, kurtarma kodlarının var olma sebebinin tam tersiydi: **telefonunu
kaybeden kullanıcı hesabına hiç giremiyordu.**

Birim testleri geçiyordu. Hatayı yalnızca gerçek bir TOTP üretip uçtan uca
akışı çalıştırmak gösterdi. Düzeltme: TOTP için tam normalleştirme, kurtarma
kodu için yalnızca `Trim()`.

Regresyon testi: `MfaTests.Kurtarma_kodu_bicimi_NormalizeCode_ile_BOZULUR`.

#### Canlı doğrulama — 12 adım (2026-09-10)

Gerçek TOTP kodu üretilerek (RFC 6238, standart kütüphane, harici bağımlılık yok):

| # | Adım | Sonuç |
|---|---|---|
| 1-2 | Kayıt + kurulum | ✅ anahtar ve `otpauth://` URI'si |
| 3 | Yanlış kodla etkinleştirme | ✅ reddedildi |
| 4 | Gerçek TOTP ile etkinleştirme | ✅ açıldı, 8 kurtarma kodu |
| 5 | Kodsuz giriş | ✅ 401 `requiresTwoFactor: true` |
| 6 | Yanlış kodla giriş | ✅ reddedildi |
| 7 | Doğru kodla giriş | ✅ jeton |
| 8 | **Kurtarma koduyla giriş** | ✅ jeton |
| 9 | Aynı kurtarma kodu ikinci kez | ✅ reddedildi (tek kullanımlık) |
| 10 | Durum | ✅ açık, **7 kod kaldı** (tüketildiği kanıtı) |
| 11 | Gerçek kodla kapatma | ✅ |
| 12 | Kapandıktan sonra kodsuz giriş | ✅ jeton |

**Kalan iş:** arayüz. Uçlar hazır ve doğrulanmış, ama Desk/frontend'de MFA
kurulum ekranı **yok**. Kullanıcı bugün bu özelliği ancak API'yi doğrudan
çağırarak kullanabilir.

---

# Yetkilendirme (RBAC)

## Rol modeli — İYİ TASARLANMIŞ

`backend/Namines.Core/Models/Auth/Organization.cs:14-21`

```csharp
Viewer  = 0,  // salt-okunur
Editor  = 1,  // şema düzenler, change request açar/oylar
Admin   = 2,  // + üye yönetir
Owner   = 3,  // + faturalama, org silme
Billing = 4   // yalnızca faturalama (sıralamaya dahil DEĞİL)
```

`Billing`'in sayısal sıralamanın **dışında** tutulup yorumla işaretlenmesi
dikkat çekici: `role >= OrgRole.Admin` gibi bir karşılaştırma Billing'i
yanlışlıkla Admin'in üstüne koyardı. Bu tuzağın farkında olunması iyi bir
işaret — ama tuzak **hâlâ orada**.

### AUTHZ-001 — `Billing` numaralandırma tuzağı

**Risk:** Bugün doğru kullanılıyor olabilir; yarın biri `>= Admin` yazarsa
`Billing` (4) sessizce Owner'dan (3) fazla yetki alır.

**Recommendation:** `Billing`'i enum'dan çıkarıp ayrı bir bayrak
(`IsBillingContact`) yap, ya da değerini `100` gibi sıralamadan açıkça kopuk
bir sayıya taşı ve `>=` karşılaştırmalarını yasaklayan bir yardımcı metot ekle.

**Severity:** MEDIUM · **Effort:** S · **Priority:** P1

### ✅ Yapıldı — üçüncü seçenek
`OrgRoleExtensions.IsAtLeast(role, minimum)` eklendi
(`Namines.Core/Models/Auth/Organization.cs`). Hiyerarşi açık bir dizi olarak
tanımlı; `Billing` o dizide olmadığı için **her eşikte `false`** dönüyor.

**Enum değeri değiştirilmedi** — `Billing = 4` veritabanında saklanan mevcut
satırların değeri; 100'e taşımak bir veri migration'ı gerektirirdi ve kazanç
buna değmezdi. Bunun yerine güvenli karşılaştırma tek bir yerde toplandı.

Tarama sonucu: depoda bugün **hiçbir sıralama karşılaştırması yok** (hepsi
`is Editor or Admin or Owner` biçiminde küme üyeliği). Yani tuzak henüz
tetiklenmemişti; yardımcı, ilk sıralama karşılaştırmasını yazacak kişi için.

Testler: `SecurityHardeningTests.Billing_rolu_hicbir_yetki_esigini_gecmez` ve
`Hiyerarsi_icindeki_roller_dogru_siralaniyor`.

---

## AUTHZ-002 — Sahiplik kontrolü dağılımı (IDOR taraması)

Her controller için "uç sayısı" ve "sahiplik kontrolü sayısı" ölçüldü:

**Kontrolü yoğun olanlar (sağlıklı):**

| Controller | Uç | Sahiplik kontrolü |
|---|---|---|
| `GatewayKeyController` | 10 | 13 |
| `BranchController` | 10 | 11 |
| `VaultController` | 10 | 11 |
| `ChangeRequestController` | 8 | 10 |
| `TeamController` | 6 | 10 |
| `GroundController` | 6 | 9 |

Bu controller'lar `GetRoleAsync(projectId, userId)` çağırıp rolü karşılaştırıyor
(ör. `VaultController.Restore`: `!= OrgRole.Owner` → `Forbid()`). **IDOR
bulunamadı.**

**Kontrolü olmayanlar — teker teker değerlendirildi:**

| Controller | Uç | Değerlendirme |
|---|---|---|
| `CompileController` | 10 | Saf dönüşüm, kalıcı durum yok → **IDOR değil**, ama DoS açık (BACK-002) |
| `SchemaController` | 7 | AI şema üretimi, girdiden çıktı → **IDOR değil** |
| `DatabaseExecutorController` | 2 | Bağlantıyı kullanıcı veriyor → sahiplik kontrol edilecek nesne yok. **Asıl sorun bu** (SEC-001) |
| `DbIntrospectController` | 2 | Aynı model, salt-okunur → kabul edilebilir ama denetim kaydı yok |
| `MigrationController` | 3 | Saf dönüşüm |
| `StripeWebhookController` | 1 | İmza doğrulaması ile korunuyor (kod yorumunda açık) |
| `GithubWebhookController` | 1 | Aynı |

**Sonuç: klasik IDOR/BOLA açığı bulunamadı.** Sahiplik kontrolü gereken her
yerde var. Boşluk, sahipliğin **kavram olarak yok** olduğu executor yolunda.

---

## AUTHZ-003 — Gateway API anahtarı izin modeli (GÜÇLÜ YAN)

`GatewayApiKey` üzerinde `CanRead` / `CanWrite` **ayrı**; ayrıca
`GatewayTablePermission` ile **tablo başına** `CanRead`/`CanWrite`.
Anahtarın kendisi saklanmıyor — `Prefix` indeksli, gerisi hash.
Her erişim `GatewayAuditEntry`'ye yazılıyor.

Kodda `CanWrite`'ın neden ayrı olduğu açıklanmış: *"Yazma yetkisinden AYRI,
çünkü ondan daha geniş"* (`GatewayApiKey.cs:42`).

Bu, denetimin aradığı "authenticated ≠ database administrator" ayrımının
**doğru uygulanmış** hâli. Sorun şu ki bu titizlik Gateway'de var, executor'da yok.

---

## Özet tablo — denetim sorularına cevaplar

| Soru | Cevap |
|---|---|
| Token nerede tutuluyor? | httpOnly cookie (tercih edilen) veya Authorization header |
| localStorage'da JWT var mı? | **Hayır** — açıkça engellenmiş |
| Refresh token güvenli mi? | **Refresh token yok** — tek JWT, süresi dolunca yeniden giriş |
| Logout session'ı sonlandırıyor mu? | Cookie'yi siliyor; **jeton geçerli kalıyor** (AUTH-001) |
| Çalınmış token revoke edilebilir mi? | **Hayır** (AUTH-001) |
| CSRF | Varsayılan `SameSite=Lax` ile korunuyor; **`Auth:CrossSiteCookie=true` ise korunmuyor** (AUTHZ-004) |
| CORS | Allowlist ile (`WithOrigins(allowedOrigins)`), `AllowAnyOrigin` yok ✅ |
| IDOR | Aranan yerlerde bulunamadı ✅ |

---

## AUTHZ-004 — Cross-site dağıtımda CSRF koruması kalmıyor

### Finding
Auth cookie'si varsayılan olarak `SameSite=Lax` — bu, CSRF'e karşı makul bir
koruma. Ama `Auth:CrossSiteCookie=true` verildiğinde `SameSite=None` oluyor ve
**başka hiçbir CSRF koruması devreye girmiyor** (anti-forgery token yok).

### Location
`backend/Namines.API/Controllers/AuthController.cs:488-505`

```csharp
Secure   = crossSite || Request.IsHttps,
SameSite = crossSite ? SameSiteMode.None : SameSiteMode.Lax,
```

### Current State
Kod, cross-site ayarının **neden** gerektiğini doğru açıklıyor (Vercel + Railway
gibi ayrı domain'lerde `Lax` cookie XHR'de gönderilmez). `Secure` bayrağının
`None` ile zorunlu olduğu da doğru kavranmış. Sorun eksik anlayış değil, eksik
telafi.

### Problem
`SameSite=None` ile tarayıcı cookie'yi **her siteden** gönderir. CORS
allowlist'i tarayıcının **cevabı okumasını** engeller ama **isteğin gitmesini**
engellemez. Yani `POST /api/vault/{id}/backups/{id}` gibi yan etkili bir uç,
kötü niyetli bir sayfadan tetiklenebilir; saldırgan cevabı göremez ama işlem
gerçekleşir.

Yıkıcı uçların çoğu ek bir onay istiyor (Vault geri yüklemesi veritabanı adını
yazdırıyor) — bu tesadüfi bir savunma ve her uçta yok.

### Risk
Cross-site dağıtımda (ki `.env.example`'daki Vercel/Railway senaryosu tam olarak
budur) kimliği doğrulanmış kullanıcı adına istem dışı yazma işlemleri.

### Severity
**HIGH** — yalnızca `Auth:CrossSiteCookie=true` iken; o yapılandırma önerilen
dağıtım şekli olduğu için gerçekçi.

### Recommendation
Üç seçenek, tercih sırasıyla:
1. **Aynı site altında dağıt** (`app.namines.com` + `api.namines.com` →
   `SameSite=Lax` + `Domain=.namines.com` yeter). CSRF sorunu tamamen kalkar.
2. Cross-site şartsa **anti-forgery token** ekle (ASP.NET'in
   `IAntiforgery`'si; çift-gönderim cookie deseni).
3. En azından durum değiştiren uçlarda **özel bir header zorunlu kıl**
   (`X-Namines-Request: 1`). Basit form-tabanlı CSRF'i keser; preflight
   gerektirdiği için CORS allowlist'i devreye girer.

### Effort
S (seçenek 1 veya 3) / M (seçenek 2)

### Priority
P0 (cross-site dağıtım yapılıyorsa) / P2 (aynı site ise)

### ✅ Yapıldı — üçüncü seçenek (özel başlık)
`Namines.API/Middleware/CsrfProtectionMiddleware.cs`. Kural: **güvenli olmayan
metot + auth cookie'si + `Authorization` başlığı yok** ise
`X-Namines-Request` başlığı zorunlu.

Neden bu seçenek: durumsuz, ek uç gerektirmiyor ve API tüketicilerini (SDK,
CLI, MCP, Desk) hiç etkilemiyor — onlar `Authorization` başlığıyla geliyor ve
tarayıcı o başlığı kendiliğinden eklemediği için CSRF yüzeyleri yok.

Neden işe yarıyor: bir HTML formu özel başlık gönderemez; `fetch` ile göndermek
isteği preflight'a zorlar ve preflight sunucunun CORS allowlist'ine takılır.
Yani başlığın varlığı, isteğin izinli bir origin'den geldiğinin **tarayıcı
tarafından doğrulanmış** kanıtı.

Frontend tarafı: `frontend/lib/csrf.ts` + axios varsayılan başlığı + 5 ham
`fetch` çağrısı (`DbConnectionPanel`, `DbPushModal`, `DockerSandboxPanel`,
`sseSchemaStream`, `useAuthStore` logout).

Middleware pipeline'da **CORS'tan sonra, Authentication'dan önce**: preflight'ın
CORS tarafından cevaplanması gerekiyor, ama cookie'nin varlığını görmek için
jetonun çözülmesini beklemeye gerek yok.

8 test: `SecurityHardeningTests` içinde yazma metotları, güvenli metotlar,
Bearer istisnası ve cookiesiz istek senaryoları.

---

## Doğrulanan iyi uygulamalar

- `logout`, cookie'yi **yazıldığı attribute'ların aynısıyla** siliyor — aksi
  hâlde tarayıcı silmezdi ve kod bunu yorumda açıklıyor.
- `Secure = crossSite || Request.IsHttps` — HTTP üzerinde geliştirmeyi
  engellemeden üretimde doğru davranıyor.
- Cookie ömrü 7 gün, `Path=/`.
