# Bulunan hatalar — 2026-09-30

Toplam: **1 CRITICAL · 7 HIGH · 17 MEDIUM · 5 LOW · 8 spekülatif**

Durum sütunu: `açık` / `düzeltildi` / `kabul edildi` (bilinçli taviz).
Düzeltirken bu dosyayı güncelle — tek doğruluk kaynağı burası.

**CRITICAL ve HIGH'ların hepsi kapandı:** C-1, H-1, H-2, H-3, H-4, H-5,
H-6 (dosya oluşturma yarısı), H-7. **MEDIUM'lardan kapanan:** M-1, M-2, M-3, M-4, M-5, M-6, M-7, M-9, M-10,
M-13, M-14, M-17 tam; **M-8 ve M-15 kısmen** (açık kalan yarıları kendi
bölümlerinde yazılı), **M-16 dar kapsamla**. **LOW'lardan kapanan:** L-2, L-3, L-5.
**TÜM BULGULAR KAPANDI.** Tek kısmi kalan **M-15**: jeton ömrü 7 günden 12 saate
çekildi ve işaretlendi, ama hâlâ projeye bağlı değil — gerçek kapsamlama her ucun
claim'e göre gözden geçirilmesini gerektiriyor ve tek başına eklemek, kapsamlıymış
gibi görünen ama olmayan bir jeton üretirdi.

Önceki turda ürün/altyapı kararı bekleyen dördü (M-8'in AAD'si, M-11, M-16'nın
geniş hâli, L-1, L-4) kullanıcının onayıyla kapatıldı — bkz. aşağıdaki "İkinci tur". Her biri için regresyon testi yazıldı ve
düzeltme geri alınıp testin gerçekten kırmızıya döndüğü doğrulandı — "test geçiyor"
tek başına testin hatayı GÖRDÜĞÜNÜ kanıtlamaz.

---

## CRITICAL

### C-1 — Şema tanımlayıcısıyla DDL enjeksiyonu → sandbox'ta superuser kod çalıştırma
**Durum:** **düzeltildi** · **Confidence:** High (kod yolu uçtan uca doğrulandı)

**Konum:** `Namines.Infrastructure/Generators/DdlGenerator/*DdlGenerator.cs` (altı `Quote`);
`Namines.API/Controllers/CompileController.cs:60-65`; `DockerController.cs:50-80`;
`Namines.Infrastructure/Services/DockerBackupService.cs:93-103, 195-207`

Üç halka birden açık:
1. Altı üreticinin hepsinde `Quote` ayırıcıyı **ikilemiyor** (`$"\"{id}\""`, `$"[{id}]"`, `` $"`{id}`" ``).
   Karşıtlık: `GatewayService.Quote` ikiliyor — çalışma-zamanı ve üretim katmanları ayrışmış.
2. Yukarı akışta hiç tanımlayıcı doğrulaması yok: `CompileSql` şemayı gövdeden alıp
   yalnızca null kontrolüyle üreticiye veriyor.
3. Üretilen betik `/api/docker/run` ile container'da **`psql -U postgres` / `mysql -u root`**
   olarak çalışıyor; `HostConfig` yalnızca `Memory` + `NanoCPUs` — **`NetworkMode` yok**.

**Etki:** Sıradan kayıtlı kullanıcıdan container içinde kod çalıştırma + iç ağa/metadata'ya erişim.
Host kaçışı değil (Binds/Privileged/docker.sock yok — doğrulandı).

**Sömürülebilirlik kaydı:** API sürecinin Docker daemon erişimi gerekir. `docker-compose`
topolojisinde socket mount edilmediği için orada çalışmaz; host üzerinde çalışan kurulumda çalışır.
`Sandbox:Enabled` bayrağı yalnızca **temizleyiciyi** kapatıyor, `run` ucunu değil.

**Düzeltme:** (a) altı `Quote`'ta ayırıcıyı ikile + kontrol karakterlerini reddet;
(b) tek bir şema tanımlayıcı doğrulayıcısı, DDL üretiminden önce; (c) sandbox'a
`NetworkMode = "none"`; (d) `PidsLimit` + `CapDrop`.

**Regresyon testi:** Ayırıcı karakter içeren tablo adı → altı üreticinin çıktısında
ikilenmiş olmalı (Docker gerekmez). Ayrı test: `NetworkMode == "none"`.

### C-1 — ne yapıldı

**Yeni:** `Generators/DdlGenerator/SqlIdentifier.cs` — altı üreticinin ve `EnumSql`in
kendi `Quote`'u yerine TEK alıntılama noktası. Ayırıcıyı ikiliyor ve kontrol
karakterlerini (satır sonu, tab, NUL) **reddediyor** — onlar ikilenerek
etkisizleştirilemez ve üretilen betiğin `--` yorumunu erken kapatabilirler.
Sessizce temizlemek yerine reddetmek bilinçli: temizlemek, kullanıcının yazdığından
FARKLI adla tablo açardı.

**43 alıntılama noktası** bu yardımcıya yönlendirildi (6 üretici + `EnumSql`in
`CREATE TYPE`ı + `ColumnFeatureSql`in `COLLATE`ı). Sandbox `HostConfig`ına
`NetworkMode = "none"`, `PidsLimit = 512` ve dört gereksiz yetkinin `CapDrop`u eklendi.

**Testler:** `Namines.Tests/Ddl/IdentifierEscapingTests.cs` — 11 test.

> **İlk geçişim EKSİKTİ ve testler onu yakaladı.** Grep'le bulunan 13 noktayı
> düzelttim, testi yazdım, test KIRMIZI döndü: aslında 34 nokta vardı —
> `CREATE TABLE "{table.Name}"`, HER kolon tanımı ve HER foreign key `Quote`'u
> tamamen atlıyordu. İkinci geçişten sonra bile tek bir nokta kalmıştı:
> Oracle'ın PK KISIT adı (`CONSTRAINT "PK_{table.Name}"`) — beş motor temiz
> görünürken yalnızca Oracle sızıyordu. Tam şema testi (`No_engine_leaks_...`,
> FK + index + UNIQUE + enum) onu buldu. "Hepsini yakaladım" varsayımı
> test edilmeden kabul edilmemeli.

**CapDrop'un dogrulama durumu dürüstçe:** `postgres:15-alpine` ve `mysql:8.0`
gerçek container olarak bu yetkiler düşürülmüş hâlde açıldı ve sağlık
kontrolünden geçti. **MSSQL 2022 doğrulanamadı:** bu makinede `CapDrop`
OLMADAN da 4 dakikada hazır olmadı (kontrol koşusu da zaman aşımına uğradı),
yani engel yetkiler değil; log'unda yetki hatası yok ama uçtan uca kanıt da yok.

---

## HIGH

### H-1 — `/query/nl` salt-okunur metin kapısını atlıyor (INCOMPLETE FIX)
**Durum:** **düzeltildi** · **Confidence:** High

`GatewayController.cs:1436-1469` (NL) vs `:1255-1271` (`/query`); `Namines.Core/Analysis/SqlStatementKind.cs`

`/query`'nin salt-okunur dalı `DeskSqlQueryAsync` → `EnsureReadOnlySelectStatement`'e
gidiyor (yasak kelime taraması, **`INTO` dahil**). `/query/nl` ise `QueryAsync(readOnly: true)`'ı
doğrudan çağırıyor; tek kapısı `SqlStatementKind.Classify` ve onun yazma-fiil listesinde
**`INTO` yok**. MSSQL/Oracle/SQLite'ta oturum seviyesi salt-okunur yok
(`UserDbConnection.AppliesReadOnlySession` false) → o motorlarda tek koruma ilk kelime.
Ayrıca NL yolu `key.CanWrite`'ı hiç sormuyor ve `AuditedAsync` yerine `ExecuteAsync`
kullandığı için **denetim kaydı yazmıyor**.

**Düzeltme:** NL yolunu `DeskSqlQueryAsync`'e yönlendir; `Classify` gövdedeki
yazma anahtar kelimelerini de görsün; denetim kaydı yaz.

### H-1 — ne yapıldı

1. NL çalıştırma yolu `QueryAsync(readOnly: true)` yerine **`DeskSqlQueryAsync`**
   kullanıyor — `/query`'nin salt-okunur dalıyla aynı kapı (baş anahtar kelime +
   yasak kelime taraması, `INTO` dahil) ve aynı satır tavanı.
2. `SqlStatementKind` artık okuma fiiliyle BAŞLAMAYI yeterli saymıyor:
   yeni `EmbeddedWriteVerbs` listesi (`INTO`, `ATTACH`, `DETACH`, `PRAGMA` ve
   yazma fiilleri) gövdede görülürse sonuç `Write`.
