# Faz 14.2D-1 Raporu: Studio, Hub, Gateway, Ops gerçek modül

> Dal: `arch/phase-14-2`. Temel: `f5fcd96` (14.2C ACCEPTED). İlk teslim `a00861c`; bağımsız denetim `3da8e2e` CONDITIONALLY ACCEPTED, bulgular §11'de kapatıldı. Ortam: Windows 11, .NET 8 SDK, Docker,
> `postgres:17-alpine` (Testcontainers), `postgres:16` (`namines-verify`: duman, EXPLAIN, katalog).

## 1. Kabul tablosu

| Alan | Sonuç | Kanıt |
|---|---|---|
| Build | PASS | `dotnet build backend/Namines.sln -c Release --no-incremental`: 0 uyarı, 0 hata |
| Unit | PASS | Ops/Studio/Hub/Gateway Application testleri (başarı, hata, yetkisiz, geçersiz, yinelenen, eşzamanlılık, idempotency); sayılar §8 |
| Integration | PASS | Gerçek PostgreSQL: `MigrationHandoffTests`, `Phase14_2D1FlowTests`, `Phase14_2D1LostUpdateTests` (B-1 yarışı, m-3), 14.2B/14.2C migration ve akış testleri; §8, §11 |
| Architecture | PASS | `Phase14_2D1ModuleArchitectureTests` (37 test) ve `Phase14_2D1BoundaryScanTests` (14 test). İlk 8 mutasyon (§7) ve denetimden sonraki 4 mutasyon (§11, m-1) düşürüyor. Denetimin kaçırdığı 3 mutasyon artık yakalanıyor |
| Migration | PASS | Boş DB, 14.2C seviyesi tohumlu DB, gerçek `--migrate`, Down testli, kesilip devam, eşzamanlı orkestratör, hacim (§4, §6) |
| Data integrity | PASS | Satır sayısı taşımadan önce ve sonra aynı, migration içi doğrulama `RAISE`, şemalar arası FK yetim reddi, bileşik FK (§3, §4). İlk teslimde FAIL'di: B-1 (ayar kaydı eşzamanlı şemayı eziyordu), §11'de düzeltildi; denetimin probu 3 koşumda 0 kayıp |
| Security | PASS, açık madde §5 sonunda | §5. İlk teslimde M-1 nedeniyle FAIL'di: veri düzlemi kararları controller'daydı. §11'de tek modül kapısına taşındı |
| API compatibility | PASS | Denetimin 829 satırlık yetki ve şekil matrisi düzeltmelerden sonra tabana karşı 0 fark. Rota/DTO anlık görüntüsü `f5fcd96`'dan üretildi, test yeşil; HTTP duman 95/95 hem temiz hem tohumlu DB'de; şekil karşılaştırması 56 etiket 0 fark. Tek bilinçli ek: paylaşım yanıtında `closedOldestLinks` yalnız >0 iken (BL-7) |
| Documentation | PASS | Bu rapor, `IMPLEMENTATION_STATUS`, `MODULE_OWNERSHIP`, `DATABASE_NORMALIZATION`, `MIGRATION_PLAN`, `TARGET_ARCHITECTURE`, `docs/deploy/MIGRATION-VE-GERI-ALMA.md` §5.3 |
| Performans | PASS | §6 |
| Independent audit | 2. tur CONDITIONALLY ACCEPTED (`155a4b7`): 1. tur bulgularının hepsi kapandı; yeni R2-M1, R2-m1..m3 §12'de düzeltildi, yeniden denetim bekliyor | `PHASE_14_2D_1_INDEPENDENT_AUDIT.md` §8 |

## 2. Son durum

