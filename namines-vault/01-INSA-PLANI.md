# 01 — Namines Vault İnşa Planı

> **Bu dokümanın işi:** [`00-GENEL-BAKIS.md`](00-GENEL-BAKIS.md) *ne* ve *neden*
> sorusunu cevaplıyor; bu doküman **nasıl** sorusunu — iş paketleri, dosya
> dosya yapı, veri modeli, API sözleşmesi ve her adımın kabul kanıtı.
>
> **Durum:** **V0–V5 tamamlandı ve canlı doğrulandı.** Erişim modeli ve deploy
> şartları için [`02-ERISIM-VE-DEPLOY.md`](02-ERISIM-VE-DEPLOY.md).
>
> Planla arasındaki iki bilinçli fark, aşağıdaki iş paketlerinde de not edildi:
> yedekler S3 yerine **sunucu diskinde** (`IBackupStore` arkasında, sağlayıcı
> seçimi hâlâ açık karar) ve kayıt yazımı `IVaultRecordStore` yerine
> **`Namines.Infrastructure`'daki `VaultService`** üzerinden — modül sınırı
> yine de korunuyor, `Namines.Vault` `Namines.Infrastructure`'ı görmüyor.
>
> Kalanlar: MySQL (V6), kısmi geri yükleme, nesne depo uygulaması.
>
> **Biçim:** `namines_desk/10-DESK-V2-YOL-HARITASI.md` ile aynı — numaralı iş
> paketi, her birinde *bugünkü durum → yapılacak → kabul kriteri*.

---

## Öneri sırası

```
V0  Modül iskeleti          — proje, isim alanı, DI, boş uç, migration (yarım gün)
V1  Yedek alma (PostgreSQL) — pg_dump → şifreli nesne depo → kayıt
V2  Yedek listesi + indirme — kullanıcının yedeğini görmesi ve alması
V3  Geri yükleme            — en riskli adım; onay akışı ve "önce yedek" burada
V4  Zamanlanmış yedek       — BackgroundService + cron
V5  Bütünlük doğrulama      — alınan yedeği gerçekten geri yükleyip kanıtlama
────────────────────────────
V6+ İkinci motor (MySQL), kısmi geri yükleme, webhook — V0-V5 kanıtlanmadan başlanmaz
```

**Neden bu sıra:** V0-V3 tek bir yürüyen iskelet oluşturuyor — yedek alınabiliyor,
görülebiliyor, geri yüklenebiliyor. Bu üçü uçtan uca kanıtlanmadan V4 (zamanlama)
anlamsız: kanıtlanmamış bir yedeği otomatikleştirmek, hatayı gece yarısına
taşımaktan başka bir şey değil. V5, "yedek var ama bozuk" sınıfını kapatıyor ve
bilerek V4'ten sonra — çünkü doğrulamanın kendisi bir geri yükleme, yani V3'ün
kodunu tekrar kullanıyor.

---

## Yapıtaşları — Vault'un SIFIRDAN yazmayacağı şeyler

Bu plan yeni bir altyapı icat etmiyor. Vault'un devraldığı, kod tabanında
**zaten çalışan** parçalar:

| İhtiyaç | Var olan parça | Nerede |
|---|---|---|
| Bağlantı dizesini çözmek | `IConnectionSecretProtector` (AES-256-GCM) | `Namines.Core/Security/IConnectionSecretProtector.cs`, DI: `ServiceCollectionExtensions.cs:115` |
| Şifreli bağlantının kendisi | `CloudProject.EncryptedConnectionString` + `ConnectionDbType` | `Namines.Core/Models/Auth/CloudProject.cs` |
| Yetki kontrolü | `OrgAccess.CanViewAsync` / `CanEditAsync` / `CanManageMembersAsync` | `Namines.Infrastructure/Data/OrgAccess.cs` |
| SSRF koruması | `IDbHostAccessPolicy` + `DbIntrospectionService.ExtractHost` | `Namines.Infrastructure/Security/DbHostAccessPolicy.cs` |
| Denetim kaydı deseni | `GatewayAuditEntry` + `RecordAuditAsync` | `Namines.Infrastructure/Data/GatewayAudit.cs` |
| Arka plan işi deseni | `BackgroundService` alt sınıfı + `AddHostedService` | `DockerSweeperBackgroundService.cs`, DI: `:119` |
| Şema/migration | `AuthDbContext` + EF migration | `Namines.Infrastructure/Data/AuthDbContext.cs`, `Migrations/` |
| Konteyner çalıştırma | `Docker.DotNet` istemcisi (ham, `docker.sock` mount YOK) | `DockerBackupService.cs` — **kopyalanacak desen, çağrılacak kod değil** |

