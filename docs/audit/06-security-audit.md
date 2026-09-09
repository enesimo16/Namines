# 06 — Güvenlik denetimi (OWASP)

> ## ✅ DURUM: SEC-001 … SEC-008 DÜZELTİLDİ (2026-09-09)
>
> Bu rapor bulguları **bulundukları hâliyle** koruyor; her bulgunun sonunda
> ne yapıldığı yazıyor. Özet:
>
> | Bulgu | Durum |
> |---|---|
> | SEC-001 Denetimsiz keyfi SQL | ✅ `SqlExecutionAudit` tablosu + migration; her çalıştırma (başarısızlar dahil) kaydediliyor |
> | SEC-002 Ham sürücü hatası sızıntısı | ✅ `DbConnectionFailure` ortak sınıflandırıcısı; `GatewayKeyController`'daki private kopya kaldırıldı |
> | SEC-003 Sahte Personal Access Token | ✅ Ekran, state, handler ve `localStorage` verisi kaldırıldı; kullanıcı gerçek anahtar ekranına yönlendiriliyor |
> | SEC-004 Mermaid XSS | ✅ `securityLevel: 'strict'` |
> | SEC-005 JWT fallback anahtarı | ✅ Kapı `IsDevelopment()`'a çevrildi + fallback ile eşitlik kontrolü eklendi |
> | SEC-006 DNS rebinding | 🟡 Açık — altyapı kararı gerektiriyor (egress allowlist), kod düzeltmesi tek başına yeterli değil |
> | SEC-007 Zayıf parola politikası | ✅ 12 karakter + hesap kilitleme (5 deneme / 15 dk) |
> | SEC-008 `ConnectionString = null` tiyatrosu | ✅ Kaldırıldı |
> | AUTHZ-004 CSRF | ✅ `CsrfProtectionMiddleware` + frontend başlığı; 8 testle sabitlendi |
>
> Regresyon testleri: `backend/Namines.Tests/Services/SecurityHardeningTests.cs` (23 test).

Bulgular önem sırasına göre. Her biri kod okumasıyla doğrulandı; canlı istismar
denenmedi (bkz. [00-YONTEM-VE-KAPSAM.md](00-YONTEM-VE-KAPSAM.md)).

**Önce hakkını verelim:** bu depoda güvenlik ciddiye alınmış. SSRF koruması,
kimlik doğrulayıcı allowlist regex'i, PII redaksiyon zenginleştiricisi, global
exception middleware, kullanıcı-bazlı rate limit partition'ı, üretimde
fail-closed JWT anahtarı — hepsi var ve gerekçeleri koda yazılmış. Aşağıdaki
bulgular bu zeminin **üstünde** duran boşluklar.

---

## SEC-001 — `/api/executor/execute` ürünün kendi yönetişimini baypas ediyor

### Finding
Kimliği doğrulanmış herhangi bir kullanıcı, gövdede verdiği **herhangi bir
bağlantı dizesine** karşı **herhangi bir SQL betiğini** çalıştırabiliyor. Bu
yolun denetim kaydı yok, risk sınıflandırması yok, onay adımı yok.

### Location
- `backend/Namines.API/Controllers/DatabaseExecutorController.cs:42-57`
- `backend/Namines.Infrastructure/Services/DatabaseExecutorService.cs` → `ExecuteScriptAsync`
- Çağıran UI: `frontend/components/compile/DbPushModal.tsx:79`

### Current State
İstek gövdesi yalnızca `{ ConnectionString, Script, DbType }` taşıyor. Proje
kimliği YOK; dolayısıyla sahiplik kontrolü de yok (kontrol edecek bir nesne yok).
SSRF koruması ve `sensitive` rate limit (dakikada 5) uygulanıyor.

### Problem
Ürünün merkezî iddiası **yönetişimli şema değişikliği**: `ChangeRequest`, risk
sınıflandırması, onay politikası (`ChangeRequestApprovalPolicy`),
`ChangeRequestAuditLog`. Bu uç bunların **tamamının yanından geçiyor**.
`DROP TABLE` yazan bir betik, "Safe/Risky" ayrımına girmeden, kimseye sorulmadan
ve **hiçbir yere kaydedilmeden** çalışıyor.