| Modül | Şema | Tablolar | Bağlam | Sıra | Kapı (controller'ın gördüğü) |
|---|---|---|---|---|---|
| Studio | `studio` | `projects`, `project_connections`, `project_shares`, `Branches`, `SchemaVersions`, `CrossDatabaseRelations` | `StudioDbContext` | 20 | `IProjectLifecycleService` (aktörlü yaşam döngüsü), `IStudioProjects` (yalnız okuma; yazma metotları modül içi akışlara), `IBranchService`, `IStudioBranches`, `ICrossDatabaseRelationService`, `IShareService`, `IProjectWorkspaceService` |
| Hub | `hub` | `repositories`, `project_tags`, `issue_labels`, `repo_issue_labels`, `RepoIssues`, `RepoIssueComments`, `RepoStars` | `HubDbContext` | 90 | `IHubRepositoryService`, `IHubIssueService`, `IHubApplicationService` |
| Gateway | `gateway` | `GatewayApiKeys`, `GatewayTablePermissions`, `GatewayAuditEntries`, `api_key_allowed_origins`, `api_key_allowed_ips`, `table_masked_columns` | `GatewayDbContext` | 100 | `IGatewayRequestGate` (veri düzlemi),  `IGatewayKeyService`, `IGatewayAuditLog`, `IGatewayMaintenance` |
| Ops | `ops` | `Feedbacks`, `SqlExecutionAudits`, `FeatureFlags`, `AiToolRoutes` | `OpsDbContext` | 110 | `IFeedbackService`, `ISqlExecutionAuditService`, `IFeatureFlagService`, `IAiToolRouter`, `IOpsAdminService` |

Her modülde `Domain`, `Application` (sözleşmeler, servisler, `I*Data`), `Infrastructure/Persistence` (bağlam, eşleme, migration), `DependencyInjection`.
Bağlamlar `internal`: başka proje derleyici düzeyinde göremez. `BuildingBlocks` yalnızca paylaşılanları tutar (`Contracts/StudioContracts.cs`, orkestratör, `ModuleOutcome`, `ITransactionParticipant`); eski cepheler (`GatewayModule` vb.) silindi.

**Hub ve Gateway Studio'ya yalnızca sözleşmeden gider.** Gateway sırrı `IProjectConnectionSecretProvider`'dan alır, koruyucuyu görmez (`Gateway_never_touches_the_secret_protector`). Ops başka tabloya dokunmaz.

**Tablo adları taşındı, yeniden adlandırılmadı** (`GatewayApiKeys`, hedef belgedeki `gateway_api_keys` değil). Ad değişikliği kod ve Down yüzeyini büyütür, faydası kozmetik: sonraya bırakıldı.

**Application → Infrastructure:** ilk sürümde servisler somut bağlamı alıyordu, mimari test yalnız public yüzeye bakıyordu; yani kural kâğıt üstündeydi. Her modüle `I*Data` iç arayüzü eklendi (DbSet'ler, `Database`, `ChangeTracker`, `Entry`, `SaveChangesAsync`). Servisler EF sorgusu yazar ama Infrastructure ad alanını görmez. Bu bir repository katmanı değil: EF'i soyutlamıyor, yalnızca bağımlılığın yönünü doğru tutuyor. Vault/Ground/Flow/Desk hâlâ somut bağlam kullanıyor (kapsam dışı).

## 3. Migration sahipliği ve FK kararları

**Beş zincir:** Auth (`public.__EFMigrationsHistory`), Studio, Hub, Gateway, Ops (`<şema>.__EFMigrationsHistory`). `--migrate` ve başlangıç kontrolü `ModuleMigrationOrchestrator`'dan geçer: danışma kilidi (`pg_try_advisory_lock` yoklamalı, `CREATE INDEX CONCURRENTLY` ile kilitlenmesin diye bloklayan kilit değil), önce Auth, sonra `Order`.

**Devir (handoff) neden boş `Up`:** Auth'un tablo düşürmesi veri kaybı olurdu. Auth tarafındaki `Phase14_2D1_HandoffOps`, `..HandoffStudioHub`, `..HandoffGateway` yalnızca anlık görüntüyü değiştirir (boş `Up`/`Down`, `Auth_handoff_migrations_have_no_operations`). Tablo hareketi modül zincirinde `ALTER TABLE SET SCHEMA`: FK ve indeksler tabloyla gider, satır kopyalanmaz. Eski migration'lar yerinde kaldı.

**Sahip olmayan bağlamlar:** Vault, Ground, Flow, Desk (DDL Auth zincirinde) ve Billing, Governance, Identity (D-2) açıkça `NonOwnerContext.Descriptor` ile gerekçeli işaretli, `ExcludeAllFromMigrations` ile. `has-pending-model-changes` 12 bağlamın 12'sinde "No changes". BL-C6 kapandı. Vault/Ground/Flow/Desk'in zincir sahipliği bu fazda taşınmadı: ana görev bu dört modül; kendi şemalarındalar, DDL sahipliğini taşımak faydasız risk olurdu.

**Şemalar arası FK kararı.** Veritabanında 33 şemalar arası FK var. Karar: **FK'ler kalır, EF modelinden çıkar.** Yetim koruması (anti-orphan) veritabanı düzeyinde sürer, bağlamlar birbirini görmez. Silme akışları sözleşmelerden geçer (`IProjectDeletion`, `IGatewayMaintenance.PurgeProjectAsync`, `IHubApplicationService.PurgeProjectAsync`), sonra FK son savunma.

| FK | Silme | Karar |
|---|---|---|
| `hub.repositories`, `hub.RepoIssues`, `hub.RepoStars`, `hub.issue_labels` → `studio.projects` | CASCADE | Kalır: proje yoksa Hub satırı anlamsız |
| `gateway.GatewayApiKeys`, `gateway.GatewayTablePermissions` → `studio.projects` | CASCADE | Kalır. `GatewayAuditEntries` FK'siz, projeden uzun yaşar (testli) |
| `gateway.GatewayApiKeys`, `hub.RepoIssues`, `hub.RepoIssueComments`, `studio.Branches`, `studio.SchemaVersions`, `studio.CrossDatabaseRelations`, `ops.Feedbacks` → `AspNetUsers` | SET NULL | Atıf kolonu. Hesap silmede `RevokeKeysByCreatorAsync` ve `AnonymizeByUserAsync` ayrıca açıkça temizler: model FK'yi bilmediği için davranış artık DB'ye örtük bırakılmıyor |
| `hub.RepoStars` → `AspNetUsers` | CASCADE | Kişisel veri |
| `studio.projects` → `AspNetUsers`, `Organizations` | CASCADE | D-2 Identity'de yeniden değerlendirilecek; hesap silme elle sıra izliyor |
| `public.ChangeRequests` → `studio.projects`, `studio.Branches` (CASCADE), `studio.SchemaVersions` (NO ACTION) | | D-2 Governance taşınınca karar |
| `desk.*`, `flow.automation_rules`, `namines_vault.*`, `ground.*` → `studio.projects`/`AspNetUsers` | Değişmedi | Vault/Ground backup ve veritabanı için RESTRICT: dış kaynak önce silinmeli (`Restrict_keeps_vault_and_ground_rows_...`) |

**Bileşik FK (BL-C1):** `repo_issue_labels`'a `ProjectId` eklendi, iki tarafta `UNIQUE (Id, ProjectId)`, FK'ler bileşik. Farklı projeden bağ artık veritabanında reddedilir. Mevcut veride farklı projeden bağ varsa migration `RAISE` ile durur, sessiz silmez; test `Migration_stops_when_a_cross_project_label_link_exists`.

**BL-8:** Vault/Ground `OrganizationId` kopyaları düştü, `Down` projeden doldurur (`Dropped_organization_copies_come_back_from_projects_on_rollback`). Eskiden okuyan kod yoktu. `MigrationCompatibilityTests.DeliberateExceptions`'a gerekçeyle eklendi.

## 4. Migration kanıtı

Gerçek binary, `namines-verify`:

```
Temiz DB:     --migrate rc=0 5,7 sn   Auth 56, Studio 1, Hub 1, Gateway 1, Ops 1
14.2C tohumlu: --migrate rc=0 1,8 sn  Auth 4 (devir ve BL-8), Studio 1, Hub 1, Gateway 1, Ops 1
```

**Katalog** (`d1_live`, tohumlu DB'den yükseltilmiş):

```
taşımadan önce  13 üst tablonun 13'ü public'te
sonra           gateway 3, hub 3, ops 4, studio 3; public 0
geçmiş          public 56, studio 1, hub 1, gateway 1, ops 1
FK sayısı       gateway 6, hub 12, ops 1, studio 13, public 25
convalidated=false FK/CHECK: 0 satır
```

Testler (gerçek PG): `Fresh_database_reaches_final_state_with_one_command`, `Every_mapped_column_of_every_context_exists_in_migrated_database` (12 bağlamın tüm kolonları), `Model_names_match_catalog_for_every_owning_context`, `Live_database_at_14_2C_moves_without_losing_rows_and_rolls_back` (satır sayısı, Down, tekrar Up), `Interrupted_run_resumes_from_the_failed_module`, `Concurrent_orchestrators_do_not_collide`, `App_role_can_use_module_schema_and_read_history_after_migration`.

Bilinen artık: Auth zincirindeki Vault/Ground/Flow/Desk'in bazı eski PK adları modelle farklı (14.2 öncesi). Davranışı etkilemiyor; ad testi bu yüzden sahip bağlamlarla sınırlı.

## 5. Akış denetimi ve güvenlik

**Bulunup düzeltilenler:**

| Bulgu | Etki | Düzeltme |
|---|---|---|
| 8 eşzamanlı commit 5 denemeyi tüketip hata döndürebiliyor | Kullanıcı rastgele 409/500 | Dal satırına `FOR NO KEY UPDATE`; numara seçimi sıralı. SQLite yolu için deneme kaldı |
| 6 eşzamanlı issue aynı şekilde "Could not allocate" | Kullanıcı rastgele 409 | Proje başına `pg_advisory_xact_lock` |
| Hesap silmede anahtar `CreatedByUserId` modelden çıkan FK'ye bağlıydı | Önceden iptal edilmiş anahtarlarda iz kalma riski (silme ardından DB temizlerdi, yine de sıra örtüktü) | Servis açıkça null yapıyor |
| Etiket bağı proje dışına çıkabiliyordu (BL-C1) | Tutarsız etiket | Bileşik FK |
| Anahtar sorgusu liste çarpımı (BL-C3) | Büyük listede satır patlaması | `AsSplitQuery` |
| Maske `PUT` yarışı (BL-C5) | Eşzamanlı istekte hata | Tekil çakışmada yeniden deneme, testte 6 istek tek satır |
| 50 aktif link tavanında sessiz kapanma (BL-7) | Sahip haberi olmadan eski link kapanıyordu | Yanıtta `closedOldestLinks` |

**Kontrol listesi (spesifikasyondaki):**

| Konu | Sonuç | Kanıt |
|---|---|---|
| IDOR, cross-tenant, yetkisiz proje | Geçti | Application katmanında kontrol (`AuthorizeBranchAsync`, `CanSeeAsync`, `CanEditAsync`, `RequireAdmin`); her serviste yetkisiz testi; duman başka kullanıcıyla 404/403 |
| Gateway yetkilendirme | Geçti (ilk teslimde yalnız anahtar yönetiminde doğruydu, M-1) | Anahtar yönetimi `GatewayKeyService`'te. Veri düzleminde anahtar, köken, IP, hız sınırı, tablo, expand, dışa aktarım, ham SQL ve maske kararı `IGatewayRequestGate`'te. `GatewayKeyRestrictions` ve hız sınırlayıcı internal; kapıyı atlayan uç mimari testte düşer |
| Sır, bağlantı dizesi | Geçti | Çözme yalnız `TenantProjectAuthorityService`, rotasyon aracı; IL taramalı mimari test; `Gateway_never_touches_the_secret_protector` |
| API anahtarı | Geçti | Yalnız SHA-256 özet, `FixedTimeEquals`; ham anahtar yalnız oluşturma yanıtında; DTO anlık görüntüsü `KeyHash` içermez |
| Paylaşım token kaba kuvvet | Geçti | `share-public`/`share-create` hız limitleri korunuyor, özetle arama |
| SSRF | Değişmedi | Host denetimi (`IConnectionHostInspector`) sözleşmeye taşındı, davranış aynı, duman `Security__AllowPrivateDbHosts` ile çalıştı |
| SQLi | Geçti | Dört modülün Application katmanında ham SQL 3 yerde, hepsi parametreli: `StudioBranchService` dal kilidi ve `HubIssueService` danışma kilidi (`ExecuteSqlInterpolatedAsync`), `GatewayAuditLog` analitiği (`SqlQuery`, kendi şeması, `date_trunc` için). İlk raporda 2 yazıyordu, üçüncüsü eksikti (m-6). `ExecuteSqlRaw`/`FromSqlRaw` 0. Migration'lardaki SQL sabit metin |
| Mass assignment | Geçti | `[FromBody]` ile entity bağlayan uç 0 (grep) |
| Hassas log | Geçti | Dört modülde sır/token/e-posta içeren log taraması: 2 satır, ikisi yalnız proje kimliği ve sayı |
| Yarış | Geçti | Yukarıdaki tablo; `Concurrent_*` 5 test gerçek PG |
| Önbellek anahtarı | Geçti | Dört modülde `IMemoryCache` yok; bayrak önbelleği küresel veri; hız limiti anahtara göre |
| Arka plan işçisi | Geçti | Saklama (`OpsRetentionService`, `PurgeAuditAsync`) kiracıdan bağımsız, süreye göre toplu siler; `Retention_runs_against_moved_tables` |
| Org sahipliği | Geçti | Yetki kararı `studio.projects.OrganizationId`'den; Vault/Ground kopyası kalktı, ikinci doğru kaynak yok |
| SQL denetim önizlemesi | Geçti (m-7 sonrası) | Anahtar kelimeden sonra tırnaklı değer, literal içindeki `password=`/`pwd=`/`token=` çiftleri, deyim başındaki `SET ad = değer` ve `Error` alanı maskeleniyor. İlk sürüm `dblink_connect('... password=...')` ile `SET app.key = '...'` değerlerini ve hata metnini açık bırakıyordu |

**Açık (spesifikasyon dışı, kayıt):** `FeatureFlagService` ve `AiRouteCatalog` deposunu `GetService` ile isteğe bağlı çözüyor, kayıt eksikse varsayılana düşüyor. Üretim DI'sinde kayıtlı (duman geçti) ama yanlış kurulum sessiz kalır; eski davranış, değiştirilmedi.

**`namines_*` MCP araçları (CLAUDE.md):** bu oturumda sunucu bağlanamadı (`CONNECT_TIMEOUT`). `namines_analyze_impact` ve `namines_prove_migration` çalıştırılamadı; yerine gerçek PG testleri ve üretim yolu kullanıldı.

## 6. Performans ve kilit

Hacim: 50 bin proje, 200 bin issue, 200,5 bin etiket bağı, 200 bin sürüm, 20 bin anahtar, 50 bin izin, 200 bin denetim, 100 bin SQL denetimi. EXPLAIN ANALYZE, taşınmış tablolarda, `ANALYZE` sonrası.

| Sorgu | Plan | Süre |
|---|---|---|
| Proje listesi (kullanıcı) | `IX_projects_UserId` | 1,36 ms |
| Explore popüler | `IX_repositories_Explore_Popular` | 1,95 ms |
| Explore `q` (10 bin public repo) | Popüler indeks + filtre + `IX_project_tags_Tag_ProjectId` | 19,4 ms |
| Explore etiket | Bitmap, `IX_project_tags_Tag_ProjectId` | 4,5 ms |
| Etiket filtresi nadir (sayfa) | Etiket bağı + `AK_RepoIssues_Id_ProjectId` | 5,2 ms |
| Etiket filtresi yaygın (sayım) | Paralel | 29,2 ms |
| Anahtar doğrulama (3 bölünmüş sorgu) | `IX_GatewayApiKeys_Prefix` | 0,08 + 0,11 + 0,10 ms |
| İzin + maske | `IX_GatewayTablePermissions_ProjectId_TableName` | 0,05 + 0,14 ms |
| Sürüm numarası | Index Only, `IX_SchemaVersions_BranchId_Version` | 0,06 ms |

**BL-7 kararı:** Explore `q` 10 bin public repoda 19 ms ve doğrusal büyüyor; `pg_trgm` eklenmedi. 100 bin public repoda ~200 ms beklenir (tahmin, ölçülmedi), o noktada yeniden bakılmalı.

**Kilit:** `--migrate` toplam 4,95 sn boşta, 4,96 sn üç okuyucu yükü altında (aynı hacim). Yükte 76 sorgu, en uzun 2.879 ms (`docker exec` gidiş dönüşü dahil), 3 sorgu hata (taşıma anında tablo şema değiştirirken: durdur-migrate-aç yapılınca oluşmaz). Zincir başına ayrı ölçüm yapılmadı; log zaman damgaları saniye çözünürlüğünde. Bileşik FK en pahalı adım olmalı ama bu varsayım, ayrı ölçülmedi.

## 7. Mimari test ve mutasyon

Geçici değişiklik, derleme, `Phase14_2D1ModuleArchitectureTests`, geri alma (`git checkout`, her biri çalıştı):

| Mutasyon | Düşen test |
|---|---|
| `HubController`'da özel `AuthDbContext` alanı | `Controllers_do_not_use_any_DbContext_outside_the_explicit_D2_allowlist` |
| Aynı yerde `RequestServices.GetService(typeof(AuthDbContext))` (yansıma) | Aynı, IL taraması |
| Hub → Gateway proje başvurusu | `Module_project_references_only_Core_and_BuildingBlocks` |
| `FeedbackService` gövdesinde `typeof(OpsDbContext)` | `Application_layer_has_no_dependency_on_infrastructure_or_a_concrete_context` |
| `AuthDbContext`'e `DbSet<GatewayApiKey>` | `AuthDbContext_has_no_dbset_or_model_entry_for_the_four_modules`, `Every_context_has_no_pending_model_changes` |
| Gateway'de `IConnectionSecretProtector` | `Gateway_never_touches_the_secret_protector`, `Secrets_are_decrypted_only_in_...` |
| Studio'da `UPDATE studio.projects` ham SQL (BL-5) | `Project_read_model_writes_have_a_single_door` |
| `OpsDbContext` modeline kolon, migration yok | `Every_context_has_no_pending_model_changes` |

İzin listesi (denetim M-2 sonrası, dokundukları veri test dosyasında satır satır): `AdminController`, `AuthController`, `ChangeRequestController`, `CoderAIController`, `DocumentationController`, `PolicyController`, `ProjectMemberController`, `QuotaController`, `SmartSeedController`, `StripeWebhookController`, `TeamController`, `UsageController` (hepsi D-2). `Allowlist_has_no_stale_entries` listenin yalnız küçülmesini zorlar: `GroundController` ve `VaultController` bu testle listeden düştü.

**Sınırlar:** BL-5 taraması metin tabanlı, dinamik kurulan SQL'i görmez. IL taraması `TypeLoadException` veren üyeyi atlar (test ortamındaki Docker.DotNet sürüm farkı), metot gövdesi yine taranır.

## 8. Test sonuçları

| Komut | Proje | Toplam | Geçen | Kalan | Atlanan | Inconclusive | Süre | Ortam |
|---|---|---|---|---|---|---|---|---|
| `dotnet test -c Release --no-build` | `Namines.Tests` | 3123 | 3123 | 0 | 0 | 0 | 17 dk 56 sn | Windows, Docker PG17 |
| `dotnet test -c Release --no-build` | `Namines.Tests.RunTests` | 19 | 19 | 0 | 0 | 0 | 1 dk 20 sn | Aynı |
| `npx tsc --noEmit` | `frontend` | | | 0 hata | | | | Kaynak `f5fcd96` ile aynı (diff boş) |
| `npx eslint .` | `frontend` | | | 0 hata, 188 uyarı | | | | Önceden var |
| `npx vitest run` | `frontend` | 280 (39 dosya) | 280 | 0 | 0 | 0 | | |
| `npx next build --webpack` | `frontend` | | | rc=0 | | | | |
| `npx vitest run`, `tsc` | `services/desk` | 121 (11 dosya) | 121 | 0 | 0 | 0 | | |
| `smoke-d1.js` | Gerçek API, temiz DB | 95 | 95 | 0 | | | | Baseline binary de 95/95 |
| `smoke-d1.js` | Gerçek API, 14.2C tohumlu DB | 95 | 95 | 0 | | | | |
| CLI | `namines` | `--help`, `validate`, `ddl`, `prisma`, `diff` | çalıştı | | | | | Duman |
| MCP stdio | `namines-mcp` | `initialize`, `tools/list` (13), `list_artifact_targets`, `generate_ddl`, `generate_prisma`, `hub_search_blueprints` | hata yok | | | | | Duman; API'ye giden araçlar (`NAMINES_API_TOKEN`) çalıştırılmadı |
| `dotnet ef migrations has-pending-model-changes` | 12 bağlam | 12 | 12 "No changes" | 0 | | | | |

Duman loglarında 2 EF hata satırı: eşzamanlı yıldız `23505`'i, servis yakalıyor, yanıt 200. Beklenen. Eşzamanlı commit: baseline'da 409 veren 5 istek şimdi `[200,200,200,200,200]`.

**Kaldırılan test:** `AutomationRuleContextTests`'teki SQLite cascade testi. FK modelden çıktığı için SQLite ona bakamazdı; gerçek PG karşılığı eklendi (`Deleting_project_cascades_to_flow_and_desk_rows_with_ddl_in_auth_chain`). Taban 2898 → 3123. Taban TRX ile ad karşılaştırması 11 ad eksiltiyor, tamamı açıklı: yukarıdaki test; `ModularMonolithArchitectureTests`'te davranışı `Phase14_2D1ModuleArchitectureTests`'e geçen 3 test (`StudioHubBaglamlari_MigrationUretmez_DdlTekZincirde` artık yanlış: modüllerin zinciri var; okuma modeli koruması `Project_read_model_writes_have_a_single_door` oldu; DbSet testi `AuthDbContext_has_no_dbset_...` ve `TasinanTablolar_kendi_modul_baglaminda_ve_semasinda` oldu); `Phase14_2CNormalizationArchitectureTests`'te tipi ya da dosyası taşınan 7 satır (yeni ad alanı ve dosya adıyla çalışıyor).

## 9. Backlog kapanışı

| ID | Durum |
|---|---|
| BL-5 | Kapandı: `Project_read_model_writes_have_a_single_door`, bağlam internal, mutasyonla sınandı |
| BL-6 | Kapandı: `RepoStars`, `RepoIssues`, `RepoIssueComments` `hub`'da |
| BL-7 | Kapandı: ölçüm §6, `closedOldestLinks`; frontend uyarısı denetim sonrası eklendi (m-4, §11) |
| BL-8 | Kapandı: kolonlar düştü |
| BL-9 | Yapılandırılabilir oldu: `Introspection:QueryTimeoutSeconds` (5-300, varsayılan 15). Soğuk Oracle hedefi gerçekten sınanmadı |
| BL-C1 | Kapandı: bileşik FK |
| BL-C3 | Kapandı: `AsSplitQuery` |
| BL-C4 | Kapandı: etiket deposu Hub'da, `GatewayDbContext` gerçekten kullanılıyor |
| BL-C5 | Kapandı |
| BL-C6 | Kapandı: 12 bağlam temiz, sahip olmayanlar açıkça işaretli |
| BL-C8 | Kapandı: `IMPLEMENTATION_STATUS` güncellendi |
| BL-C2, BL-C7 | Faz dışı (D-2 ve sonrası) |

## 10. Yapılmayanlar ve açık kalanlar

- Identity, Billing, Governance (D-2): tablolar `public`'te, `AuthDbContext` zincirinde. 12 controller izin listesinde. Bu controller'lar artık yalnız Identity (kullanıcı, oturum, MFA, `OrganizationMembers`, `TeamInvites`, kişisel org), Billing (`UserAIPolicies`, `UserAIQuotas`, `GlobalAiUsages`, `UsageEvents`, `UserBillingSettings`, `StripeWebhookEvents`, `PendingSubscriptionCancellations`) ve Governance (`ChangeRequests`, `ChangeRequestApprovals`, `ChangeRequestAuditLogs`) verisine dokunuyor. İlk rapordaki "hepsi D-2" ifadesi eksikti: `/auth/sync`, share/unshare, org taşıması ve otomatik onay Studio işiydi ve controller'daydı (M-2), §11'de taşındı. Sync sonrası otomasyon tetikleyicisi (Flow kuyruğu) `AuthController`'da kaldı: Flow'un işi, Studio'nun değil.
- Vault/Ground/Flow/Desk (m-9): varlıkları `AuthDbContext` modelinde ve migration zinciri Auth'ta, servisleri somut bağlam kullanıyor. **Sahip: Faz 14.2D-2.** D-2 kabulü "AuthDbContext yalnız Identity varlıklarını tutar" dediği için bu dört modülün DDL sahipliğini kendi zincirlerine taşımak ve Application katmanlarını `I*Data` deseniyle Infrastructure'dan ayırmak D-2'nin işidir. D-1'de kod değişikliği yok.
- `ChangeRequests` Studio tablolarına FK ile bağlı; D-2 kararı.
- Dağıtım durdur-migrate-aç gerektirir, uyumluluk görünümü yok. Kısa bakım penceresi gerekli.
- Üretim veritabanında çalıştırılmadı. Hiçbir şey push edilmedi.
- Zincir başına kilit süresi ayrı ölçülmedi (§6).
- Şekil karşılaştırması `f5fcd96` smoke çıktısına karşı etiketlerde yapıldı; gövdelerin tamamı değil. Rota ve DTO sözleşmesi anlık görüntüyle sınanıyor.
- Geliştirme notu: denetimden kalan bir Namines API süreci (`127.0.0.1:5299`) ana kopyada bin kilitlediği için iş ayrı worktree'de yapıldı; süreç sonlandırılmadı.

## 11. Bağımsız denetim düzeltmeleri (`3da8e2e` sonrası)

Taban `3da8e2e`. Her bulgu kök nedeninden düzeltildi; davranış değişikliği yalnız açıkça yazılanlar.

| ID | Kök neden | Düzeltme | Kanıt |
|---|---|---|---|
| B-1 | `HubRepositoryService.UpdateSettingsAsync` projeyi izlemesiz okuyup `SaveAsync`'e bayat `SchemaJson`, `NodePositionsJson`, `OrganizationId` ile ve sürüm jetonu olmadan yazıyordu | Hub artık Studio alanı yazmıyor: yalnız ad, motor ve zaman yazan `IStudioProjects.RenameAsync`. `SaveAsync` var olan projede `ExpectedRowVersion` olmadan yazmayı reddediyor (`ArgumentException`). Diğer çağıranlar tarandı: `/auth/sync` (okunan sürümü taşıyor), fork ve blueprint tohumu (yalnız yeni kimlik), `DevSandboxSeeder` (artık sürüm taşıyor) | `Repo_settings_save_does_not_overwrite_a_schema_saved_in_between`: ayar akışını Hub okumasında bekletip araya şema kaydı koyar. `a00861c` kodunda kaldı (`{"tables":[]}` yazıldı), şimdi geçiyor. Denetimin `probes.js lost` probu gerçek API'de 3 koşum: 52/50/54 çift-200, **0 kayıp**; kalan 8/10/6 istek sync'in 409'u (doğru çakışma, sessiz kayıp yok) |
| M-1 | Gateway veri düzlemi kararları `GatewayController`'da, anahtar kontrolleri üç kopya | `IGatewayRequestGate` (Gateway Application): anahtar doğrulama, köken, IP, hız sınırı, tablo, expand ve dışa aktarım izni, ham SQL hakkı (anahtar ve oturum yolu), oturum proje erişimi, izin listesi, maskeleme ve maske denetim kaydı. Controller istek bilgisini toplar, `GatewayDecision`'ı HTTP'ye çevirir. `GatewayKeyRestrictions` ve `GatewayRateLimiter.TryAcquireAsync` internal: API derlenirken bu kontrolleri kendisi kuramaz. Ölçüm `IGatewayMetrics` ile Infrastructure'a. Durum kodları, mesajlar ve kontrol sırası aynı | Mevcut controller ve uç testleri kapıya karşı: Gateway ve 14.2B akış filtresi 488/488. Denetimin matrisi (`matrix.js`, 212 rota, anon/yabancı/viewer/editor/owner) tabana (`mx-base.json`, `f5fcd96`) karşı **829 karşılaştırma, 0 fark**. `Every_gateway_data_plane_action_goes_through_the_gate` kapıyı atlayan ucu düşürüyor (mutasyonla) |
| M-2 | `/auth/sync`'in yetki, sürüm çakışması, kimlik üretimi ve işlemi ile share/unshare, tembel org taşıması ve otomatik onay izin listesindeki controller'lardaydı | `IProjectLifecycleService` (Studio Application), her çağrıda aktör: `SyncAsync`, `ShareToOrganizationAsync`, `UnshareAsync`, `ResolveOrganizationAsync`, `SetAutoApproveAsync`. Okuma modeli Hub'ın uyguladığı `IProjectReadModelWriter` ile aynı işlemde; slug üretimi Hub'a geçti. Rota ve DTO aynı, 409 gövdesi aynı. **Bilinçli tek sıkılaştırma:** projeyi org'a taşımak artık hedef org'da üyelik de istiyor (controller hep aktörün kendi aktif org'unu verdiği için bugünkü akış etkilenmiyor) | Studio testleri: yetkisiz (Viewer/Editor/Outsider), yabancı org, Owner-only unshare, sync (düzenleme, viewer atlama, yabancının yeni kimliği, bayat sürüm, toplu slug), otomatik onay, `RenameAsync`. `Controllers_do_not_write_studio_projects_directly`: hiçbir controller `IStudioProjects` yazma metodu çağırmıyor (ilk koşumda `ChangeRequestController`'ın otomatik onay yolunu yakaladı, o da taşındı) |
| m-1 | Mimari test SQL metnine ve tablo sahipliğine bakmıyordu | `Phase14_2D1BoundaryScanTests`: IL'deki tüm dize sabitleri (interpolasyon, ham dize, birleştirme dahil) başka modülün tablosunu nitelikli ya da tırnaklı tek başına adla içeremez; tablo listesi sahip modellerden. Sahip modülün tablosunu başka bağlam eşleyemez (`ExcludeFromMigrations` dahil) | Mutasyonlar, ayrı ayrı: Ops'ta `gateway."GatewayApiKeys"` ham SQL → `Sql_text_never_names_another_modules_table(Namines.Ops)` düştü; Hub'da `studio.project_connections` eşleyen dışlanmış varlık → `A_table_owned_by_a_module_is_mapped_by_no_other_context` düştü; API'de ham `NpgsqlCommand` ile `hub."RepoIssues"` → `(Namines.API)` düştü; Gateway `tables` ucunda kapı yerine boş anahtar → `Every_gateway_data_plane_action_goes_through_the_gate` düştü. Her biri 68/69, geri alınınca 69/69 |
| m-2 | `roles.sql` sahiplik devri listelerinde `ops` yok | İki listeye `ops`; listelerin aynı kalması gerektiği yorumla | `Superuser_ile_kurulup_sonradan_rollere_gecen_kurulumda_her_modul_semasi_migratora_gecer`: süper kullanıcıyla migrate, gerçek psql ile `roles.sql`, 9 şemada migrator dışı sahipli nesne 0, sonra Ops zinciri migrator ile Down ve Up. Eski `roles.sql` ile 6 nesne kaldı, test düştü |
| m-3 | İptal ve yeni anahtar ayrı commit | Tek işlem (çağıranın işlemine katılır) | `Download_key_rotation_keeps_the_old_key_when_the_new_one_cannot_be_written`: ekleme tetikleyiciyle düşürülür, eski anahtar etkin kalır ve doğrulanır. `a00861c` uygulamasıyla düştü (etkin anahtar 0) |
| m-4 | Frontend `closedOldestLinks`'i okumuyordu | `createShareLink` tipi alanı taşıyor, paylaşım uyarı bildirimi kapatılan sayıyı ve 50 tavanını söylüyor | `lib/shareLinkMessage.test.ts`; tsc 0 hata, eslint 0 hata (188 uyarı, önceden var), vitest 40 dosya 282/282, `next build --webpack` rc=0 |
| m-5 | `MoveToOrganizationAsync`, `SetAutoApproveAsync` çağrılmıyordu, taşımada hedef org sorulmuyordu | Silindi; yerine M-2'deki aktörlü ve hedef org üyelikli metotlar, testli | Yukarıdaki Studio testleri |
| m-6 | Rapor ham SQL envanteri eksikti | §5 SQLi satırı düzeltildi | Bu rapor |
| m-7 | Önizleme yalnız anahtar kelimeden hemen sonra tırnaklı değeri maskeliyordu; `Error` hiç | Literal içindeki sır çiftleri, deyim başındaki `SET` değeri ve `Error` aynı maskeden geçiyor; `UPDATE ... SET` ataması ayar sayılmıyor | `OpsApplicationTests`: 7 yeni önizleme vakası, hata alanı, null hata, `UPDATE` korunması |
| m-8 | Satır düzeyi GC tetikleyicisi, bağ başına iki deyim | `Hub_02_StatementLevelLabelGc` (Hub zinciri): geçiş tablosuyla deyim düzeyi, etiketler kimlik sırasıyla kilitlenip tek deyimde siliniyor. Down satır düzeyi sürümü aynı adla geri koyuyor | 200.500 bağlı hacim DB'si, işlem içinde: tüm bağlar 25,08 sn → 0,50 sn; bir projenin 50.500 bağı 16,35 sn → 0,07 sn; 50.000 issue'dan cascade 23,56 sn → 2,27 sn. Kalan etiket sayıları aynı. Eşzamanlı bağ yarışı testi (`Etiket_temizligi_eszamanli_baglanan_issue_i_silmiyor`) geçiyor |
| m-9 | Vault/Ground/Flow/Desk için faz sahibi yoktu | Sahip Faz 14.2D-2 olarak atandı (§10, `IMPLEMENTATION_STATUS`); D-1'de kod değişikliği yok | Bu rapor |

**Yeniden koşulanlar (düzeltmelerden sonra):**

| Komut | Sonuç |
|---|---|
| `dotnet build backend/Namines.sln -c Release --no-incremental` | 0 uyarı, 0 hata |
| `Namines.Tests` (tam) | 3166/3166, 0 atlanan, 0 kalan, 18 dk 40 sn (a00861c: 3123). Kaybolan ad 3: StudioApplicationTests.Moving_a_project_requires_member_management (3 durum), silinen ölü `MoveToOrganizationAsync` ile gitti; yerini aktörlü `Sharing_a_project_requires_member_management` (3 durum) ve 2 hedef-org testi aldı |
| `Namines.Tests.RunTests` | 19/19, 59 sn |
| `smoke-d1.js` temiz DB | 95/95; migrate: Auth 56, Studio 1, Hub 2, Gateway 1, Ops 1 |
| `smoke-d1.js` 14.2C tohumlu DB | 95/95; geçmiş public 56, studio 1, hub 2, gateway 1, ops 1; tetikleyici deyim düzeyi |
| Duman şekli | 56 etiket, 0 fark |
| `has-pending-model-changes` | 12 bağlamın 12'si "No changes" (Hub_02 boş model farkı, yalnız SQL) |
| Frontend, `services/desk` | yukarıda m-4; desk vitest 11 dosya 121/121, tsc rc=0 |

## 12. 2. tur denetim düzeltmeleri (`155a4b7` sonrası)

| ID | Kök neden | Düzeltme | Kanıt |
|---|---|---|---|
| R2-M1 | `roles.sql` yalnız tablo ve dizi devrediyordu; 14.2C'yi süper kullanıcı çalıştırınca `hub.issue_label_gc` onda kalıyor, `Hub_02`'nin `CREATE OR REPLACE FUNCTION`'ı migrator ile 42501 alıyordu, Gateway ve Ops hiç çalışmıyordu | `roles.sql` artık fonksiyon/tetikleyici fonksiyonu/prosedür/toplama, enum/domain/range/bileşik tip, görünüm, gerçekleştirilmiş görünüm, yabancı tablo ve `public` dışı şemaların kendisini devrediyor; liste `migration_quarantine`'i de içeriyor (yalnız sahiplik, uygulama yetkisi yok); eklenti nesneleri atlanıyor. Varsayılan yetkiler zaten migrator için tanımlı; fonksiyonlar için `PUBLIC EXECUTE` PostgreSQL varsayılanı, tetikleyici fonksiyonu tetikleyen rolden EXECUTE istemiyor. `Hub_02` önce sahipliği sınar, eksikse "Re-run deploy/sql/roles.sql" diyen 42501 ile durur. Runbook §5.3 ve betik başlığı güncellendi | Gerçek PG testi: 14.2C'ye süper kullanıcıyla migrate ve tohum, gerçek psql ile `roles.sql`, migrator ile tüm zincirler (bekleyen 0, tohum sağlam, tetikleyici deyim düzeyi), `namines_app` taşınan her tabloyu okuyor ve GC tetikleyicisinden geçerek yazıyor. `ca19825` `roles.sql`'i ile düştü (fonksiyon sahibi `postgres`). İkinci test: açık yeniden yaratılınca `Hub_02` rehber mesajla duruyor, hub geçmişi 1, `roles.sql` sonrası aynı komut tamamlıyor. Gerçek ikili: 14.2C dökümü süper kullanıcıyla geri yüklendi (fonksiyon sahibi `postgres`), yeni `roles.sql` sonrası `namines_migrator`, migrator ile `--migrate` rc=0 (geçmiş public 56, studio 1, hub 2, gateway 1, ops 1), `namines_app` ile duman 95/95. Aynı yol `ca19825` `roles.sql`'iyle: fonksiyon `postgres`'te kaldı, `--migrate` rehber mesajlı 42501 ile durdu (hub 1, gateway ve ops yok) |
| R2-m1 | Desk SQL konsolu ve yan defterleri Owner/bağlantı/konsol iznini controller'da yeniden kontrol ediyordu | `IGatewayRequestGate.AuthorizeDeskConsoleAsync`; `/query` oturum yolu ile tek özel kuralı paylaşıyor. Mesajlar, kodlar, sıra aynı | Controller artık rol sormuyor (`Gateway_controller_leaves_owner_checks_to_the_gate`; mutasyon: `DeskSql`'e geri eklenen `GetRoleAsync` bu testi düşürdü). Matris tabana karşı 829 karşılaştırmada 2 fark, ikisi de bilinçli R2-m3 değişikliği: `POST api/migration/generate` yabancı ve viewer için 500 → 503 |
| R2-m2 | Kapı taraması yalnız `GatewayController`'a bakıyordu | `IGatewayAccess` Gateway modülüne internal (`GatewayKeyRestrictions` ve `TryAcquireAsync` gibi). Gateway dışındaki her derlemede bu parçalara çağrı ya da yeniden public olmaları test hatası | Mutasyonlar: `FeedbackController`'da kapısız `IGatewayAccess.AuthenticateAsync` → derleme hatası CS0122; aynısı `IGatewayAccess` public yapılarak → `Only_the_gate_composes_gateway_access_checks` 10 derlemenin 10'unda düştü |
| R2-m3 | `MigrationController`'ın genel catch'i `AiNotConfiguredException`'ı mesajıyla 500'e çeviriyor, ara katmanın 503'ünün önüne geçiyordu | Üç uçta ve aynı desendeki `ReverseEngineerController`'da genel catch bu istisnayı dışlıyor; ara katman 503 `AI_NOT_CONFIGURED` veriyor, diğer hatalar eski yanıtında | `AiNotConfiguredMappingTests` uçları gerçek ara katmanın arkasında çalıştırıyor; `155a4b7` controller'larıyla 5 testin 4'ü düştü. Diğer AI çağrıları tarandı: `SchemaController` zaten 503, `DocumentationController` ve şema mock yolu bilinçli geri çekilme, Gateway NL bütçeyi iade edip sağlayıcı mesajsız 502 dönüyor (değiştirilmedi) |

**Yeniden koşulanlar:**

| Komut | Sonuç |
|---|---|
| `dotnet build backend/Namines.sln -c Release --no-incremental` | 0 uyarı, 0 hata, 31,7 sn |
| `Namines.Tests` (tam) | 3184/3184, 0 atlanan, 0 kalan, 22 dk 31 sn (frontend kontrolleriyle aynı anda koştu). `ca19825`: 3166; eklenenler `Phase14_2BMigrationTests` +2, `AiNotConfiguredMappingTests` +5, `Phase14_2D1BoundaryScanTests` +11; kaybolan ad yok |
| `Namines.Tests.RunTests` | 19/19, 1 dk |
| `smoke-d1.js` temiz DB / 14.2C tohumlu DB | 95/95 ve 95/95 (süper kullanıcı); rollere sonradan geçen yükseltmede `namines_app` ile 95/95. Duman şekli 56 etiket 0 fark |
| Frontend (`npm ci`, tsc, eslint, vitest, `next build --webpack`) | tsc 0 hata, eslint 0 hata (188 uyarı, önceden var), vitest 40 dosya 282/282, `next build` rc=0 |
| `services/desk` | vitest 11 dosya 121/121, tsc rc=0 |
| CLI `namines` | `--help`, `validate`, `ddl`, `prisma`, `diff` çalıştı (Release ikili) |
| MCP `namines-mcp` stdio | `initialize`, `tools/list` (13 araç), `namines_list_artifact_targets`, `namines_generate_ddl`, `namines_generate_prisma`, `namines_hub_search_blueprints`: `isError=false`. API'ye giden araçlar (`NAMINES_API_TOKEN`) çalıştırılmadı |
| `has-pending-model-changes` | 12 bağlamın 12'si "No changes" |