> **`DockerBackupService`'e dokunulmuyor.** `00-GENEL-BAKIS.md` §2'nin gerekçesi
> geçerli: o bir *şema* yedekçisi. Ondan alınacak tek şey `Docker.DotNet` ile
> konteyner çalıştırma/çıktı okuma **deseni**.

---

## V0 — Modül iskeleti

**Bugünkü durum:** `backend/Namines.Vault/` yok. `Namines.sln` yedi proje içeriyor.

**Yapılacak:**

```
backend/Namines.Vault/
  Namines.Vault.csproj          → net8.0; ProjectReference: Namines.Core
  Abstractions/
    IBackupProvider.cs          → motor başına yedek/geri yükleme sözleşmesi
    BackupSpec.cs, RestoreSpec.cs, BackupArtifact.cs
  Providers/
    PostgresBackupProvider.cs   → V1'de dolar
  Storage/
    IBackupStore.cs             → nesne depo soyutlaması
    S3BackupStore.cs            → V1'de dolar
  VaultServiceCollectionExtensions.cs → AddNaminesVault(this IServiceCollection)
```

`Namines.API/Controllers/VaultController.cs` — rota öneki `/api/vault`,
`[Authorize]`, tüm uçlar `projectId` alır.

**Neden `Namines.Vault` ayrı bir proje, `Namines.Infrastructure` içinde bir klasör
değil:** `00-GENEL-BAKIS.md` §10'un modül sınırı kuralını derleyicinin
zorlamasını istiyoruz. Ayrı bir csproj, `Namines.Vault`'un yanlışlıkla
`Namines.Infrastructure`'ın iç tiplerine bağlanmasını **derleme hatası** yapar;
aynı projedeki bir klasör bunu yalnızca disiplinle korur.

**Bağımlılık yönü (tek yönlü, ihlali derlemeyi kırar):**
`Namines.API` → `Namines.Vault` → `Namines.Core`.
`Namines.Vault` → `Namines.Infrastructure` referansı **YOK**. Vault'un DB'ye
ihtiyacı olan yerlerde (kayıt yazma) `Namines.Core`'da tanımlı bir arayüz
(`IVaultRecordStore`) kullanılır; uygulaması `Namines.Infrastructure`'da yaşar.

**Kabul kriteri:**
`dotnet build Namines.sln` 0 hata 0 uyarı; `GET /api/vault/health` 200 döner ve
`Namines.Vault.csproj` içinde `Namines.Infrastructure` referansı **yok**.

---

## V1 — Yedek alma (PostgreSQL)

**Bugünkü durum:** Canlı bir veritabanının verisini alan hiçbir kod yok.

**Yapılacak — akış:**

```
POST /api/vault/backups/{projectId}     (Admin+; body: { note?: string })
  1. Yetki: CanManageMembersAsync — geri yükleme veriyi TAMAMEN değiştirebildiği
     için Vault'un tamamı Admin+ eşiğinde (CanEditAsync YETERSİZ)
  2. CloudProject.EncryptedConnectionString → IConnectionSecretProtector.Unprotect
  3. Host → IDbHostAccessPolicy.IsHostAllowed  (SSRF; Gateway ile aynı kapı)
  4. VaultBackupRecord: status=Running, startedAt=now  → DB'ye YAZ (önce kayıt)
  5. IBackupProvider.BackupAsync → pg_dump -Fc (custom format, sıkıştırılmış)
  6. Çıktı akışı → AES-256-GCM ile şifrele → IBackupStore.PutAsync
  7. status=Succeeded|Failed, sizeBytes, completedAt, storageKey → GÜNCELLE
```

