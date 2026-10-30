# Control DB sağlamlık denetimi (PostgreSQL + EF Core 8)

> Kapsam: uygulamanın KENDİ veritabanı (`AuthDbContext`, ~44 tablo, 5 şema). Kullanıcı
> DB'leri (BYODB/Gateway hedefleri) kapsam dışı. Dal: `main` @ `be9a234`. Salt-okunur
> inceleme; kod değiştirilmedi. Kanıt formatı: `dosya:satır` ya da tablo/kolon adı.
> Gerçek bir Postgres'e bu makinede bağlanılamadı (psql yok, Docker çalışmıyor); şema
> kanıtı `dotnet ef dbcontext script` çıktısından, davranış kanıtı koddan. Canlı
> veriyle doğrulanmayan her şey "doğrulanmadı" diye işaretli.

## 0. Özet

| Alan | Puan /10 | Tek cümle |
|---|---|---|
| 1. Referans bütünlüğü | **5** | Çekirdek `public` grafı sağlam; modül şemaları (`desk/ground/namines_vault`) ve denetim tabloları FK'sız, proje silinince yetim + dış kaynak sızıntısı. |
| 2. Silme davranışı | **4** | Hesap silme yolu YOK; 9 `RESTRICT → AspNetUsers` eklenirse imkânsız olur; proje silme denetim izini sessizce yok ediyor. |
| 3. İndeksler | **6** | FK indeksleri büyük ölçüde tam; ama `CloudProjects.UserId` fiilen indekssiz, `GatewayAuditEntries` hiç indekssiz, `ShareToken` taranıyor. |
| 4. Kısıtlar ve tipler | **4** | 0 CHECK, enum/durum kolonları serbest `integer/text`, kullanıcı girdisi `text` sınırsız; zaman damgaları ise tutarlı `timestamptz`. |
| 5. Eşzamanlılık | **7** | Sayaçlar çoğunlukla atomik `ExecuteUpdate`; `xmin` yalnız `CloudProjects`'te; birkaç "kontrol et-sonra-yaz" boşluğu kaldı. |
| 6. Benzersizlik | **6** | Davet/handoff token hash'i, (OrgId,UserId), (ProjectId,IssueNumber), Stripe event Id doğru; e-posta, kişisel org, `ShareToken`, `KeyHash` eksik. |
| 7. Migration hijyeni | **7** | 37 migration gerçek PG'de uygulanıyor, model drift'i yok, veri migration'ları idempotent; Down testsiz, bir migration yerinde düzenlenmiş. |
| 8. DB düzeyi güvenlik | **7** | Bağlantı dizesi AES-GCM, davet/handoff token'ları hash'li; TOTP anahtarı + share token düz metin, RLS yok. |
| 9. Operasyonel | **4** | Çoğu büyüme tablosunda saklama politikası yok, yedek yalnız yerel ve test edilmemiş, havuz ayarı kodda uygulanmıyor. |
| **Genel** | **5.5** | Şema modeli düzgün ve CI ile korunuyor; zayıf yer "yaşam döngüsü": silme, saklama, yedek ve kısıt katmanı. |

