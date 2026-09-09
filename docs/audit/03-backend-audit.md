# 03 — Backend denetimi (C# / ASP.NET Core)

> ## ✅ DURUM (2026-09-09)
> | Bulgu | Durum |
> |---|---|
> | BACK-001 `.Result` | Yanlış pozitifti — değişiklik yok |
> | BACK-002 `CompileController` rate limit | ✅ `[EnableRateLimiting("sensitive")]` |
> | BACK-003 Tutarsız hata politikası | ✅ Ortak `DbConnectionFailure` |
> | BACK-004 Controller boyutları | 🟡 Açık — `GatewayController` bölme işi (P1, L) |
> | BACK-005 Async disiplini | Bulgu yoktu |
> | BACK-006 Docker.DotNet çatışması | 🟡 Açık — P2 |
> | BACK-007 Şifreleme | Bulgu yoktu |

**Ölçüm:** 62.183 satır C#, 9 proje, 33 controller.

---

## Genel izlenim

Bu, gördüğüm ortalamanın **belirgin biçimde üstünde** bir .NET tabanı. Somut
göstergeler, ölçülmüş:

| Gösterge | Değer | Yorum |
|---|---|---|
| `TODO` / `FIXME` / `HACK` | **0** | 62k satırda sıfır. Olağandışı. |
| `ExecuteSqlRaw` / `FromSqlRaw` | **0** | Ham EF SQL'i hiç yok |
| Gerçek sync-over-async | **0** | (aşağıda açıklanıyor) |
| Backend testleri | **1.480 geçiyor**, 3 atlanıyor | 13 dk, gerçek konteynerlere karşı |
| Derleme uyarısı | **0** | `dotnet build` temiz |

Yorumlar **kararın gerekçesini** yazıyor, kodun ne yaptığını değil. Bu, denetim
sırasında yanlış pozitifleri büyük ölçüde eledi: bir desen tuhaf göründüğünde
yanında neden öyle olduğu yazıyordu.

---

## BACK-001 — `.Result` kullanımları: YANLIŞ POZİTİF, bulgu değil

Tarama 4 eşleşme buldu. İncelendi:

- `ChangeRequestController.cs:348-349` — hemen öncesinde `await Task.WhenAll(...)`
  var. Tamamlanmış task'tan `.Result` okumak **doğru ve deyimsel**. Bulgu değil.
- `CoderAIController.cs:208`, `AsyncProgress.cs:21` — senkron arayüz
  (`IProgress<T>.Report`) uyarlaması. Kaçınılmaz; alternatifi fire-and-forget
  olurdu ve daha kötü olurdu.

**Sonuç: değişiklik önerilmiyor.** Bu maddeyi rapora, "arandı ve temiz çıktı"
kaydı olarak koyuyorum.

---

## BACK-002 — `CompileController` kimlik doğrulamasız, 10 uç

### Location
`backend/Namines.API/Controllers/CompileController.cs:27-29` — sınıf düzeyinde
`[Authorize]` **yok**.

### Current State
Uçlar saf dönüşüm: `.nsl` metni → DDL, şema → EF Core / Prisma / eject paketi.
Kalıcı durum yazmıyorlar, veritabanına bağlanmıyorlar.

### Problem
Dönüşümler saf ama **ucuz değil**: `eject` bir proje iskeleti üretip ZIP'liyor
(`System.IO.Compression` import edilmiş). Kimliksiz bir çağıran bunu sınırsız
tekrarlayabilir.

### Risk
CPU + bellek tüketimine dayalı hizmet dışı bırakma. Kimlik olmadığı için
kullanıcı-bazlı rate limit partition'ı da çalışmaz — IP'ye düşer.

### Severity
**MEDIUM**

### Recommendation
`[EnableRateLimiting("sensitive")]` en azından; tercihen `[Authorize]`.
Anonim demo gerçekten isteniyorsa (`/demo` rotası var) yalnızca o uç anonim
kalsın, `eject` kimlik istesin.

### Effort
XS · ### Priority P1

### ✅ Yapıldı
Sınıfa `[EnableRateLimiting("sensitive")]` eklendi. `[Authorize]` **bilerek
eklenmedi**: `/demo` ve `/compile` anonim deneme akışının parçası ve ürünün
dönüşüm hunisinde duruyor. Limit, kimlik varsa kullanıcıya, yoksa IP'ye göre
bölünüyor.

---

## BACK-003 — Aynı sözleşmenin iki uygulaması arasında tutarsız hata politikası