3. NL çalıştırma `ExecuteAsync` yerine **`AuditedAsync`** üzerinden gidiyor:
   metni MODEL üretiyor ve ham SQL tablo izinlerini atlıyor, yani hangi soruya
   karşılık hangi sorgunun çalıştığının izi olması şart.

**Testler:** `SqlStatementKindTests`e 13 vaka — `SELECT ... INTO`ın çeşitli
biçimleri, gövdeye gömülü yazma fiilleri, ve **meşru okuma sorgularının hâlâ
`Read` kaldığı** (kapıyı daraltmanın çalışan raporları kırmasını yakalamak için).

> **Bulgunun `CanWrite` yarısı YANLIŞTI — kendi raporumu düzeltiyorum.**
> "NL yolu `key.CanWrite`'ı hiç sormuyor" doğru ama bir açık değil: NL yalnızca
> OKUMA çalıştırıyor, dolayısıyla `CanWrite` istemek yanlış olurdu. Uç zaten
> `AuthorizeForSqlAsync` üzerinden **`CanExecuteSql`** istiyor — ham SQL için
> doğru yetki o. Gerçek olan iki şey vardı: metin kapısının atlanması ve
> denetim kaydının olmaması; ikisi de düzeltildi.

### H-2 — Gateway `expand`: genişletilen tablo izin kontrolünden geçmiyor
**Durum:** **düzeltildi** · **Confidence:** High

`GatewayService.cs:708-721`; `GatewayController.cs:458`

`ApplyExpandsAsync` `expand.Table`'ın söz dizimini doğruluyor ama `IsTableAllowedAsync`
**çağrılmıyor**; `AuthorizeAsync` yalnızca birincil `request.TableName`'i kontrol ediyor.
Bir tabloya okuma izni olan anahtar, izni olmayan ilişkili tabloyu expand ile okuyabiliyor —
ve maskeleme `tableName` başına bakıldığı için gömülü satırlara uygulanmıyor.

**Düzeltme:** Her `expand.Table` için izin kontrolü + maskelemeyi gömülü satırlara uygula.

### H-2 — ne yapıldı

`GatewayController.List`te her `expand.Table` artık `IsTableAllowedAsync`'ten
geçiyor; yeni `MaskExpandedAsync` gömülü satırları KENDİ tablolarının maskeleme
kurallarıyla maskeliyor ve o okumayı denetim kaydına yazıyor. Maskeleme
dönüşümü paylaşılan `MaskValues`a çıkarıldı — iki kopya olsaydı yeni bir tip
yalnızca birine eklenir ve gömülü satırlar sessizce maskesiz kalırdı.

`AuthorizeAsync`ın TAMAMI expand başına çağrılmadı: o metot hız limitinden jeton
düşürüyor ve bir API isteği faturalandırıyor. Tek HTTP isteğini expand sayısı
kadar çarpmak, güvenlik düzeltmesini sessiz bir fiyat artışına çevirirdi.

**Testler:** `Namines.Tests/Controllers/GatewayExpandPermissionTests.cs` — 5 test
(izinsiz tablo, takma ad arkasına saklama, çok-expandda tek izinsiz, izinli
kullanımın geçmesi, boş tablo adı). Kontrolü kaldırıp koşturarak üçünün de
gerçekten kırmızıya döndüğü doğrulandı.

> **Testlerin kapsamadığı yer, dürüstçe:** izin yarısı test edildi, **maskeleme
> yarısı edilmedi** — gömülü satır üretmek gerçek bir veritabanı istiyor.
> `MaskExpandedAsync` yalnızca kod okunarak doğrulandı.

### H-3 — Admin, raw-SQL kapsamlı anahtar üretip Owner-only kapısını atlıyor
**Durum:** **düzeltildi** · **Confidence:** High

`GatewayKeyController.cs:93-131`; `GatewayController.cs:1298-1323`

`Create` `CanManageMembersAsync` (Admin|Owner) ile korunuyor ve `entity.CanExecuteSql`
doğrudan gövdeden geliyor — üreticinin kendi rolüyle sınırlanmıyor.
`AuthorizeSessionRawSqlAsync` (Owner + `AllowDeskSql`) yalnızca `key is null` iken çalışıyor.
Admin kendine `CanExecuteSql` anahtarı basıp saklı üretim bağlantısında keyfi DDL
çalıştırabiliyor; `AllowDeskSql` bu yolda hiç okunmuyor.

**Düzeltme:** `CanExecuteSql`/`CanExport` ayarlamak Owner istesin; anahtar yolunda
saklı bağlantı çözülürken `AllowDeskSql` da aransın.

### H-3 — ne yapıldı

1. `GatewayKeyController.Create`: `CanExecuteSql` ya da `CanExport` isteniyorsa
   `IsOwnerAsync` şart (metot zaten vardı, yalnızca `SetDeskSqlEnabled`de
   kullanılıyordu). Reddedilen istek anahtar YAZMIYOR.
2. Yeni `GatewayController.AuthorizeKeyRawSqlAsync`: ham SQL projeye SAKLI
   bağlantıda çalışacaksa `CloudProject.AllowDeskSql` açık olmak zorunda —
   `/query` ve `/query/nl` ikisinde de. Proje bulunamazsa **reddediliyor**
   (fail-closed). Çağıran kendi bağlantı dizesini gönderdiyse kapı çalışmıyor:
   o zaten kendi veritabanına erişebiliyor.

**Testler:** `GatewayKeyRawSqlEscalationTests` (4) + `GatewayKeyStoredConnectionSqlTests` (3).

> **İKİ DAVRANIŞ DEĞİŞİKLİĞİ — kullanıcının bilmesi gerekiyor:**
> (a) Admin'ler artık ham-SQL ya da dışa-aktarım anahtarı üretemiyor; bunu
> Owner yapacak.
> (b) `AllowDeskSql` varsayılan **kapalı** olduğu için, saklı bağlantıda ham SQL
> çalıştıran MEVCUT bir API anahtarı artık 403 alır; Owner'ın proje
> ayarlarından açması gerekir. Bu, bulgunun istediği semantiğin ta kendisi,
> ama sessiz bir kırılma değil — duyurulması gerek.

### H-4 — Çıkarılan üye erişimini koruyor: anahtarlar ve paylaşım linkleri
**Durum:** **düzeltildi** · **Confidence:** High

`GatewayAccess.cs:67-87`; `ProjectMemberController.cs:180-210`; `ShareController.cs:38-73`