Karşılaştırma aynı depodan: `GatewayKeyController.cs:382+` bir bağlantıyı
KAYDETMEDEN önce doğruluyor, hatayı sınıflandırıyor; `ChangeRequestController`
her kararı denetime yazıyor. Yani ekip bu standardı biliyor; bu uç ondan sapıyor.

### Risk
- **İçeriden kötüye kullanım / kaza:** üretim veritabanında geri alınamaz DDL,
  sonradan "kim çalıştırdı" sorusuna cevap verecek kayıt olmadan.
- **Sunucunun kimliğiyle taciz:** uygulama, saldırganın IP'sini gizleyen anonim
  bir SQL istemcisine dönüşüyor (rate limit ile 5/dk'ya sınırlı).

### Severity
**HIGH** — yönetişim baypası ve denetim yokluğu birlikte.

### Impact
Veri kaybı; olay sonrası adli inceleme imkânsız; ürünün "güvenle yönet"
vaadinin kendi API'siyle çelişmesi.

### Recommendation
1. Uca **denetim kaydı** ekle: kullanıcı, hedef host (parola HARİÇ), betiğin
   hash'i ve ilk N karakteri, çalışan ifade sayısı, sonuç — `GatewayAuditEntry`
   ile aynı desen.
2. Betiği mevcut **risk sınıflandırıcısından** geçir; `Risky` çıkanlarda
   ChangeRequest akışına yönlendir ya da en azından hedef veritabanı adını
   yazdırarak onay iste (Vault geri yüklemesinde zaten yapılan şey).
3. Bağlantıyı kullanıcının **kayıtlı projesine bağla** (projectId + sunucuda
   çözülen bağlantı); gövdeden serbest bağlantı almayı kaldır.

### Implementation Approach
`DbPushModal` zaten bir projeden çalışıyor; `projectId` göndermesi küçük bir
değişiklik. Sunucu tarafında `GatewayKeyController`'ın bağlantı çözme kodu
yeniden kullanılabilir.

### Effort
M

### Priority
P0

### ✅ Yapıldı
- `SqlExecutionAudit` varlığı + `SqlExecutionAudits` DbSet + migration
  `20260909184925_AddSqlExecutionAudit`.
- `DatabaseExecutorController` her çalıştırmayı yazıyor: kullanıcı, hedef host
  ve veritabanı (**parola hariç**), betiğin SHA-256 hash'i ve ilk 500 karakteri,
  yıkıcı anahtar kelime bayrağı, sonuç, çalışan ifade sayısı.
- **Başarısız çalıştırmalar da kaydediliyor** — bir saldırı denemesi tam olarak
  başarısız çalıştırmalar dizisi gibi görünür.
- Kayıt yazılamazsa işlem geri alınmıyor (hedefte çoktan uygulandı) ama
  `LogError` ile sesli hata veriyor.

**Yapılmayan:** bağlantıyı zorunlu olarak projeye bağlama. Bu, "henüz projeye
bağlanmamış bir şemayı kendi veritabanıma bas" akışını kırardı — ürün kararı,
güvenlik kararı değil. `ProjectId` **isteğe bağlı** alan olarak eklendi;
verildiğinde kayıt projeye de bağlanıyor.

---

## SEC-002 — Sürücünün ham hata mesajı istemciye dönüyor (bağlantı kâşifi)

### Finding
`ExecuteScriptAsync` başarısızlıkta `$"Connection error: {ex.Message}"` üretiyor
ve controller bunu doğrudan istemciye yazıyor.

### Location
- `backend/Namines.Infrastructure/Services/DatabaseExecutorService.cs` — `ExecuteScriptAsync`, iki `catch` bloğu
- `backend/Namines.API/Controllers/DatabaseExecutorController.cs:55`

### Current State
Npgsql / SqlClient / MySqlConnector bağlantı hatalarında hedef host, port ve
bazen sunucu sürümünü mesaja gömer. Bu metin olduğu gibi 400 gövdesine giriyor.

### Problem
Aynı depoda `GatewayKeyController.cs:415-427` bunu **açıkça yasaklıyor** ve
gerekçesini yazıyor: *"bu uç … rastgele hostlara karşı bir bağlantı kâşifine
dönerdi"*. `ClassifyConnectionFailure` tam da bunun için yazılmış. Executor o
korumadan yararlanmıyor.

### Risk
Kimliği doğrulanmış bir kullanıcı, uygulamayı ağ/servis keşfi için kullanabilir:
"bu host 5432'de PostgreSQL koşuyor mu?" sorusuna cevap veren bir oracle.

### Severity
**MEDIUM**

### Recommendation
`ClassifyConnectionFailure`'ı ortak bir yardımcıya taşı ve executor'da da kullan.
Ham `ex.Message` yalnızca korelasyon ID'siyle **loga** gitsin.

### Effort
S

### Priority
P1

### ✅ Yapıldı
`Namines.Core/Security/DbConnectionFailure.cs` oluşturuldu; `GatewayKeyController`
içindeki private kopya kaldırılıp buna bağlandı, executor da aynı sınıflandırıcıyı
kullanıyor.

**Bir ayrım korundu:** bağlantı KURULDUKTAN sonraki ifade hatalarında sürücü
mesajı hâlâ dönüyor (`DescribeStatementFailure`). Gerekçe kodda: çağıran hedefe
erişebildiğini zaten biliyor, dolayısıyla keşif değeri yok; buna karşılık
"42. ifadede sözdizimi hatası" bilgisi olmadan kullanıcı betiğini düzeltemez.

---

## SEC-003 — Sahte "Personal Access Token" özelliği

### Finding
Ayarlar ekranındaki kişisel erişim jetonu **tarayıcıda üretiliyor**, sunucuya
hiç gönderilmiyor, hiçbir yerde doğrulanmıyor. "Revoke" hiçbir şeyi iptal etmiyor.

### Location
`frontend/components/canvas/panels/AIPreferencesModal.tsx:471-497`

```ts
const rawToken = 'nam_pat_' + Array.from({ length: 32 },
  () => Math.floor(Math.random() * 16).toString(16)).join('');
// ...
localStorage.setItem('namines-api-tokens', JSON.stringify(updated));
```

### Problem
Bu bir hata değil, **tutulmayan bir güvenlik sözü**. Kullanıcı bir kimlik bilgisi
ürettiğine, sakladığına ve iptal edebildiğine inanıyor. Üçünün hiçbiri gerçek
değil. Ayrıca `Math.random()` kriptografik değil — jeton gerçek olsaydı tahmin
edilebilirdi.

Depoda **gerçek** bir API anahtarı sistemi zaten var: `GatewayKeyController`
(+ `GatewayApiKey`, `GatewayTablePermission`, `GatewayAuditEntry`). Yani bu ekran
onun sahte bir ikizi.

### Risk
Kullanıcı bu jetonu bir betiğe koyar, çalışmaz — en iyi ihtimalle güven kaybı.
Kötü ihtimalle jetonu "iptal ettim" sanarak gerçek bir sızıntıyı kapattığını
düşünür.

### Severity
**HIGH** — ürün bütünlüğü; teknik istismar değil.

### Recommendation
İki seçenekten biri, ortası yok:
- **(Önerilen)** Ekranı kaldır, kullanıcıyı gerçek anahtar ekranına yönlendir.
- Ya da ekranı gerçek uca bağla: jeton sunucuda üretilsin, yalnızca hash'i saklansın.

### Effort
S (kaldırma) / M (bağlama)

### Priority
P0

### ✅ Yapıldı — kaldırıldı
`handleGenerateToken`, `handleRevokeToken`, `ApiToken` arayüzü, ilgili state'ler
ve UI bloğu silindi. Yerine gerçek anahtar sistemine yönlendiren bir açıklama
kartı kondu. Ek olarak modal açılırken `localStorage`'daki `namines-api-tokens`
anahtarı **temizleniyor** — bırakılsaydı kullanıcı geçersiz jetonları görmeye
devam ederdi.

Yan kazanç: kullanılmayan `Plus`, `Copy`, `Trash2` importları ve
`copyToClipboard` yardımcısı da öldü, kaldırıldı.

---

## SEC-004 — Mermaid `securityLevel: 'loose'` + `dangerouslySetInnerHTML`

### Finding
Mermaid `loose` modda başlatılıyor ve üretilen SVG ham HTML olarak DOM'a basılıyor.

### Location
- `frontend/components/compile/MermaidPreview.tsx:18` — `securityLevel: 'loose'`
- `frontend/components/compile/MermaidPreview.tsx:53` — `dangerouslySetInnerHTML={{ __html: svgContent }}`

### Current State
Diyagram metni şemadan türüyor: tablo/kolon adları, yorumlar — yani **kullanıcı
ve AI tarafından kontrol edilen** metin.

### Problem
`loose` mod, Mermaid'in etiketlerdeki HTML'i sanitize etmemesi ve tıklama
işleyicilerine izin vermesi demek; `strict` (varsayılan) bunu engeller. Şemanın
paylaşılabilir olması (`/share/[token]`) bunu **saklı XSS**'e çeviriyor: bir
kullanıcının şeması, onu görüntüleyen başka bir kullanıcının tarayıcısında kod
çalıştırabilir.

### Risk
JWT `httpOnly` cookie'de olduğu için jeton doğrudan çalınamıyor — ama script aynı
origin'de ve kullanıcı adına istek atabilir (proje silme, anahtar oluşturma).

### Severity
**HIGH**

### Recommendation
`securityLevel: 'strict'` (veya `antiscript`). `loose` gerçekten gerekiyorsa
(tıklanabilir düğümler için) SVG'yi DOMPurify'dan geçir.

### Effort
XS

### Priority
P0

### ✅ Yapıldı
`securityLevel: 'strict'`. Gerekçe koda yazıldı (şema metni kullanıcı/AI
kaynaklı + `/share/[token]` ile paylaşılabilir → saklı XSS).

---

## SEC-005 — JWT fallback anahtarı yalnızca `IsProduction()` ile kapatılıyor

### Finding
`Jwt:Key` tanımsızsa ve ortam adı **tam olarak** `Production` değilse, depoda
yazılı sabit anahtar kullanılıyor.

### Location
`backend/Namines.API/Program.cs:164-173`

```csharp
if (builder.Environment.IsProduction())
    throw new InvalidOperationException("Jwt:Key production ortamında zorunludur...");

secretKey = "NaminesDevFallbackKey_Change_In_Production_Min32Chars!";
```

### Problem
Kapı doğru ama **çok dar**. `ASPNETCORE_ENVIRONMENT` değeri `Staging`, `prod`,
küçük harfli `production` (`IsProduction()` ordinal karşılaştırır) ya da konteyner
ortamlarında sıklıkla unutulan `Development` olduğunda uygulama **herkesin
bildiği bir anahtarla** JWT imzalıyor. Anahtar bu depoda ve GitHub'da açık.

### Risk
Anahtarı bilen biri istediği `sub` ile jeton üretir → **tam kimlik doğrulama
baypası**: her hesap, her proje.

### Severity
**HIGH** — istismarı önemsiz; yalnızca yanlış yapılandırmaya bağlı.

### Recommendation
Mantığı ters çevir: **yalnızca `IsDevelopment()`** iken fallback'e izin ver, diğer
her ortamda fırlat. Ek olarak açılışta yapılandırılan anahtarın fallback ile aynı
olup olmadığını kontrol edip fırlat — kopyala-yapıştır ile üretime taşınmasın.

### Effort
XS

### Priority
P0

### ✅ Yapıldı
İki katmanlı:
1. Kapı `IsProduction()` → **`!IsDevelopment()`**. Artık `Staging`, `prod` ya da
   tanımsız ortam adlarında da uygulama açılmıyor.
2. Anahtar **verilmiş olsa bile** fallback'in kopyasıysa fırlatılıyor. `.env`
   dosyaları kopyala-yapıştır ile çoğaldığı için bu, tanımsız bırakmaktan daha
   olası bir hata — ve tanımsızlık kontrolü onu yakalayamazdı.

---

## SEC-006 — SSRF doğrulaması ile bağlantı arasında TOCTOU / DNS rebinding

### Finding
`SsrfGuard.IsHostSafe` host'u DNS ile çözüp adresleri denetliyor; ardından sürücü
bağlanırken **yeniden** çözüyor. İki çözüm arasında cevap değişebilir.

### Location
`backend/Namines.Core/Security/SsrfGuard.cs` — `IsHostSafe`
Çağıran: `DatabaseExecutorService.ValidateConnectionTarget`

### Problem
Saldırgan kontrolündeki bir alan adı, TTL=0 ile önce public bir IP, sonra iç ağ
IP'si dönebilir. Doğrulama geçer, bağlantı iç hedefe gider.

### Risk
İç ağdaki veritabanlarına erişim. (Bulut metadata uçları HTTP olduğu için bir SQL
istemcisiyle sömürülmesi zor; asıl hedef iç DB'ler.)

### Severity
**MEDIUM** — istismarı zahmetli, etkisi yüksek.

### Recommendation
Doğrulanan IP'ye **doğrudan** bağlan (host adını değil, çözülen adresi kullan),
ya da bağlantıyı yalnızca giden trafiği kısıtlayan bir ağ segmentinden çıkar
(egress allowlist). İkincisi altyapı işi ve kod değişikliğinden daha sağlam.

### Effort
M (kod) / S (altyapı)

### Priority
P2

### 🟡 AÇIK — bilinçli
Kod tarafında bir yama (çözülen IP'ye doğrudan bağlanma) sürücü bağlantı
dizesini yeniden yazmayı gerektiriyor ve TLS sertifika doğrulamasını bozma
riski taşıyor (sertifika host adına düzenlenmiş olur). Doğru çözüm **egress
allowlist** — altyapı kararı. Açık bırakıldı ve `21-risk-register.md`'de
izleniyor.

---

## SEC-007 — Zayıf parola politikası

### Finding
`RequiredLength = 8`; büyük harf, küçük harf ve alfanümerik olmayan karakter
zorunlulukları **kapalı**.

### Location
`backend/Namines.API/Program.cs:155-158`

### Problem
8 karakter, karmaşıklık zorunluluğu olmadan, bugünün standardına göre düşük.
Sızdırılmış-parola kontrolü (HIBP k-anonimlik) da yok.

### Severity
**MEDIUM**

### Recommendation
NIST SP 800-63B çizgisi: uzunluğu **12**'ye çıkar; karmaşıklık kuralları yerine
sızdırılmış-parola listesi kontrolü ekle. Karmaşıklık kuralları kullanıcıyı
`Parola1!` yazmaya iter; uzunluk + liste kontrolü daha etkilidir.

### Effort
S

### Priority
P2

### ✅ Yapıldı
- `RequiredLength` 8 → **12** (NIST SP 800-63B: karmaşıklık yerine uzunluk).
- **Hesap kilitleme açıldı:** `MaxFailedAccessAttempts = 5`,
  `DefaultLockoutTimeSpan = 15 dk`, `AllowedForNewUsers = true`.
- `AuthController.Login` yeniden yazıldı: `IsLockedOutAsync` kontrolü,
  başarısızlıkta `AccessFailedAsync`, başarıda `ResetAccessFailedCountAsync`.

**Kritik ayrıntı:** önceki kod yalnızca `CheckPasswordAsync` çağırıyordu — o
metot başarısız denemeyi **saymaz**, dolayısıyla ayarlar eklenmiş olsa bile
kilitleme hiç devreye girmezdi. Sayma çağrısı olmadan politika sadece görüntü
olurdu.

Ayrıca kullanıcı bulunamadığında da aynı mesaj dönüyor — farklı cevap, kayıtlı
e-postaları sızdıran bir numaralandırma aracı olurdu.

**Sızdırılmış-parola listesi (HIBP) kontrolü hâlâ yok** — ayrı bir iş.

---

## SEC-008 — `ConnectionString = null` güvenlik tiyatrosu

### Finding
Controller, kullanımdan sonra `request.ConnectionString = null` yapıyor ve buna
"Memory security" diyor.

### Location
`backend/Namines.API/Controllers/DatabaseExecutorController.cs:35, 50`

### Problem
.NET'te `string` **değişmez**. Referansı null'lamak bellekteki baytları silmez;
dize, GC toplayana kadar heap'te durur ve bu arada bir bellek dökümüne düşebilir.
Yorum, verilmeyen bir garantiyi veriyormuş gibi yazılmış — asıl zarar bu: sonraki
okuyucu sorunu çözülmüş sanıyor.

### Severity
**LOW** — teknik etki düşük, yanıltıcı yorumun etkisi yüksek.

### Recommendation
Ya yorumu gerçeğe uydur ("GC'ye erken bırakıyoruz, bellek temizliği DEĞİL"), ya da
SEC-001'in "projeye bağla" önerisini uygula: bağlantı dizesi hiç istemciden
gelmezse sorun kendiliğinden kalkar.

### Effort
XS

### Priority
P3

### ✅ Yapıldı
Satırlar ve yanıltıcı "Memory security" yorumu kaldırıldı. `ExecutorRequest`
artık `ConnectionString`'i null'lamıyor; verilmeyen bir garanti veriyormuş gibi
görünen kod yok.

---

## Kapsanan ama bulgu ÇIKMAYAN alanlar

Bunlar arandı ve temiz bulundu — negatif sonuç da bilgidir:

| Alan | Durum | Kanıt |
|---|---|---|
| Depoda sabit kodlanmış sır | ✅ Temiz | `sk-` / `gsk_` / `ghp_` / `AKIA` desenleri tarandı → 0 eşleşme |
| Git geçmişinde sızmış `.env` | ✅ Temiz | `a2bc141`'de `.env` vardı ama tek satırı `GROQ_API_KEY=` (**değer boş**); `fc35359`'da silinmiş |
| SQL enjeksiyonu (Gateway) | ✅ Savunulmuş | `ValidateIdentifierOrThrow` + `^[A-Za-z_][A-Za-z0-9_]*$`, 20+ çağrı noktasında tutarlı; testi var (`GatewayServiceTests.cs:152`) |
| `ExecuteSqlRaw` / `FromSqlRaw` (EF) | ✅ Hiç yok | Tüm depoda 0 eşleşme |
| Global exception handling | ✅ Var | `ExceptionMiddleware.cs`, korelasyon ID'li |
| Loglarda PII | ✅ Savunulmuş | `PiiRedactionEnricher`, iki Serilog yapılandırmasına da bağlı |
| Rate limit partition hatası | ✅ Savunulmuş | Kullanıcı kimliği ile bölünüyor (`Program.cs:230+`), gerekçesi yorumda |

---

## SQL enjeksiyonu — derinlemesine not

Tek savunma hattı `IdentifierPattern` regex'i. `Quote()` fonksiyonu sınırlayıcıyı
**kaçırmıyor**:

```csharp
"MSSQL" or "SQLSERVER" => $"[{identifier}]",
```

Bugün güvenli, çünkü regex `]` karakterine izin vermiyor. Ama bu, güvenliğin
**tek bir regex satırına** bağlı olduğu anlamına geliyor: birisi Unicode tablo
adlarını desteklemek için regex'i gevşetirse enjeksiyon sessizce açılır ve hiçbir
test bunu yakalamaz.

**Öneri (P2, S):** `Quote()` sınırlayıcıyı da kaçırsın (`]` → `]]`, backtick →
çift backtick, `"` → `""`). Derinlemesine savunma; regex'i değiştiren kişiyi korur.