İyi olanlar (gerçekten): model drift'i yok (`has-pending-model-changes` temiz); entegrasyon
testleri `MigrateAsync()` ile **gerçek Postgres**'e tüm migration zincirini uyguluyor
(`Namines.Tests/Integration/*`, CI'da yeşil); üretimde `MigrateOnStartup=false` +
`--migrate` ayrı adım + bekleyen migration varsa Production'da açılmama
(`Program.cs:634-690`, `docker-compose.prod.yml:99`); Stripe webhook idempotency'si
PK + tek transaction + olay-zamanı sıralaması örnek kalitede (`StripeWebhookController.cs:95-200`);
AI kota düşümü tek atomik `UPDATE ... WHERE yer_var` (`AiQuotaService.cs:460-466`);
yıldız/fork sayaçları SQL'de (`HubController.cs:575-630,706`); CR onay geçişi koşullu
`UPDATE` (`ChangeRequestController.cs:650`); `xmin` eşlemesi yalnız Npgsql'de
(`AuthDbContext.cs`); tüm zaman damgaları `timestamp with time zone`; para/kota alanları
`numeric(18,x)`; API anahtarı 256-bit rastgele + sabit-zamanlı karşılaştırma.

---

## 1. Referans bütünlüğü (FK'sız referans kolonlar)

Toplam 46 FK; aşağıdakiler FK OLMAYAN referans kolonlar (DDL: `model-schema.sql`).

| ID | Sev | Tablo.kolon | Senaryo | Çözüm |
|---|---|---|---|---|
| DB-01 | **HIGH** | `namines_vault.vault_backups.ProjectId/OrganizationId/CreatedByUserId`, `vault_restores.ProjectId/BackupId/PreRestoreBackupId`, `vault_schedules.ProjectId` | `HubController.DeleteRepo` (`:522-552`) yalnız `CrossDatabaseRelations` temizleyip `CloudProjects.Remove` yapıyor. Yedek satırları ve **şifreli blob'lar (StorageKey)** projeden sonra kalıyor: depolama sızıntısı, `vault_restores.BackupId` kırık referans. Zamanlayıcı silinmiş projeyi devre dışı bırakıyor (`VaultScheduleBackgroundService.cs:118-125`, güzel), ama yedekler kalıyor. | `ProjectId` için FK (`ON DELETE RESTRICT` + silme akışında önce `IVaultService.DeleteAsync` ile blob temizliği), `vault_restores.BackupId` → `vault_backups` FK. Modül izolasyonu gerekçesi için: FK'yı cross-schema koymak mümkün, izolasyon sorun değil (aynı DB). |
| DB-02 | **HIGH** | `ground.ground_databases.ProjectId/OrganizationId/CreatedByUserId` | Proje silinince sağlayıcıda (Neon vb.) açılmış gerçek DB **silinmeden kalır**, faturalanır; kayıt yetim. `DeleteRepo` Ground'a hiç dokunmuyor. | `ProjectId` FK `RESTRICT` + `DeleteRepo` içinde `GroundService.RequestDeleteAsync`; ya da `Status=PendingDelete` temizleyici. |
| DB-03 | MEDIUM | `desk.sql_query_history.ProjectId/UserId`, `desk.saved_queries.ProjectId/UserId`, `desk.desk_handoff_tokens.UserId` | Proje/kullanıcı silinince yetim satırlar; `sql_query_history.Sql` kullanıcı SQL'i (gömülü literal/PII içerebilir) sonsuza dek kalır. | `ProjectId`/`UserId` FK `CASCADE` (bunlar kullanıcıya ait özel veri, kalması hata). |
| DB-04 | MEDIUM | `GatewayAuditEntries.ProjectId/ApiKeyId/ActorUserId`, `SqlExecutionAudits.UserId/ProjectId` | FK yokluğu **bilinçli ve doğru** (denetim kaydı silinen varlıktan uzun yaşamalı); ama `GatewayAuditEntries.ProjectId` indeksiz (bkz. DB-12) ve retention yok (DB-40). | FK ekleme; bunun yerine `ProjectName`/`ActorEmail` anlık kopyası + saklama politikası. |
| DB-05 | LOW | `CloudProjects.ForkedFromProjectId` | Kaynak proje silinince fork'un işaretçisi boşa gidiyor; UI "forked from ?" gösterebilir. | Self-FK `ON DELETE SET NULL`. |
| DB-06 | LOW | `OrgAiUsages.OrganizationId`, `TeamInvites.CreatedByUserId/AcceptedByUserId`, `Feedbacks.UserId` | Org silinince `OrgAiUsages` kalır (küçük); davet oluşturan/kabul eden kullanıcı silinince referans boşta. | `OrgAiUsages.OrganizationId` FK CASCADE; davet kolonlarına FK SET NULL. |
| DB-07 | LOW | `CrossDatabaseRelations.Source/TargetTableId, ...ColumnId` | SchemaJson içindeki mantıksal kimlikler; şema JSON'u düzenlenince sessizce kırılır (DB ile kısıtlanamaz). | Uygulamada doğrulama (zaten `DeleteRepo` ilişkileri temizliyor); kabul. |

Doğru olanlar: `CloudProjects→Organizations`, `Branches/SchemaVersions/ChangeRequests→CloudProjects`
FK'ları tam; `flow.automation_rules→CloudProjects` **cross-schema FK mevcut ve cascade**
(`FlowModelConfiguration`), yani "modül izolasyonu FK'ya engel değil" kanıtlanmış —
`desk/ground/vault` için aynı tercih yapılmamış.

## 2. Silme davranışı

31 CASCADE, 13 RESTRICT, 2 SET NULL.

| ID | Sev | Bulgu | Kanıt | Çözüm |
|---|---|---|---|---|
| DB-08 | **HIGH** | **Hesap silme kod yolu yok** (GDPR/KVKK silme hakkı için). `UserManager.DeleteAsync`/`Users.Remove` ve ilgili endpoint hiçbir yerde yok (grep: tüm `Namines.API`, `Infrastructure`). Tek `HttpDelete` listesi: feedback, rule, branch-db, relation, key, repo, member, share, invite, backup. | `grep HttpDelete` | Hesap silme/anonimleştirme servisi yazılmalı (DB-09/10 ile birlikte tasarla). |
| DB-09 | **HIGH** | Hesap silme eklense bile **9 RESTRICT FK `AspNetUsers`'a** engel olur: `Organizations.CreatedByUserId`, `Branches.CreatedByUserId`, `ChangeRequests.CreatedByUserId`, `ChangeRequestApprovals.UserId`, `ChangeRequestAuditLogs.ActorUserId`, `GatewayApiKeys.CreatedByUserId`, `CrossDatabaseRelations.CreatedByUserId`, `RepoIssues.CreatedByUserId`, `RepoIssueComments.AuthorUserId`. Üstelik `CloudProjects.UserId` **CASCADE**: kullanıcı silinirse takım org'una ait projeler de (başka üyelerin işiyle) silinir — ama `Organizations.CreatedBy RESTRICT` bunu bloklar, yani sonuç "hiçbir şey olmaz veya hepsi gider" ikilisi. | `AuthDbContext.cs` (OnDelete'ler), DDL satır 339-673 | Karar tablosu: kişisel veri (profil, kota, MFA, yıldız) CASCADE; yazar/denetim FK'ları nullable + `SET NULL` + gösterim adı anlık kopyası ("silinmiş kullanıcı"); `CloudProjects.UserId` org projelerinde sahipliği devret (önce transfer, sonra sil). `ChangeRequestAuditLogs.ActorUserId` zaten nullable → `SET NULL` sıfır maliyetli. |
| DB-10 | **HIGH** | **Proje silme tüm denetim izini sessizce yok eder**: `ChangeRequests` (CASCADE) → `ChangeRequestAuditLogs` (CASCADE) + `ChangeRequestApprovals`; `SchemaVersions`, `Branches`, `GatewayApiKeys` de gider. Silme işleminin kendisi için denetim kaydı YOK. Onaylı bir migration'ın kim tarafından onaylandığı kanıtı proje silmeyle uçar. | `HubController.cs:540-551`; DDL `FK_ChangeRequestAuditLogs_ChangeRequests ... CASCADE` | Soft-delete (`DeletedAt`) + N gün sonra purge job'ı; ya da silmeden önce denetim kaydını arşiv tablosuna/log'a yaz. |
| DB-11 | MEDIUM | Proje silmede `ChangeRequests.Head/BaseVersionId → SchemaVersions` **RESTRICT** ile aynı ifadede `SchemaVersions` CASCADE ile siliniyor. Postgres'te RESTRICT ertelenemez; cascade sırası motor kararı olduğundan bu desen bazı sürüm/plan kombinasyonlarında "violates foreign key" verebilir. **Gerçek PG'ye karşı DeleteRepo testi yok** (`HubHardeningTests` SQLite: `:42`; SQLite FK davranışı farklı). | `AuthDbContext.cs` (ChangeRequest HeadVersion/BaseVersion Restrict); `HubHardeningTests.cs:42,327` | Doğrulanmadı. Postgres entegrasyon testi ekle: versiyonlu + CR'li proje sil. Güvenli çözüm: bu iki FK'yı `NO ACTION` (ifade sonunda kontrol) yap ya da silmeden önce CR'leri açıkça sil. |
| DB-12a | MEDIUM | `UsageEvents.UserId` CASCADE: kullanıcı silinince **faturalama olayları** da gider; Stripe tarafıyla mutabakat/vergi saklama süresi (çoğu yargıda 5-10 yıl) ihlal edilebilir. | DDL satır 352 | `UserId` nullable + `SET NULL` (anonimleştir) ya da ayrı arşiv. |
| DB-12b | LOW | `StripeWebhookEvents` hiçbir FK'ya bağlı değil (iyi): kullanıcı silinse de kalıyor; ama `StripeCustomerId` kişisel veri sayılabilir, saklama süresi tanımsız. | DDL satır 219 | 90-180 gün sonra purge. |

## 3. İndeksler

Postgres FK kolonunu otomatik indekslemez; EF "önek kuralı" ile bileşik indeks varsa tekil FK indeksi üretmez — **filtreli** bileşik indekste bu yanlış sonuç veriyor.

| ID | Sev | Bulgu | Kanıt | Çözüm |
|---|---|---|---|---|
| DB-12 | **HIGH** | `GatewayAuditEntries` tabloda **hiç indeks yok** (PK dışında). Sorgular: `Where ProjectId == x [&& CreatedAt aralığı] OrderByDescending(CreatedAt)` + analitik `GROUP BY date_trunc`. Bu, gateway'in en hızlı büyüyen tablosu (okumalar dahil: `GatewayController.cs:292,362`). Her denetim sayfası sekans tarama. | `GatewayAudit.cs:94-110`, `GatewayAnalytics.cs:63-72`; DDL: indeks listesinde yok | `CREATE INDEX ON "GatewayAuditEntries" ("ProjectId","CreatedAt" DESC)`; büyürse `BRIN("CreatedAt")`. |
| DB-13 | **HIGH** | `CloudProjects.UserId` **fiilen indekssiz**: tek aday `IX_CloudProjects_UserId_Slug` `WHERE "Slug" IS NOT NULL AND "Slug" <> ''` filtreli; planlayıcı `WHERE UserId = $1` için kullanamaz. "Projelerim", `CanView`/`GetRole`, kullanıcı silme cascade'i hep bunu kullanıyor. | `AuthDbContext.cs` (Hub bloğu); DDL: `IX_CloudProjects_UserId` yok | `HasIndex(p => p.UserId)` (filtresiz) ekle. |
| DB-14 | MEDIUM | `ShareToken` ile arama (`ShareController.cs:110,137`, anonim uç) indekssiz → her istek `CloudProjects` (SchemaJson'lı geniş satırlar) üzerinde tarama; ayrıca token düz metin (DB-34). | `ShareController.cs:110`; DDL | Unique indeks (`WHERE ShareToken IS NOT NULL`) + token'ı hash'le. |
| DB-15 | MEDIUM | `HubController.Explore` (anonim, `:107-190`): `Include(User)` + ILIKE `Contains` + `OrderBy(StarCount, CreatedAt)` + `Take(200)` ardından uygulamada JSON parse + NSL doğrulama. Hiçbir ifade indeksi kullanamaz; her istek 200 tam şema JSON'u çekiyor. Tek ilgili indeks `IX_CloudProjects_Visibility` (düşük seçicilik). | `HubController.cs:107-190` | Kısmi indeks `("StarCount" DESC,"CreatedAt" DESC) WHERE "Visibility"=2`; `pg_trgm` GIN (`Name`,`Tags`); kalite kapısı sonucunu kolon olarak sakla (`ExploreEligible`), JSON'u listede seçme (projection). `IX_CloudProjects_Visibility` kaldırılabilir. |
| DB-16 | MEDIUM | `UsageEvents` admin sorguları (`Resource==AiCall && OccurredAt aralığı`, `AdminController.cs:578,1188`) için indeks yok; mevcut indeks `(UserId, BillingPeriod, Resource)` ile başlıyor. Kullanıcı başına fatura sorgusu (`UsageMeter.cs:60-75`) iyi indeksli. | DDL `IX_UsageEvents_...` | `("Resource","OccurredAt")` ya da aylık partition (bkz. DB-42). |
| DB-17 | LOW | `flow.automation_run_logs`: yalnız `RuleId` indeksli; son çalıştırma (`OrderByDescending(TriggeredAt)` kural bazında) ve temizlik (`WHERE TriggeredAt < cutoff`) sekans/sort. | `AutomationRunLogRetentionService.cs:107-136`, `AutomationController.cs:153,251` | `("RuleId","TriggeredAt" DESC)` ve `("TriggeredAt")`. |
| DB-18 | LOW | FK'sız ama sorgulanan referanslar indeksiz: `ground_databases.OrganizationId`, `vault_*.OrganizationId`, `desk_handoff_tokens.UserId` (süresi dolan token temizliği), `saved_queries.ProjectId`. | DDL | Büyüyünce ekle. |
| DB-19 | LOW | Fazlalık/zayıf: `IX_CloudProjects_Slug` (tüm sorgular `UserId`+Slug ile; DB-13 çözülünce gereksiz), `IX_CloudProjects_Visibility` (DB-15), `IX_SqlExecutionAudits_CreatedAt` tek başına (BRIN daha ucuz). | `AuthDbContext.cs` | Kaldır/BRIN. |
| DB-20 | LOW | `AspNetUsers.NormalizedEmail` indeksi (`EmailIndex`) benzersiz DEĞİL (bkz. DB-26); `StripeCustomerId` indeksi var (iyi, `:` yorumu); `GatewayApiKeys.Prefix` non-unique (kasıtlı, aday listesi). | DDL | — |

İyi: `RepoStars` PK `(ProjectId,UserId)` + `UserId` indeksi; `ChangeRequestApprovals (ChangeRequestId,UserId)` unique; `SchemaVersions (BranchId,Version)` unique + `(ProjectId,CreatedAt)`; `OrganizationMembers.UserId` indeksi; `SqlExecutionAudits (UserId,CreatedAt)`.

## 4. Kısıtlar ve tipler

| ID | Sev | Bulgu | Kanıt | Çözüm |
|---|---|---|---|---|
| DB-21 | MEDIUM | **0 CHECK.** Enum'lar `integer` (Status, Kind, RiskLevel, Role, Visibility, Cadence, Decision, Resource, GroundStatus…); aralık dışı değer (ör. `Role=99`) sessizce yazılabilir ve `OrgRole` switch'leri varsayılana düşebilir. Metin durumlar da serbest: `automation_run_logs.Status`, `TriggerType`, `ActionType`, `CloudProjects.DbType/Category/ConnectionDbType`, `ground_databases.Provider`, `vault_backups.Engine`. Kapsam kontrolü yalnız `HasMaxLength`'li 3 tabloda. | DDL; `Namines.Core/Models/Auth/*.cs` | Kritik olanlar için `HasCheckConstraint`: `OrganizationMembers.Role BETWEEN 0 AND 3`, `ChangeRequests.Status/RiskLevel`, `CloudProjects.Visibility`, `HourUtc BETWEEN 0 AND 23`, `DayOfWeek BETWEEN 0 AND 6`, `RetainCount>=1`, `StarCount>=0`, `Quantity>=0`, `RateLimitPerMinute>0`. Metin enum'lar için `HasConversion<string>` + CHECK ya da PG enum. |
| DB-22 | MEDIUM | Kullanıcı girdisi tutan kolonlar **sınırsız `text`**: `CloudProjects.Name/Description/Tags/Category`, `Feedbacks.Message` (uygulamada 4000 ile kırpılıyor, `FeedbackController`), `Feedbacks.Email`, `ChangeRequests.Title`, `RepoIssues.Body`, `RepoIssueComments.Body`, `SchemaJson` (3 yerde), `GatewayApiKeys.AllowedOrigins/AllowedIps`, `TeamInvites`. İstek gövdesi sınırı yalnız tek admin ucunda (`[RequestSizeLimit(16_384)]` `AdminController.cs:707`); genel Kestrel limiti dışında satır başına tavan yok → tek kullanıcı çok büyük `SchemaJson` yazabilir. | DDL; `grep MaxLength` | `HasMaxLength` (Name 200, Description 2000, Body 64k) + `SchemaJson` için CHECK `length("SchemaJson") <= 5_000_000` ve API'de açık limit. |
| DB-23 | LOW | JSON'lar `text` (`SchemaJson`, `NodePositionsJson`, `ImpactReportJson`, `ConditionsJson`, `ActionConfigJson`, `ProfileJson`, `AdvancedJson`, `MigrationResultJson`). **`text` kalması savunulabilir**: `Checksum` istek dizgesinden hesaplanıyor (`ChangeRequestController.cs:~160`), `jsonb` anahtar sırasını/boşluğu değiştirir → checksum doğrulaması bozulurdu. Ama sorgulanabilirlik yok (ör. "şu tabloyu içeren projeler" yapılamaz) ve geçersiz JSON DB'ye girebilir. | DDL | `CHECK (SchemaJson::jsonb IS NOT NULL)` (ya da `pg_input_is_valid`, PG16+) ucuz doğrulama; sorgu ihtiyacı doğarsa ayrı `jsonb` türev kolon. |
| DB-24 | LOW | **String PK = `Guid.NewGuid().ToString()` `text`** (36 bayt, rastgele). Üretim uygulamada (entity başlatıcıda), DB varsayılanı yok → SQL ile elle ekleme/migration'da `gen_random_uuid()::text` gerekiyor. Rastgele UUID'ler B-tree'yi parçalar; 16 bayt `uuid` yarı boyut. Mevcut ölçekte sorun değil, ama tüm FK/indeks kolonları bu 36-bayt metni taşıyor (`CloudProjects.Id` ~30 yerde). | DDL (`"Id" text NOT NULL`); `GatewayApiKey.cs:10` | Şimdilik kabul; `AspNetUsers.Id` Identity gereği `text`. Yeni tablolarda `uuid` + `DEFAULT gen_random_uuid()` (UUIDv7 PG18). |
| DB-24b | LOW | Nullable olması gerekenler/olmaması gerekenler: `Branches.ParentBranchId` ok; `TeamInvites.AcceptedByUserId` ok; `ChangeRequests.Title` nullable; `ChangeRequestAuditLogs.ActorUserId` nullable ama FK RESTRICT (tutarsız, DB-09); `SqlExecutionAudits.Error` vs `Success` tutarlılık kısıtı yok; `GatewayAuditEntries.TableName/RowKey` nullable (Rpc/Sql için meşru). `SchemaVersions.TableCount smallint` — 32767'yi aşan şema `(short)` cast taşar (uygulama tarafı doğrulanmadı). | DDL | `TableCount` için `int`. |

Zaman damgaları: DDL'de tamamı `timestamp with time zone`, entity'lerde `DateTime.UtcNow`, kodda `DateTime.Now` yalnız PDF raporlarında (`PdfReportGenerator.cs:86,196`, DB'ye gitmiyor) — **temiz**. `OrgAiUsages/GlobalAiUsages.Date` `timestamptz` ama "gün" anlamında: UTC gece yarısı normalize edildiği varsayılıyor (`AiQuotaService`), `date` tipi daha doğru (LOW). Parasal: `UsageEvents.Quantity numeric(18,4)`, `UserBillingSettings.MonthlyCapUsd numeric(18,2)` — doğru; token sayaçları `bigint`/`integer` (`UserAIQuotas.DailyLimit integer`: yeterli).

## 5. Eşzamanlılık

| ID | Sev | Bulgu | Kanıt | Çözüm |
|---|---|---|---|---|
| DB-25a | MEDIUM | `CloudProjects` dışında **concurrency token yok**. `xmin` yalnız `CloudProject`'te; `DbUpdateConcurrencyException` yakalanan yer yok (yalnız scaffold şablonunda). Yani token var ama çakışma işlenmiyor: `CloudProject` güncellemesinde 500 dönme riski; diğer satırlar (`Branches`, `GatewayTablePermissions`, `OrganizationMembers.Role`, `VaultSchedules`) son-yazan-kazanır. | `AuthDbContext.cs` (xmin bloğu); `grep DbUpdateConcurrencyException` | `SchemaController` PUT'unda 409 + istemciye "yenile"; `GatewayTablePermissions` ve `OrganizationMembers` için `xmin`. |
| DB-25b | LOW | Yıldız: `RepoStars` insert/delete ile `StarCount` düzeltmesi **ayrı ifadeler, transaction yok** (`HubController.cs:575-612`): ikisi arasında süreç ölürse sayaç ile satır sayısı ayrışır; self-heal yok. Fork: `ForkCount+1` fork ekledikten SONRA, aynı sorun (`:706`). | `HubController.cs` | Tek transaction ya da periyodik `StarCount = (SELECT count(*) ...)` yeniden hesap; ya da sayaç yerine sayım (cache). |
| DB-25c | LOW | `IssueNumber = MAX+1` + unique indeks + 5 deneme (`:838-880`): doğru ama yük altında retry fırtınası; kabul. Aynı desen `SchemaVersions.Version` (`ChangeRequestController.cs:140-175`, 4 deneme) ve `BranchController.cs:270-290` — **BranchController'da retry yok**: yarışta unique ihlali → 500. | `BranchController.cs:270-290` | Aynı retry döngüsünü uygula veya advisory lock (`pg_advisory_xact_lock(hashtext(branchId))`). |
| DB-25d | LOW | Global/Org AI havuz sayaçları bilerek best-effort (`AiQuotaService.cs:468-475` yorumu): kabul edilmiş, belgelenmiş taviz. İyi. | | — |

## 6. Benzersizlik

| ID | Sev | Bulgu | Kanıt | Çözüm |
|---|---|---|---|---|
| DB-26 | MEDIUM | **E-posta benzersizliği yalnız uygulamada**: `RequireUniqueEmail` ayarlanmamış (`Program.cs` Identity bloğu), `EmailIndex` unique değil; kayıt "FindByEmail sonra Create" (`AuthController.cs:87-105`). Yarışta iki aynı e-postalı kullanıcı oluşabilir; sonra `FindByEmailAsync` (`:171`) "birden fazla eleman" ile patlar → o hesaba giriş kilitlenir. `UserNameIndex` unique, ama UserName e-postadan türetiliyorsa kısmen korur (doğrulanmadı). | `Program.cs:165-185`, DDL `EmailIndex` | `RequireUniqueEmail=true` + unique `NormalizedEmail` indeksi (önce mevcut yinelenenleri tara). |
| DB-27 | MEDIUM | **Kişisel org tekilliği DB'de yok**: `GetOrCreatePersonalOrgAsync` "yarış-güvenli" diyor ama `catch (DbUpdateException)` tetiklenmez çünkü ihlal edilecek bir kısıt yok → eşzamanlı iki çağrı iki kişisel org üretir. | `OrgAccess.cs:98-135` | `UNIQUE ("CreatedByUserId") WHERE "IsPersonal"`. |
| DB-28 | LOW | `GatewayApiKeys.KeyHash` unique değil; doğrulama `Prefix` ile aday seçiyor (12 karakter, ~48 bit; çakışma düşük ihtimal ama indeks tek tek taranır). | `GatewayAccess.cs:70-80` | `UNIQUE (KeyHash)`; arama doğrudan hash ile (prefix gereksiz kalır, zamanlama riski de azalır). |
| DB-29 | LOW | `CloudProjects.ShareToken` benzersiz değil (DB-14). `Organizations.Name` tekil değil (kasıtlı olabilir). `ground_databases.ProviderProjectId` tekil değil: aynı sağlayıcı projesi iki kayda bağlanabilir. | DDL | `UNIQUE (Provider, ProviderProjectId) WHERE ProviderProjectId IS NOT NULL`. |

Doğru: `TeamInvites.TokenHash`, `desk_handoff_tokens.TokenHash` unique; `OrganizationMembers` bileşik PK; `RepoIssues (ProjectId,IssueNumber)`; `Branches` ad + "tek varsayılan" filtreli unique; `(UserId,Slug)` filtreli unique; `StripeWebhookEvents.Id` PK; `GlobalAiUsages.Date`, `OrgAiUsages (OrgId,Date)` unique; `GatewayTablePermissions (ProjectId,TableName)`.

## 7. Migration hijyeni

| ID | Sev | Bulgu | Kanıt | Çözüm |
|---|---|---|---|---|
| DB-30 | MEDIUM | **Uygulanmış migration yerinde düzenlenmiş**: `20261019120710_ModularMonolithSchemaIsolation` `1d8bd3c`'de `vault` → `namines_vault` olarak yeniden yazıldı. Yan etkiler: (a) Designer snapshot'ları (`…120710.Designer.cs:1326-1412`, `…080345.Designer.cs:1403-1489`) hâlâ `"vault"` diyor — yalnız `migrations remove`/yeniden iskele için yanıltıcı; asıl snapshot (`AuthDbContextModelSnapshot`) doğru ve drift yok. (b) Eski sürümü uygulamış DB'lerde tablolar `vault` şemasında kalıyor; bunu `20261024215203_MigrateLegacyVaultSchemaToNaminesVault` koşullu `ALTER TABLE ... SET SCHEMA` ile onarıyor (idempotent, iyi tasarlanmış; eski `vault` şemasını bilerek silmemesi de doğru). | `git log -- …120710*`, `…1024215203.cs:11-35` | Kural: uygulanmış migration'a dokunma; ihtiyaç → yeni migration. Designer'ları elle düzeltme riskli; uyumsuzluğu bu dosyada/CHECKLIST'te not et. Eski sürümü uygulamış bir DB'den yükseltme yolu **CI'da test edilmiyor** (doğrulanmadı). |
| DB-31 | LOW | Boş/yok `Down()`: `…1024215203` (kasıtlı boş). Diğerlerinin Down'u dolu ama **Down hiç test edilmiyor** (testlerde yalnız `MigrateAsync`). Geri alma planı pratikte "yedekten dön". | grep `Down` | Down'u "desteklenmiyor" diye belgele ya da bir Down testi ekle. |
| DB-32 | LOW | Ham SQL migration'ları: `…180755` (3 backfill), `…195831` (AI politika değer eşleme, kullanıcı tercihlerini koruyor), `…919221439` (5 SQL), `…1029153509` (slug yinelenen temizliği + unique). Hepsi idempotent/`NOT EXISTS` korumalı; `gen_random_uuid()` PG13+ çekirdek (hedef PG17, sorun yok). `…153509`'un slug yeniden adlandırması tek ifadede tüm `CloudProjects`'i güncelliyor (tablo kilidi, küçük tablo için kabul); üçüncü adım (`Slug || '-' || Id`) çakışma için son çare, ama gerçek veride 1 kez denenmedi (doğrulanmadı). | migration dosyaları | Büyük tabloda `CREATE INDEX CONCURRENTLY` için `suppressTransaction: true` kullan. |
| DB-33 | LOW | Kilit riski: tüm `CreateIndex`'ler transaction içinde, `CONCURRENTLY` değil. Şu an tablolar küçük (kabul); `UsageEvents`, `GatewayAuditEntries`, `SchemaVersions` büyüdükten sonra indeks eklemek yazmaları bloklar. DB-12'nin düzeltmesi **şimdi** yapılırsa ucuz. | migration'lar | Büyüme öncesi ekle. |

Not: 37 migration × Designer ~ 1.5 MB kod; `InitialPostgres` tek başına tüm Identity + çekirdeği kuruyor — temiz bir başlangıç.

## 8. DB düzeyi güvenlik

| ID | Sev | Bulgu | Kanıt | Çözüm |
|---|---|---|---|---|
| DB-34 | MEDIUM | **`ShareToken` düz metin ve bearer**: DB sızıntısı = tüm paylaşım linkleri. Aynı projede `TeamInvites.TokenHash`/`desk_handoff_tokens.TokenHash` hash'li — tutarsız. | `CloudProject.cs:96`, `ShareController.cs:110` | SHA-256 hash sakla, ham değer yalnız oluştururken dön (DB-14 ile birlikte). |
| DB-35 | MEDIUM | **TOTP gizli anahtarı ve kurtarma kodları** Identity varsayılanıyla `AspNetUserTokens`'ta düz metin; `ProtectPersonalData` etkin değil (grep boş). DB okuma yetkisi olan biri 2FA'yı çoğaltabilir. | `AuthController.cs:839-881`, `Program.cs` | `options.Stores.ProtectPersonalData=true` + `IPersonalDataProtector` (mevcut AES-GCM anahtarına bağla) ya da en azından kurtarma kodlarını hash'le. |
| DB-36 | LOW | `AesGcmConnectionSecretProtector`: AES-256-GCM, rastgele nonce, PBKDF2-100k, **sabit salt** (kasıtlı, belgeli). Eksik: anahtar versiyonlama/rotasyon (`v1` öneki var ama tek anahtar → rotasyon tüm `EncryptedConnectionString`'leri yeniden şifrelemeyi gerektirir). Kolon adı doğru (`Encrypted…`); `appsettings.json`'daki dev şifresi yalnız dev. | `AesGcmConnectionSecretProtector.cs` | `v2:<keyId>:…` + çoklu anahtar; rotasyon komutu. |
| DB-37 | LOW | API anahtarı: SHA-256 tuzsuz hash — 256-bit rastgele girdi için **yeterli** (parola gibi yavaş KDF gerekmez), hex saklama, sabit-zamanlı karşılaştırma (`GatewayAccess.cs:53-90`). Doğru. `LastUsedAt` her istekte yazılıyorsa sıcak satır güncellemesi (doğrulanmadı). | | — |
| DB-38 | MEDIUM | **Kiracı izolasyonu yalnız uygulama kodunda** (`OrgAccess.CanViewAsync/GetRoleAsync`); Postgres RLS yok, uygulama tek rolle (şema sahibi) bağlanıyor, `PUBLIC` şemalar ayrı rol/GRANT'larla ayrılmamış. Tek bir yetki-kontrolü hatası = çapraz kiracı sızıntısı (Ekim denetimi bunun gerçek örneklerini buldu: `04-EKIM-DENETIMI.md` #7, #8). | `OrgAccess.cs`; `docker-compose.prod.yml:82` | Kısa vade: uygulama için DDL yetkisiz ikinci rol (migration rolünden ayrı). Uzun vade: `CloudProjects`/`GatewayAuditEntries` gibi tablolarda RLS (`current_setting('app.user_id')`). |
| DB-39 | LOW | PII envanteri: `AspNetUsers` (Email, PhoneNumber, CompanyName, ProfileJson), `Feedbacks.Email/UserId`, `sql_query_history.Sql`, `SqlExecutionAudits.ScriptPreview/TargetHost`, `GatewayAuditEntries.RowKey`. Saklama süresi/silme akışı tanımsız (DB-08, DB-40). | DDL | Veri haritası + silme/anonimleştirme matrisi. |

## 9. Operasyonel

| ID | Sev | Bulgu | Kanıt | Çözüm |
|---|---|---|---|---|
| DB-40 | **HIGH** | **Saklama (retention) yalnız 2 tabloda**: `automation_run_logs` (`AutomationRunLogRetentionService`) ve `sql_query_history` (kullanıcı/proje başına N). **Sınırsız büyüyenler:** `GatewayAuditEntries` (okuma dahil), `UsageEvents`, `SqlExecutionAudits` (`ScriptPreview` metni), `ChangeRequestAuditLogs`, `StripeWebhookEvents`, `OrgAiUsages/GlobalAiUsages` (günde 1 satır, ihmal edilebilir), `desk_handoff_tokens` (süresi dolanlar silinmiyor), `vault_restores`, `Feedbacks`, `SchemaVersions` (her sürüm TAM `SchemaJson` kopyası). | grep `Retention`; DDL | Tablo başına politika: Gateway audit 90-180 gün, `desk_handoff_tokens` süresi dolduktan 1 gün sonra, `StripeWebhookEvents` 180 gün, `UsageEvents` fatura dönemi + 13 ay. Tek `DataRetentionService` (BackgroundService, `ExecuteDeleteAsync` ile toplu). `AutomationRunLogRetentionService.PurgeAsync` satırları `ToListAsync` + `RemoveRange` ile belleğe çekiyor → `ExecuteDeleteAsync`. |
| DB-41 | MEDIUM | `SchemaVersions.SchemaJson` ve `CloudProjects.SchemaJson` her kayıtta tam şema; sürüm başına yeni satır. Büyük şemalarda (yüzlerce tablo, MB'larca) hızlı şişme; Postgres TOAST+pglz sıkıştırır ama yine de. Delta veya N sürümden eskiyi arşivle politikası yok. | `ChangeRequestController.cs:157-165` | Eski sürümlerde (CR'e bağlı olmayan) sıkıştırma/arşiv; `lz4` TOAST (`ALTER COLUMN ... SET COMPRESSION lz4`, PG14+). |
| DB-42 | MEDIUM | **Bölümleme planı yok.** Şimdilik gerekmez; ama `GatewayAuditEntries`/`UsageEvents` için ilk adım aylık `RANGE (CreatedAt)` + `DROP PARTITION` ile saklama olacak — EF Core bunu yönetmez (ham SQL migration), PK `(Id, CreatedAt)` gerekir. Bugünden tasarım kararı ver: `Id` metin GUID, zaman içermiyor. | | Eşik belirle (örn. >50M satır) ve o zamana dek yalnız DB-12 + DB-40. |
| DB-43 | **HIGH** | **Yedek yalnız yerel ve doğrulanmamış**: `scripts/namines-backup` `pg_dump` + `.env` (şifreleme anahtarı, Stripe sırları dahil) tek `tar.gz`'ye koyuyor, `0600/0700` izinli (iyi), ama `/home/ubuntu/backups` **aynı sunucuda** — disk/sunucu kaybı = DB + yedek birlikte gider; **uzak (off-site) kopya yok**, şifreleme yok (arşiv içindeki düz `.env` ile birlikte), PITR/WAL arşivi yok (RPO = son çalıştırma), **geri yükleme testi otomatik değil** (`restore` komutu arşivi yalnız açıyor ve `psql` komutunu YAZDIRIYOR; `:120`). Retention 14 gün + son 7 korunur (iyi). Not: betik hâlâ "Supabase" diyor, prod compose ise kendi `namines-control-db` konteynerini kullanıyor (`docker-compose.prod.yml:30`) — hangisinin gerçek olduğu **doğrulanmadı**; `DB_HOST` compose'tan okunduğu için ikisinde de çalışır. | `scripts/namines-backup:5,40,135-170,199-201`; `docker-compose.prod.yml:30,82` | (1) off-site kopya (rclone/S3, istemci tarafı `age`/`gpg` şifreli), (2) DB dump'ı ve `.env`'yi ayrı arşivlere böl (anahtar + veri aynı dosyada olmasın), (3) haftalık otomatik "geçici Postgres'e restore + `SELECT count(*)`" doğrulaması, (4) WAL arşivi/pgBackRest ya da yönetimli PG. |
| DB-44 | MEDIUM | **Bağlantı havuzu ayarı kodda uygulanmıyor**: `docs/new-phase/19-ENV-VARS.md:32` `Database__MaxPoolSize=100` diyor ama kodda yok (grep); `Program.cs:151-165` yalnız `EnableRetryOnFailure(3, 5s)`. Npgsql varsayılanı Maximum Pool Size=100, Postgres `max_connections` varsayılanı 100 — **tek** API örneği + migration + arka plan servisleri (Vault zamanlayıcı, retention, SignalR) 100'ü aşarsa `too many clients`; 2. örnek mümkün olmaz. Havuz/zaman aşımı parametresi bağlantı dizesinde yok; `Command Timeout` varsayılan 30 sn. | `Program.cs:151-165`; `appsettings.json:10`; compose'ta `max_connections`/`shared_buffers` ayarı yok | `Maximum Pool Size=30;Minimum Pool Size=5;Timeout=15;Command Timeout=30;Connection Idle Lifetime=300` (veya PgBouncer transaction mode) ve compose'a `-c max_connections=200 -c shared_buffers=256MB` (konteyner limiti 1 GB: `docker-compose.prod.yml:47`). Uzun süren `Run Tests` gibi çağrılar DbContext'i tutuyorsa havuzu tüketir (doğrulanmadı). |
| DB-45 | LOW | `EnableRetryOnFailure` açık ve manuel transaction'lar `CreateExecutionStrategy` ile sarılmış (Stripe, iyi). Diğer manuel `BeginTransaction` kullanımlarında strateji yoksa runtime `InvalidOperationException` (doğrulanmadı — `grep BeginTransactionAsync` ile tara). | | — |
| DB-46 | LOW | `pg_stat_statements`, `log_min_duration_statement`, autovacuum ayarı, disk/şişkinlik (`pg_stat_user_tables.n_dead_tup`) izleme yok (repo içinde bulunamadı). | | Yavaş sorgu logu + `pg_stat_statements` etkinleştir. |

## 10. Önceliklendirilmiş düzeltme listesi (ilk 10)

| # | Öncelik | İş | İlgili | Efor |
|---|---|---|---|---|
| 1 | HIGH | Off-site + şifreli yedek, `.env` ayrı arşiv, otomatik restore doğrulaması | DB-43 | 1-2 gün |
| 2 | HIGH | Proje silme akışı: vault blob + ground sağlayıcı DB temizliği, denetim izi için soft-delete, `desk.*` CASCADE, vault/ground `ProjectId` FK | DB-01/02/03/10 | 2-3 gün |
| 3 | HIGH | İndeksler: `GatewayAuditEntries(ProjectId,CreatedAt)`, `CloudProjects(UserId)`, `ShareToken` unique | DB-12/13/14 | 2 saat (tek migration; tablolar küçükken şimdi yap) |
| 4 | HIGH | Hesap silme/anonimleştirme: `SET NULL` + nullable kullanıcı FK'ları, org projesi devri, `UsageEvents` anonimleştirme, endpoint + test | DB-08/09/12a | 3-4 gün |
| 5 | HIGH | Saklama servisi (Gateway audit, handoff token, Stripe events, UsageEvents, SqlExecutionAudits) + toplu `ExecuteDeleteAsync` | DB-40 | 1 gün |
| 6 | MEDIUM | Pool/`max_connections`/zaman aşımı ayarları + `pg_stat_statements` | DB-44/46 | 2-3 saat |
| 7 | MEDIUM | Benzersizlik: e-posta (`RequireUniqueEmail` + unique indeks), kişisel org filtreli unique, `KeyHash` unique | DB-26/27/28 | yarım gün (önce yinelenen taraması) |
| 8 | MEDIUM | Gerçek PG'ye karşı `DeleteRepo` (versiyon + CR'li) ve eski-`vault`-şemalı DB'den yükseltme testleri | DB-11/30 | yarım gün |
| 9 | MEDIUM | CHECK kısıtları (Role, Status, RiskLevel, Visibility, saat/gün aralıkları, `>=0` sayaçlar, `SchemaJson` uzunluk) + `HasMaxLength` | DB-21/22 | 1 gün |
| 10 | MEDIUM | TOTP/kurtarma kodu koruması (`ProtectPersonalData`), `ShareToken` hash, bağlantı-sırrı anahtar versiyonlama | DB-34/35/36 | 1-2 gün |

Sonraya bırakılabilir: `Explore` yeniden tasarımı (DB-15), `uuid` PK'lar (DB-24), bölümleme (DB-42), RLS (DB-38), `BranchController` sürüm-numarası retry'ı (DB-25c, 1 saatlik iş — kolay kazanç, 3. maddeyle birlikte yapılabilir).

## Doğrulanmayanlar (açık uçlar)

- Canlı Postgres'e bağlanılmadı; `DeleteRepo`'nun RESTRICT+CASCADE kombinasyonuyla gerçek PG'de başarılı olup olmadığı (DB-11) test edilmedi.
- Prod'daki gerçek DB barındırıcısı (Supabase mı, `namines-control-db` mi), `max_connections`, otomatik vacuum durumu, tablo boyutları.
- `UserName`'in e-postadan türetilip türetilmediği (DB-26'nın etkisini değiştirir).
- `LastUsedAt`'in her istekte yazılıp yazılmadığı (sıcak satır).
- `SchemaVersions.TableCount smallint` taşma davranışı.
- Manuel `BeginTransactionAsync` kullanımlarının tamamının `CreateExecutionStrategy` içinde olduğu.

---

## 11. Düzeltme durumu (2026-10-31, dal `fix/db-hardening`)

Üç Sonnet ajanı (dbschema, dbops, dbdelete); **12 yeni migration**. CI'da
gerçek PostgreSQL'e karşı `MigrationHygieneTests` geçti: boş DB'den en son
sürüme kadar migrate, migrate-down, tekrar up ve yetim kayıt içeren DB'den yükseltme.

| Bulgu | Durum | Nasıl |
|---|---|---|
| DB-01/02/03/05/06 | ✅ | Modül tablolarına FK. Yedek ve Ground kayıtları RESTRICT (silme akışı temizler), atıf sütunları SET NULL, geçmiş tabloları CASCADE. Yetim kayıtlar `migration_quarantine.orphaned_rows`'a taşındı |
| DB-04 | ✅ (bilinçli) | Audit tabloları FK'siz kalıyor, gereken index'ler eklendi |
| DB-07 | ➖ | SchemaJson içindeki mantıksal kimlik; DB ile kısıtlanamaz (uygulama doğrulaması mevcut) |
| DB-08 | ✅ | `DELETE /api/auth/me`, `AccountDeletionService`: yeniden kimlik doğrulama, sahiplik devri, Stripe iptali, anahtar iptali, anonimleştirme, PII'siz audit. Frontend "Hesabı sil" bölümü |
| DB-09 | ✅ | 9 RESTRICT FK → SET NULL (sütunlar nullable). Silinmiş kullanıcı "Deleted user" olarak gösteriliyor |
| DB-10 | ✅ | `ProjectDeletionService`: audit → Vault blob'ları → Ground sağlayıcı DB'si → proje. Hata olursa proje yerinde kalır (409/503). Audit kalıcı (`ChangeRequestAuditLogs` FK'siz + snapshot sütunları) |
| DB-11 | ✅ | `Head/BaseVersionId` RESTRICT → NO ACTION (ifade sonunda kontrol) |
| DB-12a/12b | ✅ | `UsageEvents.UserId` SET NULL; Stripe olayları için saklama süresi 180 gün |
| DB-12…19 | ✅ | Eksik index'ler. Büyük tablolarda `CREATE INDEX CONCURRENTLY`. Gereksiz index'ler kaldırıldı |
| DB-21/22 | ✅ | 13 enum sütunu ve aralıklar için CHECK; kullanıcı girdisi için uzunluk CHECK'leri (`NOT VALID` → temizse `VALIDATE`); SchemaJson ≤ 10M karakter. Uçlarda `RequestSizeLimit` (413) |
| DB-23 | ➖ | JSON `text` olarak kalıyor (savunulabilir); lz4 sıkıştırma eklendi (DB-41) |
| DB-24 | ✅ | 24 tabloda `DEFAULT gen_random_uuid()::text` |
| DB-25a/25b | ✅ | `DbUpdateConcurrencyException` → 409; yıldız işlemi tek transaction'da |
| DB-25c/25d | ➖ | Kabul edilmiş tavizler (belgeli) |
| DB-26/27/28/29 | ✅ | E-posta unique + `RequireUniqueEmail`; kişisel organizasyon filtreli unique; `KeyHash` unique; canlı Ground kaydı unique. Çift kayıt varsa migration açık hatayla durur, hesaplar birleştirilmez |
| DB-30/31 | ✅ | Migration hijyen testleri (up / down / up, yetimli yükseltme). Eski Designer dosyaları zararsız, olduğu gibi bırakıldı |
| DB-32/33 | ✅ | Ham SQL'ler okundu; büyük tablo index'leri CONCURRENTLY |
| DB-34 | 🟡 | `ShareTokenHash` + unique index; arama hash ile. Düz metin sütun, link yeniden gösterilebilsin diye geçici olarak duruyor (TODO). **`/api/share/sitemap.xml` ucunun bütün ham token'ları anonim olarak yayınladığı bulundu ve kaldırıldı** |
| DB-35 | ✅ | TOTP anahtarı ve kurtarma kodları AES-GCM ile şifreli (`EncryptedUserStore`, eski düz metin değerler geriye uyumlu) |
| DB-36 | ✅ | Çoklu anahtar + yeniden şifreleme (`--reencrypt-secrets`) |
| DB-37 | ✅ (zaten doğruydu) | — |
| DB-38 | 🟡 | `namines_migrator` / `namines_app` rolleri + ayrı migration bağlantısı (isteğe bağlı). Postgres RLS sonraki adım (modül raporu M6) |
| DB-39/40 | ✅ | `DataRetentionService` (günlük, toplu silme, yapılandırılabilir) |
| DB-41 | ✅ | SchemaJson için `SET COMPRESSION lz4` (PG14+) |
| DB-42 | ➖ | Bölümleme şimdilik gerekmiyor; saklama servisi büyümeyi sınırlıyor. Plan belgede |
| DB-43 | ✅ | Yedek age/gpg ile şifreli; `.env` ayrı arşivde; rclone ile sunucu dışına kopya; `namines-backup verify` geri yükleme testi. PITR önerisi `docs/deploy/URETIM-CALISTIRMA.md` §7'de |
| DB-44/45 | ✅ | Havuz, timeout ve keepalive varsayılanları kodda; manuel transaction'lar kontrol edildi |
| DB-46 | ✅ | `pg_stat_statements` + `log_min_duration_statement` |

**Bilinçli olarak açık bırakılanlar:**
- DB-34'teki düz metin share token sütunu;
- DB-38'deki Postgres RLS;
- DB-42'deki bölümleme.

Üçünün de gerekçesi yukarıda.

**Deploy öncesi yapılacaklar:**
1. Prod'da çift e-posta veya çift API anahtarı hash'i varsa migration durur. Önce kontrol: `SELECT "NormalizedEmail", count(*) FROM "AspNetUsers" GROUP BY 1 HAVING count(*) > 1;`
2. Yedek için `BACKUP_AGE_RECIPIENT` veya `BACKUP_GPG_PASSPHRASE_FILE` tanımlanmalı; tanımsızsa `namines-backup` artık çalışmaz.
3. Saklama servisi ilk gece eski kayıtları silmeye başlar; varsayılan süreler `.env.prod.example`'da.