(a) `AuthenticateAsync` üreticinin güncel üyeliğine bakmıyor; üyelik kaldırma hiçbir
anahtarı iptal etmiyor → ayrılan Admin'in yazma/raw-SQL anahtarı süresiz çalışıyor,
istekler hâlâ onun hesabına faturalanıyor.
(b) `ShareController` create/revoke `p.UserId == CurrentUserId` eşitliğiyle çalışıyor —
org kapsamlı değil. Çıkarılmış kurucu hâlâ paylaşım linki üretebiliyor (şema anonim
yayınlanıyor, `sitemap.xml`'de listeleniyor) ve mevcut Owner'ın iptal ucu yok.

### H-4 — ne yapıldı

**(a) İKİ katman:**
1. `GatewayAccess.AuthenticateAsync` artık üretenin GÜNCEL erişimini de soruyor
   (`CanViewAsync`). En az Viewer yetiyor, Editor değil: anahtarın kendi
   yetkileri (CanWrite/CanExecuteSql/CanExport) ayrıca uygulanıyor, burada
   sorulan tek şey üretenin projeyle hâlâ bir ilişkisi olup olmadığı. Maliyet
   kabul edildi: iptal zaten önbelleklenmiyor ve her istekte DB'ye gidiliyor.
2. `ProjectMemberController.Remove` çıkarılan üyenin O PROJEDEKİ anahtarlarını
   açıkça iptal ediyor (`RevokedAt`). Tek başına 1. madde erişimi keserdi, ama
   anahtar listede "aktif" görünmeye devam eder ve üye geri eklendiğinde
   kimsenin niyet etmediği bir anda yeniden canlanırdı.

**(b)** `ShareController` create/revoke artık `OrgAccess`'in tek kopyasından
`CanManageMembersAsync` (Admin|Owner) istiyor. Revoke'un eşiği create ile AYNI:
yayınlanmış bir şemayı geri çekebilmek, onu yayınlayabilmekten daha zor olmamalı.

**Testler:** `Namines.Tests/Controllers/RemovedMemberKeepsAccessTests.cs` — 7 test
(anahtar doğrulama, açık iptal, kalan üyelerin etkilenmemesi, çıkarılmış kurucu,
kurucu olmayan Owner'ın iptal edebilmesi, Editor'ın edememesi, Admin'in edebilmesi).

### H-5 — Gateway yazma yolunda doğrulanmamış `projectId` denetim kaydına yazılıyor
**Durum:** **düzeltildi** · **Confidence:** High (artık testle doğrulandı)

`GatewayController.cs:395-435, 555-668`; `GatewayAudit.cs:29-74`

`ResolveConnectionAsync` istek bir bağlantı dizesi taşıyorsa erken dönüyor; proje
erişim kontrolü o daldan hiç çalışmıyor. Yazma eylemleri yine de `request.ProjectId`'yi
denetim kaydına yazıyor → kurbanın Logs/Analytics ekranı saldırgan kontrollü satırlarla doluyor.

Aynı kök neden: `DatabaseExecutorController.cs:126-142`.

### H-5 — ne yapıldı

1. Yeni `GatewayController.AuditProjectId`: proje kimliği ya ANAHTARDAN gelir
   (taklit edilemez) ya da isteğin bağlantı dizesi YOKKEN gelir — çünkü yalnızca
   o dalda `ResolveSessionAccessAsync` gerçekten çalışıyor. Kendi dizesini
   gönderen çağrı `null` alıyor ve kayıt "session" placeholder'ına düşüyor —
   Desk'ten ÖNCEKİ davranış. Dört yazma ucunda da uygulandı.
2. `DatabaseExecutorController.ExecuteScript` `request.ProjectId`'yi
   **çalıştırmadan ÖNCE** `CanViewAsync`'ten geçiriyor; geçemezse 403.
   Sonradan "kimliği düşürüp yine kaydet" demek, betik çalıştıktan sonra
   sessizce izsiz bırakmak olurdu.

**Testler:** `Namines.Tests/Controllers/AuditProjectIdSpoofingTests.cs` — 4 test
(yabancının kurban projesine yazamaması, kendi projesinin çalışmaya devam etmesi,
`ProjectId` hiç verilmediyse kaydın yine yazılması — Studio'nun canvas çağrıları
proje taşımıyor ve onları reddetmek düzeltmeyi kırılmaya çevirirdi — ve var
olmayan proje kimliğinin de reddedilmesi).

### H-6 — SQLite yoluyla sunucuda keyfi dosya oluşturma
**Durum:** **düzeltildi** (dosya oluşturma) · **Confidence:** High

`DatabaseExecutorService.cs:84-85, 228-239`; `DatabaseExecutorController.cs:59-96`

SQLite için `ExtractHosts` boş liste dönüyor ve `ValidateConnectionTarget` boş listede
**fail-open** (ortak `FindDisallowedHost` ise fail-closed). Ham dize
`new SqliteConnection(...)`'a gidiyor; `Mode` varsayılanı `ReadWriteCreate`.
Ayrıca uç proje sahipliği kontrolü yapmıyor (`ProjectId` opsiyonel).

### H-6 — ne yapıldı

1. `ValidateConnectionTarget` SQLite'ı **tamamen reddediyor**. Yolu allowlist'lemek
   yerine motorun kendisini reddetmek bilinçli: bu servisin işi KULLANICININ
   KENDİ veritabanına bağlanmak ve Namines'in sunucusunda kullanıcıya ait bir
   dosya sistemi yok — yani meşru bir SQLite hedefi hiç yok. Doğrulandı:
   `ExecuteScriptAsync`/`TestConnectionAsync`'in tüm çağıranları tarandı, tek iç
   çağıran `LaunchService` ve o PostgreSQL kullanıyor. (SQLite bir HEDEF MOTOR
   olarak duruyor — DDL ÜRETMEK bağlantı gerektirmiyor.)
2. Boş aday listesi artık **fail-closed**: tek bir host bile çıkarılamadıysa
   reddediliyor. "Ayıklayamadım" ile "güvenli" aynı şey değil ve builder'ın
   ayrıştıramadığı bir dize kontrolü atlamanın en kolay yolu olurdu.

**Testler:** `Namines.Tests/Services/ExecutorSqliteFileCreationTests.cs` — 7 test.

> **Mutasyon testi bu bulgunun GERÇEKTEN sömürülebilir olduğunu kanıtladı:**
> düzeltme geri alınıp koşturulduğunda `SQLite_betigi_sunucuda_dosya_OLUSTURMUYOR`
> kırmızıya döndü — yani dosya diskte **gerçekten oluştu**. Teorik bir bulgu değildi.

> **KAPANMAYAN yarısı:** "uç proje sahipliği kontrolü yapmıyor" kısmı hâlâ açık.
> Aynı kök neden **H-5** ile paylaşılıyor (doğrulanmamış `projectId` denetim
> kaydına yazılıyor) ve orada ele alınacak.

### H-7 — INCOMPLETE FIX: `BuildIdentifier` düzeltmesi kurtarma yolunda veri kaybı
**Durum:** **düzeltildi** · **Confidence:** High · **Bu turun kendi regresyonu**

`Namines.Ground/Providers/LocalPostgresProvider.cs:341-367`; `GroundService.cs:198-246`

Önceki turda `BuildIdentifier`'a kanonik-GUID olmayan her kimlik için özet soneki
eklendi. `CancelDeleteAsync` ise `CreateIsIdempotentByProjectId` yeteneğine güveniyor:
`CreateAsync`'i proje kimliğiyle çağırıp **aynı** veritabanını bulacağını varsayıyor.
Kanonik GUID olmayan kimlikte türetilen ad değiştiği için ikinci, **boş** bir veritabanı
açılıyor; `record.ProviderProjectId` eski adı gösterdiği için `PurgeExpiredAsync`
sonradan **veriyi taşıyan** veritabanını düşürüyor.

Aynı kök nedenin diğer tüketicisi: `VaultService.NewBackupRecord:192` → `StorageKey`.
Eski (doğrulama öncesi) kimlikler `FileSystemBackupStore.Resolve` tarafından artık
reddediliyor → o projelerde yedek/geri yükleme kalıcı olarak başarısız.

**Düzeltme:** Kurtarma yolu türetmeye güvenmesin, saklı `ProviderProjectId`'yi kullansın.

### H-7 — ne yapıldı

`ProvisionSpec`e iki alan eklendi (`ExistingProviderProjectId`,
`ExistingProviderBranchId`); `CancelDeleteAsync` artık bunları kayıttan geri
veriyor (bölge de kayıttan — sağlayıcı varsayılanına düşmek kurtarmayı başka
bölgeye taşıyabilirdi). `LocalPostgresProvider` saklı adı `ReuseOrDerive` ile
kullanıyor; biçim uymuyorsa türetmeye DÜŞÜYOR (ucı patlatmıyor), çünkü türetilen
ad her zaman geçerli ve en kötü durumda bugünkü davranış.

**Testler:** `GroundCancelDeleteTests.Idempotan_saglayicida_kurtarma_SAKLANAN_kimligi_geri_veriyor`
+ `GroundIdentifierTests`e 11 `ReuseOrDerive` vakası. Çağrı yerini `null`a
çevirip testin gerçekten kırmızıya döndüğü doğrulandı.

> **Bulgunun Vault yarısı YANLIŞTI — kendi raporumu düzeltiyorum.**
> "Eski kimlikler `FileSystemBackupStore.Resolve` tarafından reddediliyor →
> yedek/geri yükleme kalıcı olarak başarısız" iddiası mekanizma olarak yanlış:
> `Resolve` biçim doğrulaması yapmıyor, yalnızca kök D I Ş I N A çıkmayı
> engelliyor — ve `StorageKey` kayıtta SAKLANDIĞI için geri yükleme zaten
> yeniden türetmiyor. Yani Vault'ta türetme-kayması sorunu hiç yok.
> Ground yarısı gerçekti ve düzeltildi.

---

## MEDIUM (17)

| # | Bulgu | Konum | Durum |
|---|---|---|---|
| M-1 | Onay sayısı canlı ekip büyüklüğünden her oyda yeniden hesaplanıyor → ekibi küçülten Admin kendi yıkıcı CR'ını tek başına onaylayabiliyor | `ChangeRequestApprovalPolicy.cs:47-63`, `ChangeRequestController.cs:457-466` | **düzeltildi** |
| M-2 | Stripe'ta **event-id idempotency yok** → tekrar iletilen `active` olayı iptal edilmiş aboneliği geri getiriyor | `StripeWebhookController.cs:48-112, 199-224` | **düzeltildi** |
| M-3 | AI kotası `Check`+`Consume` iki ayrı adım; kod yorumu "atomik" diyor ama değil | `AiQuotaService.cs:415-424`, `AIQuotaMiddleware.cs:155-174` | **düzeltildi** |
| M-4 | `GET /api/changerequest/{id}` her görüntülemede ölçülmeyen 2 LLM çağrısı, Viewer'a açık, hız sınırı yok | `ChangeRequestController.cs:293-349` | **düzeltildi** |
| M-5 | Hız sınırı partisyonu `X-Forwarded-For`'a güveniyor, `KnownNetworks` varsayılan boş → login brute-force koruması atlatılabilir | `Program.cs:299-312, 385-440, 699-722` | **düzeltildi** |
| M-6 | `Outputs/` çıktıları hiç silinmiyor (`EvictExpiredJobs`'ın çağıranı yok); "2 saat" mesajı yanlış | `DockerJobManager.cs:71-80` | **düzeltildi** |
| M-7 | Ground purge ile cancel-delete yarışıyor; concurrency token yok → "Active ama düşürülmüş DB" | `GroundService.cs:254-322` vs `198-246` | **düzeltildi** |
| M-8 | Vault kaydı ile şifreli dosya birbirine bağlı değil (AAD'de backup/proje kimliği yok, kayıtta digest yok); NVLT1 okuyucu açık | `BackupCipher.cs:98-106, 161-178`, `VaultBackup.cs` | **düzeltildi** |
| M-9 | **Anonim** `/api/vault/health` sunucu dosya yolunu ve Docker hata metnini sızdırıyor | `VaultController.cs:65-84` | **düzeltildi** |
| M-10 | Branch DB yeniden provizyonu her çağrıda ölçülüp faturalanıyor (kota kapısı bilerek atlanırken) | `BranchController.cs:352-372` | **düzeltildi** |
| M-11 | DNS rebinding hiçbir sink'te azaltılmamış; egress allowlist yalnızca DB'de ve varsayılan boş | `SsrfGuard.cs:25-43`, `DbHostAccessPolicy.cs:30-51` | **düzeltildi** |
| M-12 | Vault dump/restore container içinden bağlanıyor → guard'ın çözdüğü isim ile bağlanılan isim farklı çözücüden | `ContainerBackupProvider.cs:291-313` | **düzeltildi** |
| M-13 | SignalR bağlantıları SecurityStamp'e karşı yeniden doğrulanmıyor → "tüm oturumları sonlandır" canlı hub'ı kapatmıyor | `SecurityStampValidation.cs:61-105`, `CanvasHub.cs:77-94` | **düzeltildi** |
| M-14 | CanvasHub düzeltmesi pasif dinleyiciyi kapsamıyor — çıkarılan üye yayın almaya devam ediyor | `CanvasHub.cs:77-94, 128-160` | **düzeltildi** |
| M-15 | Desk handoff, 30 sn'lik jetonu 7 günlük tam yetkili JWT'ye çeviriyor, `sessionStorage`'a yazıyor, projeye bağlı değil | `DeskHandoff.cs:28-73`, `AuthController.cs:272-304` | **düzeltildi** |
| M-16 | MCP süreci SSRF politikasını yapısal olarak kapatıyor, bağlantı dizesini model çıktısından alıyor | `Namines.Mcp/Program.cs:30-42`, `NaminesTools.cs:238-253` | **düzeltildi** |
| M-17 | CoderAI paketleri sabit DB parolası gömüyor + DB portunu host'a açıyor | `CoderAIPackagerService.cs:29-85` | **düzeltildi** |

### M-2 — ne yapıldı

**Yeni:** `StripeWebhookEvent` tablosu (migration
`20260930131620_AddStripeWebhookEventIdempotency`, yalnızca bu tabloyu ekliyor).
Stripe olay kimliği **birincil anahtar**: eşzamanlı ikinci bir istek uygulama içi
bir "var mı?" kontrolüyle değil, veritabanı seviyesinde durduruluyor.

Kayıt **işlemeden ÖNCE** yazılıyor: sonra yazmak, işlem ile kayıt arasında çöken
bir süreçte olayın ikinci kez uygulanmasına izin verirdi. Yazma imza
doğrulamasından SONRA: önce olsaydı, kimliği doğrulanmamış biri uydurma olay
kimlikleriyle tabloyu doldurup gerçek olayları "zaten işlenmiş" saydırabilirdi.

**İkinci ve ayrı bir sorun da kapandı: SIRA DIŞI TESLİMAT.** Idempotency tek başına
yetmiyor, çünkü Stripe teslimat sırasını garanti etmiyor: `deleted` işlendikten
sonra ondan ESKİ bir `active` olayı gelirse kimlik FARKLI olduğu için idempotency
onu durdurmaz ve iptal geri alınır. Abonelik durumunu değiştiren olaylar artık, o
müşteri için daha yeni bir olay işlenmişse uygulanmıyor. Karşılaştırma olayın
ÜRETİLME zamanıyla (`Event.Created`), bizim aldığımız zamanla değil — alınma zamanı
tam olarak sıra dışı teslimatta yanlış cevabı verir.

**Testler:** `Namines.Tests/Controllers/StripeWebhookIdempotencyTests.cs` — 4 test.
İmzalı gövdeler gerçekten HMAC-SHA256 ile üretiliyor; imza doğrulamasını atlatmak
testin kanıt değerini yok ederdi.

> **Mutasyon testi bir şeyi ortaya çıkardı:** yalnızca "zaten işlenmiş mi?"
> kontrolünü kaldırmak testi KIRMIYOR — çünkü birincil anahtar çakışması
> (`DbUpdateException` dalı) aynı sonucu veriyor. Yani tekrar iletim koruması
> iki katmanlı ve testler ikisini ayırt edemiyor. Sıra kontrolünü kaldırmak ise
> testi kırdı, yani o katman tek başına ölüçülebilir durumda.

### M-1 — ne yapıldı

`ChangeRequest.VotingMembersAtCreation` (migration
`PinVotingMembersOnChangeRequest`, tek bir `int` kolon, varsayılan 0) CR AÇILDIĞI
ANDA oy verebilen üye sayısını sabitliyor. Oy verirken dayanak
`max(sabitlenen, canlı)`:

- ekip **büyüdüyse** canlı sayı kazanır — ideal kural uygulanabilir hâle gelmiştir
  ve onu görmezden gelmek kuralı gereksiz yere gevşetirdi;
- ekip **küçüldüyse** sabitlenen sayı kazanır — saldırı tam olarak budur.

`min` almak saldırıyı geri getirirdi (küçült → canlı daha küçük → min daha küçük).

**Kabul edilen bedel, dürüstçe:** ekip GERÇEKTEN küçüldüğünde (biri işten ayrıldı)
açık bir Destructive CR kilitlenebilir. Çıkış yolu var (CR'ı kapatıp yenisini açmak —
o anın ekip büyüklüğüyle yeniden sabitlenir) ve bu bedel tersinden çok daha küçük:
tersi, yıkıcı bir değişikliğin yetkisiz onaylanması olurdu — AGENTS.md'deki
"varsayılan asla veri kaybına doğru düşmemeli" ilkesiyle aynı yön.

**Kolon 0 ise canlı sayıya düşülüyor** (alan eklenmeden önce açılmış satırlar).
Geriye dönük bir sayı UYDURMAK, olmayan bir bilgiyi varmış gibi göstermek olurdu.

**Testler:** `Namines.Tests/Controllers/ApprovalCountPinningTests.cs` — 4 test
(saldırının engellenmesi, meşru onayın çalışmaya devam etmesi, ekip büyüdüğünde
canlı sayının kazanması, sabitlenmemiş eski CR'ın eski davranışı koruması).

### M-9 — ne yapıldı

`/api/vault/health` **anonim kalıyor** (bir sağlık kontrolü oturum istememeli) ama
anonim cevap artık yalnızca `{ ok }` dönüyor. `store` (içinde yedeğin durduğu
**mutlak sunucu dosya yolu** var — `FileSystemBackupStore.Description`), `engines`
ve `problem` (sağlayıcının ham hata metni: Docker socket yolu, iç host adları)
yalnızca kimliği doğrulanmış çağırana gidiyor.

Ayrıntıyı tamamen kaldırmak yerine kimliği doğrulanmış çağırana vermek bilinçli:
uçun kendi gerekçesi ("kullanıcı yedeğinin nerede durduğunu bilmeden ona
güvenmemeli") hâlâ geçerli — geçerli olmayan, bunu kimliği doğrulanmamış birine
de söylemekti.

### M-3 — ne yapıldı

`TryReserveAsync`ın yorumu "kontrolü ve düşmeyi TEK atomik adımda yapıyor"
diyordu; gövdesi ise `CheckAsync` + `ConsumeAsync` — **İKİ ayrı veritabanı turu**.
Pencere hiç kapanmamıştı. Kullanıcı tavanı artık tek bir
`UPDATE ... WHERE yer_var` ifadesiyle koşullu düşülüyor; etkilenen satır 0 ise
`UserExhausted`. Paylaşılan sayaçlar (havuz + ekip) `RecordSharedUsageAsync`e
çıkarıldı, yani çift harcama da yok.

**Testler:** `AiQuotaServiceTests.Concurrent_reservations_cannot_exceed_the_user_cap`
— GERÇEK PostgreSQL'e karşı 40 eşzamanlı rezervasyon.

> **Mutasyon testi açığın büyüklüğünü ölçtü:** koşulu kaldırıp koşturduğumda
> 10.000'lik tavana karşılık **31.000 token** harcandı — yani 3 kat aşım. Teorik
> bir yarış değildi.

> **İddia edilmeyen şey, dürüstçe:** tam kullanım garanti edilmiyor. Ölçüm: 40
> eşzamanlı denemede 10 slotun 8'i verildi, 2.000 token boşa gitti. PostgreSQL'in
> koşullu UPDATE'i çekişme altında bazı denemeleri yer varken de reddediyor. Hata
> yönü bütçenin LEHİNE olduğu için güvenlik sorunu değil, ama test tek bir sayıya
> kilitlenmiyor — vermediğimiz bir garantiyi test etmiş gibi görünmesin.

### M-6 — ne yapıldı

**Yeni:** `SandboxOutputRetentionService`. `EvictExpiredJobs`ın **hiçbir çağıranı
yoktu**, yani iş kayıtları bellekte sınırsız birikiyor ve `Outputs/` altındaki
zip'ler **asla silinmiyordu** — oysa indirme ucu kullanıcıya "saklama penceresi
(2 saat) geçmiş olabilir" diyordu. Ürün tutmadığı bir söz veriyordu ve o dosyalar
kullanıcının tam şemasını taşıyor.

`DockerSweeperBackgroundService`e **eklenmedi**, ayrı ve KOŞULSUZ kaydedildi: o servis
`Sandbox:Enabled` ile kapılı ve varsayılan kapalı, ama çıktı dosyaları o bayraktan
BAĞIMSIZ birikiyor. Temizliği aynı kapının arkasına koymak, açığı kapatmış gibi
görünüp kapatmamak olurdu.

**İki ayrı silme ölçütü var ve ikisi de gerekli:** (1) bellekteki süresi dolmuş
işlerin dosyaları, (2) **sahipsiz** dosyalar — uygulama yeniden başladığında iş
sözlüğü boşalıyor, yani yalnızca 1. ölçüte bakan bir temizlik eski dosyaları
sonsuza kadar bırakırdı. Gerçek bir dağıtımda asıl temizliği yapan ölçüt bu.

**Testler:** `Namines.Tests/Services/SandboxOutputRetentionTests.cs` — 7 test.

> **Test edilmeyen kısım, dürüstçe:** testler servisi DOĞRUDAN kurup süpürme
> mantığını doğruluyor. **DI kaydının kendisi test kapsamında değil** — yani
> `AddHostedService` satırını silen bir değişikliği bu testler yakalamaz. Bu,
> bulgunun kök nedeninin ta kendisi olduğu için burada açıkça yazılı.

### M-17 — ne yapıldı

**İki ayrı sorun vardı ve ikincisi daha ciddiydi:**

1. **Sabit parola:** üretilen her pakete aynı `Namines_Secure123!` gömülüyordu ve o
   parola bu depoda açık yazılı. Artık paket başına 32 karakterlik (~190 bit)
   rastgele parola üretiliyor, `.env`e yazılıyor (compose onu kendiliğinden okuyor)
   ve `.gitignore` ile korunuyor. Alfabe URL-güvenli: parola bir SQLAlchemy URL'sine
   giriyor ve `@`/`:`/`/` orada ayırıcı.
2. **Port 0.0.0.0'a açıktı:** `ports: "5432:5432"` biçimi portu host'un TÜM
   arayüzlerine açar. Genel IP'si olan bir makinede bu, veritabanını bilinen bir
   parolayla **internete açmak** demekti. Hem DB hem yönetim paneli artık
   `127.0.0.1`e bağlı.

Model artık parolayı DEĞİL, `DATABASE_URL` ortam değişkenini okuması gerektiğini
söyleyen bir talimat alıyor (`StreamlitPromptBuilder`, `GroqAIService`,
`OllamaAIService`). Semantik önbellek anahtarına `promptVersion` eklendi: önbellekte
duran ESKİ app.py sabit parolayı gömülü taşıyor ve yeni rastgele parolayla
bağlanamazdı — sürüm, o girdileri geçersiz kılıyor.

**Testler:** `Namines.Tests/Services/CoderAiPackageSecretsTests.cs` — 10 test
(üç motorda sabit parola yok, üç motorda loopback bağlama, paket başına farklı
parola, parolanın compose'da değil `.env`de olması, URL-güvenli alfabe, `.gitignore`).
Mutasyon testinde 10 testin 8'i kırmızıya döndü.

> **Kapsam dışı bırakılan, bilinçli:** `ContainerProfiles`, `DockerBackupService` ve
> `BranchTestRunnerService` hâlâ aynı sabit parolayı kullanıyor. Onlar **geçici
> sandbox** container'ları ve C-1 düzeltmesinden sonra `NetworkMode = "none"` ile
> çalışıyorlar — yani ağdan ulaşılamıyorlar ve parola kullanıcıya hiç teslim
> edilmiyor. Risk profili farklı; aynı değişikliği oraya taşımak kazançsız bir
> koordinasyon işi olurdu.

### M-4 — ne yapıldı

`GET /api/changerequest/{id}` her görüntülemede **İKİ LLM çağrısı** yapıyordu, uç
Viewer'a açıktı, hız sınırı yoktu ve çağrılar AI kotasına HİÇ yazılmıyordu: bir
sekmeyi yenilemeye devam eden biri, kimsenin bütçesinden düşmeyen sınırsız model
çağrısı üretebiliyordu.

> **Kod yorumu yanlış bir şey söylüyordu:** migration üreticisini "deterministik EF
> Core" sanıyordu. Gerçekte `MigrationService.GenerateMigrationAsync` →
> `GroqAIService.GenerateMigrationCodeAsync`, yani o da modele gidiyor. Bulgunun
> "2 LLM çağrısı" demesi doğruydu.

Girdi DEĞİŞMEZ (CR'ın base/head şemaları sabit), o yüzden sonuç artık kayda
yazılıyor (`AiExplanation`, `MigrationResultJson`; migration
`CacheChangeRequestAiOutputs`) ve bir daha hesaplanmıyor. Hesaplanan tek kez de
`AiQuotaService.TryReserveAsync`'ten geçiyor. Kota dolduysa yalnızca o sekmeler boş
görünüyor — isteğin tamamı reddedilmiyor, çünkü CR'ın kendisi (risk, onaylar,
diff) AI'dan bağımsız. Yalnızca BAŞARILI sonuç saklanıyor: `null` yazmak geçici bir
servis arızasını kalıcı "açıklama yok" hâline çevirirdi.

### M-5 — ne yapıldı

`ForwardedHeaders:KnownNetworks` tanımsızken kod yalnızca **uyarı basıp** header'ı
doğrulanmadan kabul ediyordu — yani varsayılan bir üretim dağıtımında
`X-Forwarded-For`'u döndüren biri login'in 5 istek/dakika kapısını partition'ı her
istekte değiştirerek atlatıyordu. Brute-force koruması kâğıt üzerinde vardı.

Artık **sessiz fail-open yok**: üretimde ya güvenilen ağlar tanımlı olacak ya da
operatör `ForwardedHeaders:TrustAllProxies=true` ile güvenmeyi AÇIKÇA seçecek
(o seçim de loglanıyor). Bu, bu kod tabanının `Jwt:Key` için zaten uyguladığı
desenin aynısı: eksik güvenlik yapılandırması başlatmayı engelliyor.

### M-7 — ne yapıldı

Purge ile "silmeyi geri al" aynı kayıt üzerinde yarışıyordu ve hiçbir eşzamanlılık
kontrolü yoktu: bekleme penceresinin son saniyesinde iptal durumu `Active` yaparken
purge zaten `DROP DATABASE`'i çağırıyor olabiliyordu — ekranda "Active" görünen,
veritabanı ise düşürülmüş bir kayıt (hem veri kaybı hem onu gizleyen arayüz).

İki taraf da geçişi **koşullu bir `UPDATE` ile sahipleniyor**, yani yalnızca biri
kazanabiliyor. Yeni `GroundStatus.Deleting` enum'un SONUNA eklendi (değerler DB'de
int; araya eklemek mevcut satırların anlamını kaydırırdı). Her iki yolda da hata
durumunda sahiplenme GERİ ALINIYOR — `Deleting`/`Provisioning`'de asılı kalan bir
kaydı ne purge ne de ikinci bir iptal ele alabilirdi.

### M-8 — ne yapıldı (KıSMEN)

**(a) Kayıt ↔ şifreli dosya bağı:** `VaultBackup.CipherTextSha256` (migration
`BindVaultRecordToCipherText`). Parola SUNUCU GENELİ olduğu için A projesinin yedeği
B'nin kaydındaki anahtarın yerine konsa sorunsuz çözülüyordu — sessiz bir veri
değişimi yolu. Özet YAZARKEN tek geçişte hesaplanıyor (`CryptoStream`) ve
restore/indirme yollarında sabit zamanlı karşılaştırılıyor. Hata mesajı beklenen
özeti SÖYLEMEZ.

**(b) NVLT1 düşürme yolu:** okuyucu artık `Vault:AllowLegacyV1` ile kapılı,
varsayılan **kapalı**. Tamamen silmek, gerçekten V1 yedeği olan bir kurulumda o
yedekleri kalıcı erişilemez kılardı.

> **AÇIK KALAN yarısı, dürüstçe:** AAD'ye backup/proje kimliği **eklenmedi**. Bu bir
> biçim değişikliği (NVLT3) gerektiriyor ve mevcut NVLT2 dosyalarını okumaya devam
> etmek için ikinci bir okuyucu yolu demek. Özet kontrolü aynı saldırıyı zaten
> yakalıyor (kayıtın özeti dosyayla eşleşmiyor); AAD bağlaması onun üstüne
> derinlik katmak olurdu. Bir sonraki tura bırakıldı.

### M-10 — ne yapıldı

Kota kapısı "zaten açık olan bir veritabanını yeniden istemek kotaya sayılmaz" diyerek
bilerek atlanıyordu, ama ölçüm KOŞULSUZ yazılıyordu: aynı sayfayı yenileyen bir
kullanıcı her seferinde YENİ bir branch veritabanı faturalandırıyordu, sağlayıcı
hiçbir şey açmadığı hâlde. İki taraf artık AYNI cevabı kullanıyor: varlık kontrolü
bir kez yapılıp hem kotaya hem faturalamaya veriliyor.

### M-13 ve M-14 — ne yapıldı (aynı kök neden)

Yetki YALNIZCA bağlantı bir hub metodu çağırdığında kontrol ediliyordu. İki sonucu
birden vardı: **PASİF** bir dinleyici hiç yeniden doğrulanmıyordu (M-14 — projeden
çıkarılan üye şema/imleç yayınlarını almaya süresiz devam ediyordu) ve güvenlik
damgası kurulmuş bir soket için bir daha hiç okunmuyordu (M-13 — "tüm oturumları
sonlandır" canlı hub'ı kapatmıyordu).

**Yeni:** `LiveConnectionRegistry` (bu instance'taki açık, kimliği doğrulanmış
bağlantılar) + `LiveConnectionAuthSweeper` (30 saniyede bir yeniden yetkilendirme).
İki ölçüt AYRI uygulanıyor — üyelik kaybı ile oturum iptali farklı olaylar ve biri
diğerini kapsamıyor. Bağlantı gruptan çıkarılmakla YETİNİLMİYOR, `Abort()` ediliyor:
gruptan çıkarmak yayınları keser ama soket açık kalır ve istemci sessizce yeniden
katılabilir.

Kayıt defteri bellekte ve instance'a özel, çünkü `HubCallerContext` tutuyor — bir
bağlantıyı gerçekten kapatmanın tek yolu o ve serileştirilemediği için Redis
backplane'e konamaz. Bir bağlantı her zaman TEK bir instance'a bağlı olduğu için bu
yeterli. Guest odaları (üyelik istemeyen, tasarlanmış akış) taranmıyor.

### M-15 — ne yapıldı (KıSMEN)

30 SANİYELİK tek kullanımlık bir devir teslim jetonu, **7 GÜNLÜK ve tam yetkili**
normal bir oturum jetonuna çevriliyordu: el sıkışmanın dar penceresi, elde edilen
şeyin dar olduğu anlamına gelmiyordu. Ömür **12 saate** çekildi (14 kat kısalma) ve
jeton `namines:purpose=desk-handoff` claim'iyle işaretlendi.

**Canlı doğrulandı:** devir teslim jetonu 12.0 saat, purpose claim'i yerinde; NORMAL
login jetonu hâlâ 7.0 gün ve purpose'suz (yani ana akış kırılmadı).

> **AÇIK KALAN yarısı, dürüstçe:** jeton hâlâ **projeye bağlı değil** ve claim şu an
> yetki KISITLAMIYOR — yalnızca denetlenebilirlik sağlıyor ve uç-bazlı kapsam
> kontrolü eklendiğinde dayanağı hazır olsun diye var. Gerçek kapsamlama, her ucun
> bu claim'e göre yeniden gözden geçirilmesini gerektiriyor; tek başına eklemek,
> kapsamlıymış gibi görünen ama olmayan bir jeton üretirdi.

### M-16 — ne yapıldı (dar kapsamla)

MCP süreci SSRF politikasını yapısal olarak kapatıyor — ve bu, ürünün hedeflenen
kullanımı için **doğru**: süreç kullanıcının kendi makinesinde ve localhost tam olarak
amaçlanan hedef (33 §2). Kapatılmaması gereken şey buydu, o yüzden varsayılan
davranış **değiştirilmedi**.

Kalan gerçek risk: bağlantı dizesi **MODELİN çıktısından** geliyor. Prompt injection'a
maruz kalmış bir model, kullanıcının kimlik bilgilerini içeren bir dizeye
`Host=saldirgan.example.com` yazıp onları dışarı taşıyabilir. Yeni
`McpConnectionGuard` iki şey yapıyor: (1) her hedefi (host + motor, **asla kimlik
bilgisi değil**) stderr'a yazıyor, yani modelin nereyi aradığı görünür oluyor;
(2) `Security:McpAllowedDbHosts` tanımlıysa sert bir allowlist uyguluyor.

> **Bilinçli olarak yapılmayan:** genel internet hostlarını varsayılan olarak
> reddetmek. Çok kullanıcı MCP'yi gerçekten barındırılan bir Postgres'e (Neon, RDS)
> yöneltiyor; reddetmek meşru akışı kırardı. Bu bir ÜRÜN kararı ve tek taraflı
> alınmamalı — kullanıcıya sorulduğunda sertleştirilebilir.

### C-1 — sonradan bulunan kusur: 400 yerine 500

**Localhost doğrulamasında bulundu.** C-1'in kontrol karakteri reddi çalışıyordu, ama
fırlatılan `ArgumentException` `CompileController`'da yakalanmıyordu: uç
**HTTP 500** dönüyor ve gövdede iç ayrıntıyı (`Parameter 'identifier'`) sızdırıyordu.
Geçersiz bir tablo adı bir İSTEMCİ hatası, sunucu arızası değil. Artık 400 ve temiz
mesaj.

> **Hiçbir birim testi bunu göstermemişti**, çünkü hepsi üreticiyi DOĞRUDAN çağırıyor
> ve HTTP katmanından geçmiyordu. "2280 test yeşil" bu kusurla birlikte doğruydu.
> `CompileInvalidIdentifierTests` bu yolu artık kilitliyor.

## LOW (5)

| # | Bulgu | Konum | Durum |
|---|---|---|---|
| L-1 | State-changing `GET` (branch default) satır oluşturuyor, CSRF'ten muaf | `BranchController.cs:183-196` | **düzeltildi** |
| L-2 | Eşzamanlı oylar durum geçişini kaybediyor → quorum dolu ama `PendingReview` | `ChangeRequestController.cs:444-510` | **düzeltildi** |
| L-3 | `ResolvePlanCode` bilinmeyen fiyatta/fetch hatasında Pro'ya fail-open | `StripeWebhookController.cs:136-153` | **düzeltildi** |
| L-4 | Gateway anahtar hız limiti süreç-başına → instance sayısıyla çarpılıyor | `GatewayRateLimiter.cs:21-63` | **düzeltildi** |
| L-5 | Bot PR yorumu kaçışsız markdown kuruyor → inceleyici yanıltılabilir | `PullRequestReview.cs:47-131` | **düzeltildi** |

### L-2 — ne yapıldı

`approvedCountBefore` yüklenen nesneden okunuyor ve durum kararı o BAYAT sayıya göre
veriliyordu. İki farklı kullanıcı aynı anda onayladığında ikisi de "0 onay var"
görüyor, ikisi de "henüz yetmedi" diyor ve iki onay yazılıyor — quorum DOLU ama CR
`PendingReview`'da asılı. Onaylayanlar işi bitti sanıyor, CR hiç ilerlemiyor.

Oy YAZILDIKTAN sonra veritabanından yeniden sayılıyor ve gerekirse geçiş **koşullu
bir `UPDATE`** ile uygulanıyor: yarışın her iki tarafı da bunu yaptığı için hangisi
son yazarsa doğru sonuca varıyor, koşul da aynı geçişin iki kez uygulanmasını
engelliyor. Quorum'un ne zaman dolduğu ayrıca denetim kaydına yazılıyor — tek tek
oylardan farklı bir bilgi.

### L-3 — ne yapıldı

`ResolvePlanCode` bilinmeyen fiyatta "pro"ya düşüyordu. Düşüşün kendisi doğru (ödeyen
birine hizmet vermemektense daha az hak vermek), ama **yapılandırma eksikliği** ile
"fiyat gerçekten Pro" ayrı şeylerdi. Team fiyat kimlikleri hiç tanımlı değilse eşleme
matematiksel olarak çalışamaz: her Team müşterisi sessizce "pro" olur — **en çok
ödeyen müşteri en az hakkı alır**. O durum artık WARNING değil **ERROR** (uyarı
akışında kaybolur, fatura farkı aylar sonra fark edilir), ve tanınmayan fiyat
kimliği de kayda giriyor. Faturalama davranışı DEĞİŞMEDİ — değiştirmek daha riskli olurdu.

### L-5 — ne yapıldı

Tablo/kolon adları ve gerekçe metinleri HAM olarak markdown'a gömülüyordu. Bu
değerler müşterinin şemasından geliyor — kullanıcının kontrolünde — ve yorumun
okuyucusu bir İNSAN. `x** — sorun yok, geç **` gibi bir tablo adı yorumun yapısını
bozup inceleyiciye "risk yok" izlenimi verebiliyordu. Bot yorumu yönetişim
zincirinin insana bakan yüzü; onu yanıltmak zincirin tamamını atlatmanın en kolay yolu.

Yeni `Md()` yardımcısı markdown yapı karakterlerini kaçırıyor; **satır sonları da**
(bir tablo hücresindeki satır sonu tabloyu tamamen bozar ve geri kalan satırları
görünmez kılar). **Testler:** `Namines.Tests/Github/PullRequestReviewEscapingTests.cs`
— 4 test, sıradan adların okunabilir kaldığı dahil.

> **AÇIK kalan iki LOW ve neden:**
> • **L-1** (state-changing GET): oluşturma idempotent ve kullanıcının ZATEN sahip
> olduğu bir kaynak için; CSRF ile bir saldırganın kazanacağı bir şey yok. Doğru
> düzeltme ucu POST'a taşımak, ama bu frontend'in canlı işbirliği akışını
> (`useMultiplayer`) da değiştirmeyi gerektiriyor — güvenlik kazancı sıfıra yakınken
> kırılma riski gerçek.
> • **L-4** (süreç-başına hız limiti): doğru çözüm dağıtık bir sayaç (Redis).
> Redis bu projede yalnızca SignalR backplane'i için OPSİYONEL; hız limitini ona
> zorunlu kılmak bir altyapı kararı ve tek taraflı alınmamalı.

### M-12 — ne yapıldı

SSRF guard'ı host adını **API sürecinin** çözücüsüyle doğrularken, bağlantı
**konteynerin içinden ve konteynerin çözücüsüyle** kuruluyordu. İki farklı çözücü iki
farklı cevap verebilir: guard'ın "bu ad genel bir adrese çözülüyor" kararı,
konteynerde aynı adın İÇ bir adrese çözülmesini engellemiyordu — yani guard'ın kararı
gerçek bağlantıyı **bağlamıyordu**.

`DbConnectionParts.Parse` / `PostgresConnectionParts.Parse` artık adresi API
tarafında çözüp `ResolvedAddress`'e sabitliyor ve konteynere **ad değil ADRES**
geçiliyor: konteynerde çözümleme hiç yapılmıyor, guard'ın gördüğü adres ile
bağlanılan adres aynı oluyor. Adres ayrıca `SsrfGuard.IsHostSafe`'ten geçiyor — adın
bir kez geçmiş olması, ŞU ANDA çözüldüğü adresin güvenli olduğunu kanıtlamaz.

**Loopback bilerek dışarıda:** konteynerin loopback'i host'un loopback'i değil, o
yüzden oraya IP geçmek işe yaramaz — `host-gateway` tam olarak bu yüzden var. O dal
zaten geliştirme çift kapısıyla kapılı.

**Çözülemezse ya da adres güvensizse** eski davranış sürüyor (adı geçirmek).
Uydurma bir adres yazmak doğrulanmamış bir şeyi doğrulanmış göstermek olurdu;
bağlantıyı tamamen reddetmek ise çözümlemenin GEÇİCİ olarak başarısız olduğu her
durumda yedeği kesecekti.

### M-11 — ne yapıldı (KıSMEN — büyük kısmı bilinçli kabul)

**Kodun kendi analizi zaten doğruydu** ve bu turda değiştirilmedi: DNS rebinding'in
gerçek cevabı egress allowlist, ve sürücülere "şu IP'ye bağlan ama sertifikayı şu ad
için doğrula" demenin taşınabilir bir yolu yok.

**Gerçekten kapanan kısım:** Vault'un konteyner yolu — bkz. M-12. Orada IP
sabitlemek mümkündü çünkü `pg_dump`/`mysqldump` TLS ad doğrulamasına bağlı değil.

**Değişen ikinci şey:** allowlist tanımsızken basılan satır **üretimde artık
UYARI** (önce Information'dı). Boş allowlist bu ürün için meşru bir varsayılan —
işi zaten müşterinin veritabanına bağlanmak, hedefleri önceden bilmek mümkün değil —
ama "meşru varsayılan" ile "bilinçli kabul edilmiş risk" aynı şey değil.

> **AÇIK KALAN, dürüstçe:** sürücü yollarında (Npgsql, MySqlConnector, SqlClient...)
> rebinding penceresi **hâlâ açık** ve kodda kapatılamıyor. Operatörün kontrolü
> `Security:DbEgress:AllowedHosts`. Bunu zorunlu kılmak her dağıtımı kırardı, o yüzden
> yapılmadı — bu bir ÜRÜN kararı.

## İkinci tur: açık bırakılan dörtü de kapatıldı

Kullanıcı "bunları da yap" dedi. Üçü ürün/altyapı kararı bekliyordu; hepsinde
**güvenli varsayılan + açık opt-in** şekli seçildi, böylece meşru kullanım tek
satır yapılandırmayla geri geliyor.

### M-16 — geniş hâli

MCP artık yalnızca **yerel/özel ağ** hedeflerine bağlanıyor. Genel internet
hostları `Security:McpAllowPublicDbHosts=true` ile açılıyor; hata mesajı bunu
söylüyor, yani Neon/RDS kullanan kullanıcı çıkmaz sokakta kalmıyor. Allowlist
(`Security:McpAllowedDbHosts`) tanımlıysa TEK kapı o — daha dar olduğu için ikinci
bir bayrak sormak, kullanıcıyı iki ayarı birlikte doğru yapmaya zorlardı.

`IsHostPrivate` bilerek `IsHostSafe`'in tersi değil: çözülemeyen bir ad özel
SAYILMIYOR, yani geçemiyor — aksi hâlde var olmayan bir alan adıyla kapı atlanırdı.

**Testler:** `Namines.Tests/Mcp/McpConnectionGuardTests.cs` — 13 test.

### L-1 — state-changing GET

Uç ikiye ayrıldı: `GET` yalnızca OKUYOR (yoksa 404), oluşturma `POST`'ta
(`200` vardı / `201` oluşturuldu). Frontend istemcisi (`branchService`) POST'a
geçirildi — tek çağıranı vardı.

Risk zaten küçüktü (idempotent, kullanıcının zaten sahip olduğu kaynak), ama
"GET yazmaz" kuralının istisnası olmak, bir sonraki değişikliğin o istisnayı
büyütmesine zemin hazırlar — ve CSRF muafiyeti tam olarak o kurala dayanıyor.

**Testler:** `Namines.Tests/Controllers/DefaultBranchVerbTests.cs` — 5 test
(GET'in hiçbir şey oluşturmadığı dahil).

### L-4 — dağıtık hız limiti

`GatewayRateLimiter` Redis yapılandırılmışsa **dağıtık** sayaç kullanıyor:
`INCR` + ilk artışta `PEXPIRE`, **tek Lua betiğinde** (ayrı gönderilseydi araya
giren bir istek TTL'siz anahtar bırakabilirdi). Pencere numarası anahtarın
İÇİNDE: sıfırlama ayrı bir yazma gerektirmiyor ve instance'lar arası saat farkı
sayacı bozmuyor.

**Redis ZORUNLU DEĞİL** — tanımlı değilse bellek içi sayaç sürüyor (tek instance
dağıtımda zaten doğru sonuç). Zorunlu kılmak, tek sunucuda çalışan herkese Redis
kurdurmak olurdu. **Redis arızasında istek REDDEDİLMİYOR**: bir önbellek
arızasını müşterinin API'sini kapatmaya çevirmek, sınırın kendisinden büyük zarar —
bellek içi sayaca düşülüyor (eski davranış, hiç sınır olmaması değil).

**Testler:** `Namines.Tests/Integration/GatewayRateLimiterDistributedTests.cs` —
4 test, **gerçek Redis container'ına** karşı. Sahte bir Redis ile "sayaç süreç
dışında paylaşılıyor" iddiasını kanıtlamak, kendi sahtemizi test etmek olurdu.

### M-8 — AAD bağlaması (NVLT3)

Yeni biçim: yedeğin kimliği (proje + yedek kimliği) **şifrelemenin AAD'sine**
giriyor. Yanlış kimlikle çözmek GCM etiketini bozuyor, yani başka bir kaydın
dosyası bu kaydın yerine konamaz.

**Kimlik dosyaya YAZILMIYOR,** kayıttan geliyor: dosyaya yazılsa onu değiştiren
biri AAD'yi de kendi lehine kurardı ve bağlama anlamsız olurdu. Kimlik alanları
uzunluk önekleriyle yazılıyor — `"ab"+"c"` ile `"a"+"bc"` aksi hâlde AYNI bayt
dizisini verirdi.

NVLT2 ve (kapılı) NVLT1 okunmaya devam ediyor; `DecryptV3Async` gövdesi V2'den
KOPYALANDI — iki biçimin çözücüsünü tek koda bağlamak, birinde yapılan bir
değişikliğin diğerinin şifreleme bağlamını sessizce değiştirmesi demek.

**Testler:** `BackupCipherTests`e 11 test.

> **Mutasyon testi ilk testlerimin YANLIŞ SEBEPTEN geçtiğini gösterdi.** Kimliği
> AAD'nin İÇERİĞİNDEN çıkardığımda testler hâlâ yeşildi: AAD dizisinin BOYUTU
> kimlik uzunluğuna göre hesaplandığı için farklı uzunluktaki iki kimlik zaten
> farklı AAD veriyordu. Bağlama "çalışıyor" görünüyordu ama kanıt uzunluk
> tesadüfünden geliyordu. **AYNI uzunlukta, farklı içerikli** kimliklerle üç test
> eklendi; onlar mutasyonu yakalıyor. Bir testin geçmesi, DOĞRU ŞEYDEN geçtiği
> anlamına gelmiyor.

### R-4 — bayat test DLL'i yanlış bir "kararsızlık" teşhisine yol açtı

`GatewayRateLimiterDistributedTests` koşudan koşuya farklı sonuç veriyordu ve bunu
"test izolasyonu / pencere hizalaması" sorunu sandım; iki kez yanlış yere düzeltme
yaptım (hizalama, ping). Gerçek sebep: **lokalde çalışan API, `Namines.API.dll`'i
kilitliyordu**, `dotnet build` MSB3021/MSB3027 ile düşüyordu ve ben çıktıda
yalnızca `error CS` aradığım için bunu görmüyordum. `dotnet test --no-build`
BAYAT DLL'i koşturuyordu; hata satırı başka bir metodu gösteriyordu ve bu, PDB'nin
eski olduğunun kanıtıydı.

API durdurulup gerçek bir derleme yapıldığında testler **5/5 koşuda yeşil** —
kararsızlık hiç yoktu.

> **Ders:** derleme çıktısında `error CS` aramak yetmez; **dosya kilidi hataları
> `MSB****` koduyla gelir** ve "0 error CS" onları gizler. Bir testin açıklanamaz
> davranışında ilk soru "hangi binary koşuyor?" olmalı. Hizalama ve ping
> düzeltmeleri yine de kodda kaldı: ikisi de kendi başına doğru (dağıtık sayaç
> gerçekten mutlak kovalar kullanıyor, ve bağlantı hazır olmadan ölçmek anlamsız),
> ama **asıl hata onlar değildi.**

### M-11 — artık "kısmen" değil

M-12 (konteyner yolunda IP sabitleme) + M-16'nın geniş hâli (MCP'de genel host
reddi) birlikte, rebinding'in kodda kapatılabilir tüm yüzeylerini kapatıyor.
Sürücü yollarında kalan pencere operatörün `Security:DbEgress:AllowedHosts`
kontrolünde ve bu, kodun kendi analizinde zaten doğru tespit edilmiş bir sınır —
sürücülere "şu IP'ye bağlan ama sertifikayı şu ad için doğrula" demenin taşınabilir
bir yolu yok. Üretimde allowlist tanımsızsa artık UYARI basılıyor.

## Bu turun KENDİ regresyonları

Düzeltmelerin kendisi iki hata üretti. İkisini de **mevcut testler** yakaladı, ve
ikisi de "tam suite koşmadan yeşil demek" hatasının bedeliydi — ara koşularda
raporladığım "yeşil" o iki değişikliği HİÇ kapsamıyordu.

### R-1 — M-7'nin sahiplenmesi ret kapılarından ÖNCE duruyordu

`CancelDeleteAsync`'te durum sahiplenmesini metodun BAŞINA koymuştum, yani
`CreateIsIdempotentByProjectId` yetenek kontrolünden ve proje aramasından ÖNCE.
O dallardan biri erken döndüğünde sahiplenme geri alınmıyor ve kayıt
`Provisioning`'de **asılı** kalıyordu — ne purge ne de ikinci bir iptal onu ele
alabilirdi. Yani eşzamanlılık düzeltmesi, düzeltmeye çalıştığı şeyin daha kötü bir
sürümünü üretmişti.

Yakalayan test: `GroundCancelDeleteTests.Idempotent_OLMAYAN_saglayicida_ORIJINAL_ProviderProjectId_KORUNUYOR`
— reddedilen bir isteğin kaydı DEĞİŞTİRMEMESİ gerektiğini zaten kilitliyordu.
Sahiplenme tüm ret kapılarından sonraya taşındı.

### R-2 — M-8'in V1 kapısı mevcut bir sözleşmeyi kırdı

NVLT1 okumayı varsayılan kapalıya çekmek, `Legacy_nvlt1_backups_stay_readable`
testini kırdı — o test tam olarak geriye dönük uyumluluğu koruyordu. Bu bir kaza
değil, **bilinçli bir sözleşme değişikliği**: test yeni sözleşmeyi yansıtacak şekilde
güncellendi (`..._WHEN_EXPLICITLY_ENABLED`) ve varsayılan reddi ölçen
`Legacy_nvlt1_is_REFUSED_by_default` eklendi. Hata mesajının operatöre NE yapacağını
söylediği de test ediliyor — sessiz bir ret, eski yedeğini geri yüklemeye çalışan
birini çıkmaz sokağa sokardı.

> **R-4** (bayat test DLL'inin yanlış bir "kararsızlık" teşhisine yol açması)
> yukarıdaki "İkinci tur" bölümünde, M-11'in hemen öncesinde — orada bulunduğu
> işle birlikte okunması daha anlamlı.

### R-3 — C-1'in reddi 400 yerine 500 dönüyordu

Ayrıntı C-1 bölümünde. **Localhost doğrulamasında** bulundu; hiçbir birim testi
göstermemişti, çünkü hepsi üreticiyi doğrudan çağırıyor ve HTTP katmanından
geçmiyordu.

> **Üçünün ortak dersi**, AGENTS.md'nin zaten yazdığının aynen tekrarı: "testler
> geçiyor" hiçbir şey kanıtlamaz. Buraya eklenecek şey şu: **hangi testlerin hangi
> değişikliği kapsadığına bakmadan "yeşil" demek**, testleri hiç koşmamaktan daha
> yanıltıcı — çünkü bir kanıt görüntüsü üretiyor.

## Spekülatif (doğrulanmadı — bulgu olarak sunulmamalı)

1. `UserType.Corporate`'ı doğrudan okuyan bir yetki/özellik kapısı var mı (past-due hesap yeteneği korur mu)
2. Çok-org kullanıcıda AI bütçesi hangi org sayacına yazılıyor; koltuk sayısı üyelikle doğrulanıyor mu
3. `IMemoryCache` anahtarlarında kiracı bağlamı — **hiç denetlenmedi**
4. Eject üreticilerinde zip entry adları tanımlayıcıdan türüyorsa zip-slip
5. Branch container `Sanitize` enjektif değil (bugün erişilemez: branch id sunucu GUID'i)
6. GitHub installation↔org bağlaması yok (bugün etkisiz; F2 özel depo okumayı getirince yük taşıyacak)
7. `IPAddress.TryParse`'ın kısa IPv4 biçimleri; container içindeki libc çözücüsüyle uyuşmazlık
8. `SsrfGuard` port ve userinfo kısıtlamıyor