Bkz. [SEC-002](06-security-audit.md#sec-002--sürücünün-ham-hata-mesajı-istemciye-dönüyor-bağlantı-kâşifi).
`GatewayKeyController` sürücü mesajını sınıflandırıyor;
`DatabaseExecutorController` ham geçiriyor. Aynı sorun, iki farklı cevap.

**Backend açısından çıkarım:** hata sınıflandırması bir **ortak servis** olmalı,
controller'a gömülü bir private metot değil. Bugün `ClassifyConnectionFailure`
`GatewayKeyController`'ın içinde özel; bu yüzden yeniden kullanılmadı.

### Effort S · ### Priority P1

### ✅ Yapıldı
`Namines.Core/Security/DbConnectionFailure.cs` — ortak sınıflandırıcı. İki
controller da onu kullanıyor; `GatewayKeyController`'daki private kopya silindi.

---

## BACK-004 — Controller boyut dağılımı

| Controller | Satır | Uç | Yorum |
|---|---|---|---|
| `GatewayController` | 1.447 | 15 | Bkz. ARCH-002 |
| `SchemaController` | 662 | 7 | AI çağrıları; kabul edilebilir |
| `AuthController` | 601 | 9 | Kimlik + proje senk.; ayrılabilir |
| Diğer 30 controller | ≤ 591 | — | Sağlıklı |

Medyan controller ~180 satır. Dağılım sağlıklı; sorun iki uç değerde.

---

## BACK-005 — Async / CancellationToken disiplini

Örnekleme yapıldı (`VaultController`, `GroundController`, `BranchController`,
`ChangeRequestController`): `CancellationToken` **imzalarda taşınıyor ve
servise geçiriliyor**. Bu, çoğu .NET projesinde ilk feda edilen şeydir; burada
tutarlı.

`VaultService` ve `Namines.Vault` içinde `System.IO.Pipelines` ile üç aşamalı
akış kullanılıyor (üret → dönüştür → tüket) — yedekler belleğe **hiç
toplanmıyor**. Bu, gigabaytlık dump'larda OOM'u yapısal olarak imkânsız kılan
doğru tercih.

**Bulgu yok.**

---

## BACK-006 — Docker.DotNet sürüm çatışması (test projesinde)

### Finding
`Namines.Vault` → `Docker.DotNet 3.125.15`.
`Namines.Tests` → `Testcontainers.* 4.13.0`, bu da Docker.DotNet **4.x** getiriyor.
Test bin klasöründe 4.x kazanıyor.

### Location
`backend/Namines.Vault/Namines.Vault.csproj:27`,
`backend/Namines.Tests/Namines.Tests.csproj:16-19`

### Current State
Bu oturumda **gerçekten patladı**:
`TypeLoadException: Could not load type 'Docker.DotNet.DockerClientConfiguration'
from assembly 'Docker.DotNet, Version=4.3.0.0'` — bir sağlayıcı nesnesi
kurulduğu anda.

Geçici olarak Docker istemcisi `Lazy<T>` yapılarak testler kurtarıldı (ki bu
kendi başına doğru bir tasarım), ama **çatışma duruyor**: testte Docker'a
gerçekten dokunan bir Vault yolu yazıldığı gün yeniden patlayacak.

### Severity
**MEDIUM** — gizli, tetiklendiğinde kafa karıştırıcı.

### Recommendation
İki yoldan biri:
- `Namines.Vault`'u Docker.DotNet 4.x'e taşı (API değişiklikleri var; Vault'un
  canlı doğrulanmış akışlarının yeniden kanıtlanması gerekir), ya da
- Test projesinde açık bir `<PackageReference>` ile sürümü sabitle ve
  binding redirect ile tek sürüme zorla.

Birincisi doğru, ikincisi ucuz. Karar bilinçli verilmeli.

### Effort
M · ### Priority P2

---

## BACK-007 — Şifreleme ve sır yönetimi (GÜÇLÜ YAN)

- `IConnectionSecretProtector` ile bağlantı dizeleri şifreli saklanıyor.
- Vault yedekleri AES-256-GCM, parçalı, blok başına nonce+tag.
- `Vault:BackupEncryptionKey` **tanımsızsa Vault açıkça duruyor** — şifresiz
  yedek yazmıyor. Fail-closed'ın doğru uygulanışı.
- `Security:ConnectionEncryptionKey` ile Vault anahtarının **ayrı olması**
  zorunlu tutulmuş ve gerekçesi `.env.example`'da yazılı.

**Bulgu yok. Bu bölüm örnek alınmalı.**