**`pg_dump` nasıl çalıştırılır:** resmî `postgres:17-alpine` imajında, ağ erişimi
olan geçici bir konteynerde. Namines'in kendi süreci `pg_dump` binary'sine sahip
olmak zorunda kalmaz ve sürüm uyumu (dump eden `pg_dump` ≥ sunucu sürümü)
imaj etiketiyle yönetilir.

> **`docker.sock` mount edilmez** — `AGENTS.md`'nin kesin kuralı. Konteyner,
> ana backend'in zaten kullandığı `Docker.DotNet` istemcisiyle host'un
> daemon'una **uzaktan** konuşularak açılır (`DockerBackupService`'in deseni).
> Bu, `06-DATA-PLANE.md`'nin provisioning broker kararıyla da tutarlı.

**Şifreleme:** dump nesne depoya **şifreli** yazılır (`00-GENEL-BAKIS.md` §3
madde 8: opsiyonel değil). Anahtar `IConnectionSecretProtector`'ınkinden
**AYRI** bir yapılandırma anahtarı (`Vault:BackupEncryptionKey`) — bağlantı
sırrını çözen anahtarla yedek içeriğini çözen anahtarın aynı olması, birinin
sızmasını ikisinin sızması yapar.

**Veri modeli (yeni tablo, yeni migration):**

```csharp
public class VaultBackupRecord {
    public string Id { get; set; }             // guid
    public string ProjectId { get; set; }
    public string Engine { get; set; }         // "PostgreSQL"
    public string Status { get; set; }         // Running|Succeeded|Failed
    public long? SizeBytes { get; set; }
    public string? StorageKey { get; set; }    // nesne depodaki yol
    public string? Note { get; set; }          // §11.4 kullanıcı notu
    public string? Error { get; set; }
    public string CreatedByUserId { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime? VerifiedAt { get; set; }  // V5 doldurur
    public string? VerifyError { get; set; }   // V5 doldurur
}
```

**Kritik: parola loglanmaz.** `pg_dump` parolayı komut satırından DEĞİL
`PGPASSWORD` ortam değişkeninden alır — komut satırı konteyner meta verisinde
ve `docker inspect` çıktısında görünür, ortam değişkeni de görünür ama
konteyner ömrü saniyeler ve kayıt tutulmaz. Hiçbir log satırına bağlantı
dizesi yazılmaz (`DbIntrospectionService`'in aynı kuralı).

**Kabul kriteri (kanıt = çalışan komut + görülen çıktı):**
1. İçinde **gerçek satır olan** bir PostgreSQL veritabanı için yedek alınır.
2. Nesne depodaki dosya indirilip **elle** `pg_restore -l` ile listelenir ve
   içinde kullanıcının tablolarının göründüğü **görülür**.
3. `VaultBackupRecord.SizeBytes` ile dosyanın gerçek boyutu eşleşir.
4. Yanlış parolalı bir bağlantıda `status=Failed` ve `Error` dolu; **kısmi
   dosya nesne depoda BIRAKILMAZ**.

---

## V2 — Yedek listesi + indirme

**Bugünkü durum:** V1'in kaydı var, kullanıcı göremiyor.

**Yapılacak:**

```
GET  /api/vault/backups/{projectId}        → VaultBackupRecord[] (yeni → eski)
GET  /api/vault/backups/{backupId}/download → 302 (kısa ömürlü imzalı URL)
DELETE /api/vault/backups/{backupId}        → kayıt + nesne birlikte silinir
```

**İndirme neden imzalı URL, akış değil:** birkaç GB'lık bir dump'ı ana API
sürecinden akıtmak, o süreci dakikalarca meşgul eder. Nesne deponun kendi
imzalı URL'i (S3 presigned, ömür ≤5 dk) veriyi doğrudan kullanıcıya taşır.

> **Karar bekliyor:** nesne depo sağlayıcısı (`00-GENEL-BAKIS.md` §9 madde 1).
> Kod `IBackupStore` arkasında yazılır, bu karar V2'yi bloklamaz — MinIO ile
> geliştirilip sağlayıcı sonra seçilebilir.

**Arayüz:** Desk'in bir sekmesi olarak (`services/desk/app/Vault.tsx`), `DeskView`
birliğine `'vault'` eklenerek. Ayrı bir uygulama **değil** — Ground'un kendi
arayüzü olmaması gibi (`namines-ground/00-GENEL-BAKIS.md` §1), Vault da Desk'in
içinde yaşar. Bu, `AppShell`'in "İşlemler" grubuna bir satır eklemek demek.

**Kabul kriteri:** İki yedek alınır; liste ikisini de doğru boyut/tarihle
gösterir; indirilen dosya `pg_restore -l` ile açılır; silinen bir yedeğin nesnesi
depoda **kalmaz** (elle kontrol edilir).

---

## V3 — Geri yükleme

> **Bu, Vault'un en tehlikeli adımı.** Geri yükleme hedefin tamamını üzerine
> yazar ve geri alınamaz. V1/V2 kanıtlanmadan başlanmaz.

**Yapılacak — akış:**

```
POST /api/vault/restore/{backupId}   (Owner; Admin YETERSİZ)
  body: { targetProjectId, confirmProjectName }
  1. confirmProjectName, hedef projenin adıyla TAM eşleşmeli — yoksa 400
  2. Hedefte mevcut tablo/satır sayısı okunur ve YANITTA döner (kullanıcı
     neyi sildiğini önce görmüş olmalı — akış iki adımlı: önizle, sonra onayla)
  3. ZORUNLU ÖN YEDEK: hedefin mevcut hâli V1 akışıyla yedeklenir
     (Note="restore öncesi otomatik"). Bu yedek BAŞARISIZ olursa restore
     BAŞLAMAZ — güvenlik ağı olmadan geri alınamaz bir işlem yapılmaz.
  4. pg_restore --clean --if-exists → hedef
  5. VaultRestoreEntry audit kaydı (kim, hangi yedek, hangi hedef, sonuç)
```

**Farklı projeye geri yükleme** (`00-GENEL-BAKIS.md` §11.3) varsayılan olarak
**kapalı**; açıksa `confirmProjectName` **hedef** projenin adını ister.

**Neden Owner, Admin değil:** Desk'te SQL konsolu eşiği de Owner
(`GatewayController.DeskSql`). Geri yükleme ondan daha yıkıcı; eşiği düşürmek
tutarsız olurdu.

**Kabul kriteri:**
1. Veri yazılır → yedek alınır → veri **elle bozulur** (satır silinir) →
   geri yüklenir → silinen satırın **geri geldiği** bağımsız bir `psql`
   oturumunda görülür.
2. Yanlış proje adı yazıldığında işlem **başlamaz**.
3. Ön yedek kaydı listede görünür ve kendisi de geri yüklenebilir.
4. Denetim kaydında restore işlemi görünür.

---

## V4 — Zamanlanmış yedek

**Yapılacak:** `VaultScheduleService : BackgroundService` — `DockerSweeperBackgroundService`
deseniyle, `AddHostedService` ile kayıtlı.

```csharp
public class VaultSchedule {
    public string ProjectId { get; set; }
    public string Cadence { get; set; }        // "daily" | "weekly"
    public int HourUtc { get; set; }
    public int? DayOfWeek { get; set; }
    public bool Enabled { get; set; }
    public DateTime? LastRunAt { get; set; }
}
```

**Cron ifadesi DEĞİL, iki seçenek** — `00-GENEL-BAKIS.md` §9 madde 3'ün açık
kararına şu öneri: kullanıcıya cron yazdırmak, yanlış yazılmış bir ifadeyle
yedeğin sessizce hiç çalışmaması demek. İki sabit seçenek bu hata sınıfını
tamamen kaldırır.

**Çoklu instance güvenliği:** İki API instance'ı aynı anda çalışırsa aynı yedek
iki kez alınır. `VaultSchedule` üzerinde `(ProjectId, LastRunAt)` ile
koşullu güncelleme (optimistic concurrency) — "önce satırı kap, sonra çalış".

**Saklama/kota:** Plan bazlı sayılar hâlâ açık karar (§9 madde 2). Kod
`PlanQuotas` desenini izler; sayı gelene kadar yapılandırmadan okunur.

**Kabul kriteri:** Saat ileri alınmış bir test ortamında zamanlanmış yedek
tetiklenir ve kaydı oluşur; aynı anda iki instance çalıştırıldığında **tek**
yedek alınır.

---

## V5 — Bütünlük doğrulama

**Yapılacak** (`00-GENEL-BAKIS.md` §11.1): her başarılı yedek, arka planda
geçici bir PostgreSQL konteynerine **gerçekten geri yüklenir**; sonuç
`VerifiedAt` / `VerifyError` olarak yazılır ve listede "✓ doğrulandı" rozetiyle
gösterilir.

**Neden V3'ten sonra:** doğrulama, geri yükleme kodunun ta kendisi — hedefi
kullanıcının veritabanı yerine geçici bir konteyner olan hâli.

**Kabul kriteri:** Bilerek bozulmuş bir dump dosyası `VerifyError` üretir ve
listede "✓" **görünmez**.

---

## Test stratejisi

Kod tabanının mevcut disipliniyle aynı (`backend/Namines.Tests/README.md`):

| Katman | Nasıl | Not |
|---|---|---|
| Saf mantık (kota, zamanlama, ad doğrulama) | xUnit birim testi | Docker gerekmez, her koşuda çalışır |
| `pg_dump`/`pg_restore` akışı | `RequiresDockerFact` | Docker yoksa **atlanır**, kırılmaz |
| Uçtan uca yedek→boz→geri yükle | `RequiresDockerFact` | V3'ün kabul kriterinin otomatik hâli |

> **Kural:** `AGENTS.md`'nin "testler geçiyor hiçbir şey kanıtlamıyor" dersi
> burada iki kat geçerli. Her kabul kriteri **bağımsız bir `psql` oturumunda**
> gözle doğrulanır; testin yeşil olması yeterli sayılmaz.

---

## Riskler ve azaltma

| Risk | Neden ciddi | Azaltma |
|---|---|---|
| Yanlış hedefe geri yükleme | Geri alınamaz veri kaybı | Proje adı yazma + zorunlu ön yedek (V3) |
| Sürüm uyumsuzluğu (`pg_dump` < sunucu) | Dump sessizce eksik/başarısız | İmaj etiketi sunucu sürümüne göre seçilir; uyumsuzlukta **hata**, sessiz devam yok |
| Büyük veritabanı, uzun işlem | HTTP isteği zaman aşımına uğrar | İş asenkron: uç `202` + `backupId` döner, durum yoklanır |
| Nesne depo dolması | Yedek sessizce başarısız | Kota kontrolü **yedek başlamadan** yapılır |
| Şifreleme anahtarı kaybı | Tüm yedekler kalıcı olarak okunamaz | Anahtar rotasyonu **v1'de yok** — bu bilinçli sınır, dokümante edilir |

---

## Açık kararlar (kod başlamadan netleşmeli)

`00-GENEL-BAKIS.md` §9'un üç maddesi + bu planın açtığı ikisi:

4. **`Vault:BackupEncryptionKey` yönetimi** — `Security:ConnectionEncryptionKey`
   gibi ortam değişkeni mi, yoksa bir KMS mi? (v1 için ortam değişkeni önerilir,
   mevcut desenle tutarlı.)
5. **Asenkron iş durumu** — yoklama (polling) yeterli mi, yoksa SignalR
   üzerinden ilerleme akıtılsın mı? (Öneri: v1 yoklama; SignalR hub'ı şu an
   canvas işbirliği için yapılandırılmış, Vault'u ona bağlamak ayrı bir iş —
   Desk'in `VERSION_POLL_MS` kararıyla aynı gerekçe.)

---

## Kalıcı olarak Vault'a GİRMEYECEKLER

`00-GENEL-BAKIS.md` §8 aynen geçerli. Bu planın eklediği tek madde:

- **Yedek şifreleme anahtarının kullanıcıya verilmesi.** "Kendi anahtarını
  getir" (BYOK) kulağa güvenli geliyor ama anahtarını kaybeden kullanıcının
  yedeği kurtarılamaz hâle gelir ve bu, destek yükünü veri kaybı hikâyesine
  çevirir. v1 sunucu tarafı anahtarla kalır.
