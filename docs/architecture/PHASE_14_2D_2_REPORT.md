# Faz 14.2D-2 Raporu: Governance, Billing, Identity gerçek modül; AuthDbContext yalnızca Identity

> Dal: `arch/phase-14-2`. Temel: `00c287b` (14.2D-1 ACCEPTED). Ortam: Windows 11, .NET 8 SDK, Docker, `postgres:17-alpine` (Testcontainers), `postgres:16-alpine` (`namines-verify`: duman, EXPLAIN, katalog). Yalnızca çalıştırılanlar yazıldı; çalıştırılmayanlar §10'da.

## 1. Kabul tablosu

| Alan | Sonuç | Kanıt |
|---|---|---|
| Build | PASS | `dotnet build backend/Namines.sln -c Release --no-incremental`: 0 uyarı, 0 hata |
| Unit | PASS | `Namines.Tests` içinde yeni Application servisi testleri (başarı, hata, yetkisiz, geçersiz, yinelenen, eşzamanlılık, idempotency): `AiPolicyServiceTests` 5, `ChangeRequestServiceTests` 19, `BillingAccountServiceTests` 16, `BillingSubscriptionServiceTests` 10, `SubscriptionCancellationProcessorTests` 9, `StripeSubscriptionCancellerTests` 4, `UsageMeterServiceTests` 4, `AiQuotaServiceBehaviourTests` 7 |
| Integration | PASS | Gerçek PG: `Phase14_2D2BillingRealPgTests` 14, `Phase14_2D2GovernanceRealPgTests` 15, `Phase14_2D2BillingGovernanceMigrationTests` 4, `Phase14_2D2ModuleOwnershipTests` 14, `AccountDeletionDeskCascadeTests`, mevcut migration/akış testleri yeni düzene uyarlandı |
| Architecture | PASS | `Phase14_2D1ModuleArchitectureTests` (60 test, izin listesi BOŞ), 3 mutasyon yakalandı (§8) |
| Migration | PASS | Boş DB, D-1 seviyesinden dağınık veriyle yükseltme, tam Down + yeniden Up, 3 eşzamanlı `--migrate` (§4) |
| Data integrity | PASS | 16 parmak izi yükseltmede ve Down'da birebir aynı (§4) |
| Security | PASS, açık madde §10 | §6 |
| API compatibility | PASS | Yetki matrisi 829 karşılaştırma, 0 fark; yeni duman betiği 46 etiketin şekli tabanla 0 fark (§7) |
| Documentation | PASS | Bu rapor, `IMPLEMENTATION_STATUS`, `MODULE_OWNERSHIP`, `DATABASE_NORMALIZATION`, `MIGRATION_PLAN`, `docs/deploy/MIGRATION-VE-GERI-ALMA.md` §5.4 |
| Performans | PASS | §9 |
| Independent audit | BEKLİYOR | Yazılmadı; denetçinin işi |

## 2. Son durum

| Modül | Şema | Bağlam | Sıra | Kapı |
|---|---|---|---|---|
| Identity | `public` | `AuthDbContext` (internal, `Namines.Identity`) | 0 | `IIdentityDirectory`, `IOrganizationMembers`, `IAccountOrganizations`, `IIdentityMaintenance`, `IIdentityAdmin`, `ITeamService` |
| Studio | `studio` | `StudioDbContext` | 20 | değişmedi |
| Billing | `billing` | `BillingDbContext` | 30 | `IAiQuota`, `IUsageMeter`, `IBillingAccounts`, `IBillingMaintenance`, `ISubscriptionService`, `IStripeWebhookService` |
| Governance | `governance` | `GovernanceDbContext` | 40 | `IChangeRequestService`, `IAiPolicies`, `IGovernanceAudit`, `IGovernanceMaintenance`, `IChangeRequestStats`, `IGovernanceAdminQueries` |
| Hub / Gateway / Ops | değişmedi | | 90/100/110 | |
| Vault / Ground / Flow / Desk | kendi şemaları | artık zincir sahibi, internal, `I*Data` | 120/130/140/150 | |

`AuthDbContext` modelinde yalnızca ASP.NET Identity tabloları, `Organizations`, `OrganizationMembers`, `TeamInvites` var (`AuthDbContext_model_contains_only_identity_entity_types`); bağlam internal olduğu için başka hiçbir derleme ona başvuramaz (`Only_the_identity_assembly_references_AuthDbContext`). Controller izin listesi boş; 12 controller artık hiçbir DbContext kullanmıyor.

**Kararlar ve gerekçeleri**
- **Varlık sınıfları:** Billing ve Governance varlıkları kendi `Domain` ad alanlarına taşındı. Kimlik varlıkları (`ApplicationUser`, `Organization`...) ASP.NET Identity ile iç içe olduğu için `Namines.Core`'da kaldı; Identity için ayrı Domain katmanı yok (mimari test bunu bilerek hariç tutuyor).
- **`IsDev` Identity'de kaldı:** Stripe'ın yazdığı alan değil, yetki bayrağı (eski gerekçe geçerli). Plan = abonelik satırı (Billing) + `IsDev` (Identity, `IIdentityDirectory.GetUserFactsAsync`).
- **Plan kullanıcıya bağlı, org'a değil:** ekip planı kurucunun aboneliğinden okunuyor (eski davranış). Org'a ait abonelik tablosu açılmadı.
- **Webhook kullanıcı tipi:** `AspNetUsers.Type` güncellemesi Billing işlemine `ICrossModuleTransaction` ile katılır; yalnızca `Type` ve `ConcurrencyStamp` yazılır, SecurityStamp'a dokunulmaz.
- **`UserAIPolicies` ve test koşusu** normalize edilmedi (neden: `DATABASE_NORMALIZATION.md`).
- **Çapraz şema FK'ler (D-1 üslubu):** FK'ler DB'de kalır, EF modelinde yok. Yeni: `billing.*` → `AspNetUsers` CASCADE, `OrgAiUsages` → `Organizations` CASCADE, `governance.ChangeRequests` → `studio.projects/Branches/SchemaVersions` (değişmedi), `ChangeRequestAuditLogs`'ta proje/istek FK'si YOK (kanıt projeden uzun yaşar), kullanıcı FK'si SET NULL. Silme akışları sözleşmeyle açık: `IBillingMaintenance.ErasePersonalDataAsync`, `IGovernanceMaintenance.ErasePersonalDataAsync`, `IAccountOrganizations.DeleteUserAsync`, `DeleteProjectChangeRequestsAsync`; FK son savunma.
- **Hesap silme uçtan uca:** `AccountDeletionTests` (14) ve `Phase14_2BFlowTests` hesap silme testleri geçiyor; yetim satır yok. Desk sorgu geçmişi silinmesi yalnızca DB FK'sine dayanıyor (§10).

## 3. Migration sahipliği

Zincir sırası: Auth → Studio → Billing → Governance → Hub → Gateway → Ops → Vault → Ground → Flow → Desk (11 zincir, 11 `<şema>.__EFMigrationsHistory`). `Phase14_2D2_HandoffModules` Auth tarafında boş Up/Down; modeli yeni hâline getirir. Vault/Ground/Flow/Desk için tarihsel DDL Auth geçmişinde kalır (geçmiş yeniden yazılmaz); `*_01_AdoptTables` zincir sahipliğini devralır ve on birincil anahtar adını modele hizalar (`RENAME CONSTRAINT`, Down geri adlandırır). `has-pending-model-changes`: `AuthDbContext`, `BillingDbContext`, `GovernanceDbContext`, `VaultDbContext`, `GroundDbContext`, `FlowDbContext`, `DeskDbContext` için `dotnet ef` ile "No changes"; Studio/Hub/Gateway/Ops için `Every_context_has_no_pending_model_changes` ve `Owning_context_model_matches_its_migration_snapshot` (11 bağlam) testleri geçiyor (bu dördü için `dotnet ef` komutunu bu fazda ayrıca çalıştırmadım).

## 4. Migration kanıtı

Gerçek ikili, `namines-verify`:

```
Boş DB:        --migrate rc=0   ~6-29 sn   Auth 57, Studio 1, Billing 1, Governance 1, Hub 2, Gateway 1, Ops 1, Vault 1, Ground 1, Flow 1, Desk 1
D-1 seviyesi   --migrate rc=0   2,0 sn     (dağınık tohum: 9 yeni kullanıcı, org, üyelik, 4 davet, 4 değişiklik isteği, 4 oy, 4 denetim, kota, kullanım, webhook, outbox...)
3 eşzamanlı    --migrate         3 süreç rc=0, sonuç tek süreçle aynı (16 parmak izi)
```

16 parmak izi (`fp-before.sql`/`fp-after.sql`, her biri satır sayısı + alan düzeyinde `md5(string_agg(satır::text))`): kullanıcı kimlik alanları (parola özeti, SecurityStamp, ConcurrencyStamp, e-posta, tip, IsDev...), Stripe alanları (eski kolonlardan / yeni tablolardan), kullanıcı rol/talep/giriş/jeton, organizasyonlar, üyelikler, davetler, değişiklik istekleri, oylar, denetim, politika, kota, ayarlar, kullanım, global/org kullanımı, webhook olayları, outbox. **Yükseltme sonrası 16/16 aynı; tam Down (Governance, Billing) sonrası 16/16 eski hâle aynı; yeniden Up sonrası ilk yükseltmeyle aynı.** Katalog: beş Stripe kolonu `AspNetUsers`'ta yok, tablolar `billing`/`governance`'ta, `public.__EFMigrationsHistory` 57. Silinen kullanıcının `customers` satırı CASCADE ile gitti; denetim satırlarının `ActorUserId`'si FK ile NULL'landı ve tetikleyiciden geçti.

Migration içi `RAISE` doğrulamaları (Billing_01): önce yinelenen `StripeCustomerId` ön denetimi; doldurma sonrası müşteri ve abonelik sayı eşitliği; alan düzeyi `IS DISTINCT FROM` karşılaştırması; ancak sonra `DROP COLUMN`. Down aynı doğrulamayı tersine yapar. Gerçek PG negatif testleri (`Phase14_2D2BillingGovernanceMigrationTests`): yinelenen müşteri kimliği migration'ı net hatayla durdurur ve hiçbir şeyi değiştirmez, çözülünce yeniden çalışır; boş dizge müşteri kimliği "yok" sayılır (Down'da NULL döner: tek bilinçli dönüşüm). Kısmen başarısız yeniden çalıştırma ve eşzamanlı orkestratör: `MigrationHandoffTests` (`Interrupted_run_resumes...`, `Concurrent_orchestrators_do_not_collide`) geçiyor.

## 5. Akış denetimi

**Billing.** Kota rezervasyonu `AiQuotaService.TryReserveAsync` tek işlemde havuz (yalnızca Free), kullanıcı tavanı ve ekip havuzunu koşullu artırır; biri tutmazsa hiçbiri yazılmaz (karar önceliği havuz, kullanıcı, ekip). Önceki sürümde havuz ve ekip havuzu "oku, sonra yaz"dı: eşzamanlı isteklerde aşılabiliyordu. Gerçek PG testleri: 40 paralel rezervasyon tek kullanıcı tavanında tam 10 izin; 24 kullanıcı Free havuzda tam 10 izin; 12 üye ekip havuzunda tam 6 izin; reddedilen rezervasyon hiçbir sayaca dokunmaz; çift iade sıfırın altına inmez. **Bilinen sınır:** `AIQuotaMiddleware` yolu (`CheckAsync` + başarıdan sonra `ConsumeAsync`) rezervasyon değil; havuz sayacı orada gerçek tüketimi gösterir ve kullanıcı sayacı tavanda kırpılır, fakat bu yolda havuz tavanı sert değil (yerel yedeğe düşme kararı için okunuyor). `TryReserveAsync` kullanan yollar (şema üretimi, Gateway `/query/nl`, Flow) sert.

**Webhook.** İmza zorunlu (gizli anahtar yoksa 500, başlık yoksa/geçersizse 400; eksik başlıkta Stripe.net'in `NullReferenceException`'ı üretim hatası olarak bulundu ve servis artık önceden 400 veriyor). Olay kimliği birincil anahtar ve etkiyle AYNI işlemde; 12 paralel aynı teslimat tam bir etki üretir ve sahte Stripe geçidi bir kez çağrılır; etki patlarsa kayıt geri alınır ve yeniden deneme çalışır; sıra dışı eski olay `stale` kaydedilir, abonelik durumu değişmez. Yaşam döngüsü: `checkout.session.completed` (aktifleştir), `customer.subscription.updated` (past_due, plan değişimi, dönem sonunda iptal ayarı durumu bozmaz), `deleted` (ücretsize indir); `created` bilerek işlenmez (Stripe `incomplete` ile açar, etkinleştirmeyi `checkout.session.completed` yapar).

**BL kayıtları.** BL-2: 2B outbox migration'ının Down'ı açık satır varken `RAISE` ile durur. BL-3: `GetCancellationBacklogAsync` gerçek `total`/`stuck` verir (200 kayıtlık liste sınırından bağımsız). BL-4: denetim satırı değiştirilemez olduğu için tamamlanma `SubscriptionCancellationCompleted` olarak EKLENİR (outbox kimliğiyle eşleşir); işçi tamamlayınca da yazılır (`SubscriptionCancellationProcessorTests`, `Phase14_2BFlowTests`). BL-1: Stripe gerçek ağına gidilmedi; `resource_missing` eşlemesi Stripe.net `IHttpClient` saplamasıyla belgelenmiş 404 gövdesi üzerinden sabitlendi (`StripeSubscriptionCancellerTests`), diğer Stripe hataları `false` döner ve outbox yeniden dener. Gerçek test modu anahtarıyla çalıştırılmadı (§10).

**Governance.** Oylama tek işlemde ve istek satırı `SELECT ... FOR UPDATE` ile kilitli: iki eşzamanlı onaylayıcı (8 tur), eşik sonrası üçüncü onaylayıcı, onay/red yarışı (10 tur), aynı kullanıcının üç eşzamanlı oyu, yazarın kendi oyu — gerçek PG testleri geçiyor. Mutasyon: `FOR UPDATE` kaldırılınca üç test düştü (çift oy testi düşmedi; benzersiz indeks son savunma). Eski kod onay yarışından sonra çözülmüş isteği "Rejected" yapabiliyordu. Yetkisiz izleyici istek var/yok bilgisini almaz (404). **Denetim değiştirilemezliği:** `change_request_audit_guard` tetikleyicisi UPDATE/DELETE/TRUNCATE'i reddeder (SQLSTATE 42501). İstisnalar: yalnızca `ActorUserId` doluyken NULL'lamak (kullanıcı silme FK'si ve `ErasePersonalDataAsync`) ve `set_config('namines.audit_retention','on',true)` olan işlemde DELETE (yalnız `PurgeAuditAsync`, işlem yerel). Testler: her kolon için UPDATE, DELETE, TRUNCATE hem ham SQL hem EF ile reddedilir; saklama yalnızca süresi dolanı siler ve izin sonraki deyimlere sızmaz. Tablo sahibi tetikleyiciyi kapatabilir: bu veritabanı yöneticisine karşı değil, uygulama koduna ve yanlış yazılmış sorguya karşı koruma. Saklama süresi (`ChangeRequestAuditDays`) korunuyor.

**Identity.** Davet tek kullanımlık ve atomik (`AcceptedByUserId IS NULL` koşullu güncelleme). "Son sahip" kuralı organizasyon başına `pg_advisory_xact_lock` ile seri (iki eşzamanlı düşürme organizasyonu sahipsiz bırakamaz); üyelik değişikliği ve Gateway anahtar iptali tek işlemde. Oturumlar: yükseltmede kullanıcı satırına UPDATE yok (SecurityStamp, parola özeti aynı: parmak izi); `SecurityStampValidation` aynı sözleşmeyle çalışır (`SecurityStampValidationTests` 7). Not: JWT/yenileme jetonu ile "yükseltme öncesi jeton yükseltme sonrası çalışır" iddiası canlı HTTP'de ayrıca sınanmadı; dayanak SecurityStamp'ın değişmediği ve jeton imza anahtarına dokunulmadığıdır.

**Bilgi sızıntısı (denetim ek maddesi).** `MigrationController` (3 uç), `ReverseEngineerController`, `ScaffolderController` (2 uç) ve `SchemaController` revizyon yolu 500 yanıtında artık istisna metni taşımıyor (istisna loglanır); `AiNotConfiguredMappingTests`'te iki yeni test, gerçek ara katmanın arkasında gövdede gizli ayrıntının bulunmadığını doğrular. Kalan `ex.Message` kullanımları 400/409/422/429/503 doğrulama ve sağlayıcı hata yanıtları (bilinen istisna türleri); bunları taradım ama değiştirmedim.

## 6. Güvenlik kontrol listesi

| Konu | Sonuç |
|---|---|
| IDOR / yetkisiz | Application'da: `ChangeRequestService` (Viewer/yabancı 404), `ProjectMembershipService`, `TeamService`; yetki matrisi 829/0 fark |
| Çok kiracılı | Kota ve kullanım kullanıcı kimliğine bağlı; ekip havuzu organizasyon kimliği anahtarlı; denetim satırları proje kimliği anlık kopya |
| Sır / ödeme verisi | Kart verisi sunucuya gelmez; webhook gövdesi saklanmaz (yalnız olay kimliği/tür/zaman); Stripe gizli anahtarı yalnız yapılandırmadan |
| Webhook | İmza zorunlu, kimlik tekil, sıra dışı koruması, eşzamanlı yinelenen test edildi |
| SQLi | Billing ham SQL: iki `INSERT ... ON CONFLICT` (parametreli) ve outbox `FromSqlRaw` (sabit tablo adı, parametreli); Governance: kilit için `ExecuteSqlInterpolated`, `set_config` sabit. Dinamik SQL yok |
| Mass assignment | Controller'lar varlık bağlamıyor (kayıt tipleri) |
| Yarış | Kota, webhook, oylama, davet, son sahip: gerçek PG testleri |
| Önbellek anahtarı | Billing/Governance/Identity'de `IMemoryCache` yok |
| Arka plan işçisi | `SubscriptionCancellationWorker` kiracıdan bağımsız, açık satırları `FOR UPDATE SKIP LOCKED` ile alır |
| Hassas log | Webhook logları olay kimliği/tür/kullanıcı kimliği; e-posta/token yok |

## 7. API uyumu

- `smoke-d1.js` yeni ikili: boş DB 95/95, D-1 seviyesinden yükseltilmiş DB 95/95 (taban da 95/95).
- Yeni `smoke-d2.js` (kota, kullanım, politika, abonelik, webhook imza reddi, değişiklik isteği yaşam döngüsü, ekip/üyelik/son sahip, yönetici uçları): yeni ikili 44/44, taban (00c287b) 44/44; `shapecmp.js`: 46 etiket, 0 fark.
- Yetki matrisi (`aud/matrix.js`, 212 rota × anon/yabancı/viewer/editor/owner): 829 karşılaştırma, **0 fark** (taban 00c287b).
- Rota/DTO anlık görüntüsü testi geçiyor. Frontend dosyası değişmedi.

## 8. Mimari test ve mutasyon

Geçici değişiklik, derleme, `Phase14_2D1ModuleArchitectureTests`+`BoundaryScan`, geri alma:

| Mutasyon | Düşen test |
|---|---|
| `UsageController`'a `DbContext` alanı | `Controllers_do_not_use_any_DbContext` |
| `AuthDbContext`'e Desk varlığı DbSet'i | `AuthDbContext_model_contains_only_identity_entity_types`, `Every_context_has_no_pending_model_changes` |
| Billing Application'da `typeof(BillingDbContext)` | `Application_layer_has_no_dependency_on_infrastructure_or_a_concrete_context` |

`Only_the_identity_assembly_references_AuthDbContext` mutasyonla sınanamadı: bağlam internal, başka derleme derleyici düzeyinde başvuramıyor (derleme hatası), test yansıma/IL katmanında ek güvence. Not: IL taraması hizası kaymış baytları tesadüfen çözebiliyor; bu yüzden tasarım zamanı fabrikaları yok sayılıyor (D-1'den gelen sınırlama).

## 9. Performans (EXPLAIN ANALYZE, `ANALYZE` sonrası; 50 bin kullanıcı, 500 bin kullanım olayı, 200 bin webhook olayı, 300 bin denetim satırı)

| Sorgu | Plan | Süre |
|---|---|---|
| Üyelik rolü | `IX_OrganizationMembers_UserId` | 0,08 ms |
| Ekip havuzu organizasyonu | UserId indeksi + `PK_Organizations` | 0,18 ms |
| Atomik kota rezervasyonu (UPDATE) | `PK_UserAIQuotas` | 0,93 ms |
| Dönem özeti (kullanıcı) | `IX_UsageEvents_UserId_BillingPeriod_Resource` | 0,30 ms |
| Yönetici günlük çağrı (14 gün, 25 bin satır) | `IX_UsageEvents_Resource_OccurredAt` | 41 ms |
| Webhook müşteri araması | `IX_customers_StripeCustomerId` | 0,11 ms |
| En yeni olay (müşteri) | `IX_StripeWebhookEvents_StripeCustomerId_EventCreatedAt` index only | 0,10 ms |
| Plan çözümü | `PK_subscriptions` | 0,07 ms |
| Plana göre kullanıcılar (6 bin) | `IX_subscriptions_PlanCode` | 3,1 ms |
| Denetim: isteğe göre | `IX_ChangeRequestAuditLogs_ChangeRequestId_CreatedAt` | 0,27 ms |
| Denetim: projeye göre (50) | `IX_ChangeRequestAuditLogs_ProjectId_CreatedAt` | 0,11 ms |

`UsageEvents` bölümleme: ölçülmüş ihtiyaç yok, yapılmadı. Kilit süresi: boş DB dışında hacimli DB'de `--migrate` ölçülmedi (yalnız küçük tohumlu DB'de 2 sn); `SET SCHEMA` katalog işlemi, `AspNetUsers` üzerinde `DROP COLUMN` ve iki `UPDATE`'siz doldurma kısa ama **büyük kullanıcı tablosunda süre ölçülmedi**.

## 10. Yapılmayanlar ve açık kalanlar

- **Stripe gerçek ağı:** checkout/portal oturumu oluşturma, `StripeGateway` ve gerçek test modu iptali çalıştırılmadı (anahtar yok). Yalnızca imzalı sahte yük, sahte geçit ve `IHttpClient` saplaması.
- **Frontend:** hiçbir frontend dosyası değişmedi; `tsc`/`eslint`/`vitest`/`next build` bu fazda yeniden çalıştırılmadı.
- **Hacimli migration süresi ve kilit ölçümü** yok (§9).
- **Desk kişisel verisi** (sorgu geçmişi, kayıtlı sorgular, devir jetonları) hesap silmede yalnızca veritabanı FK'si (`CASCADE`) ile gidiyor; sözleşmeyle açık silme yok. Bu FK'ler düşerse silme sessizce eksik kalır. FK testle (`Deleting_a_user_removes_the_desk_sql_history_through_the_foreign_key`) korunuyor.
- **Tarihsel DDL:** Vault/Ground/Flow/Desk tablolarının `CREATE TABLE`'ı hâlâ Auth geçmişinde (geçmiş yeniden yazılmadı); zincir sahipliği taşındı.
- **Kimlik varlık sınıfları** `Namines.Core`'da (§2).
- **Middleware kota yolu** havuz tavanını sert uygulamıyor (§5).
- **JWT/yenileme jetonu sürekliliği** canlı HTTP'de ayrıca sınanmadı (§5, Identity); parola özeti ve SecurityStamp'ın aynı kaldığı parmak izi ile gösterildi.
- **`dotnet ef has-pending-model-changes`** Studio/Hub/Gateway/Ops için bu fazda yeniden komut olarak çalıştırılmadı (test karşılığı geçiyor).
- **Admin kontrolcüsünde** `UserManager` hâlâ yönetici doğrulaması ve kullanıcı güncellemesi için kullanılıyor (DbContext değil, Identity API'si); tam bir yönetici Application servisine taşınmadı.
- Namines MCP araçları (`namines_*`) kullanılamadı; yerine gerçek PostgreSQL.
- Üretim veritabanında çalıştırılmadı, hiçbir şey push edilmedi. Geliştirme notu: önceki denetimden kalan API süreci (PID 38252) artık çalışmıyordu; derleme doğrudan ana kopyada yapıldı.

## 11. Test sonuçları

| Komut | Proje | Toplam | Geçen | Kalan | Atlanan | Süre | Ortam |
|---|---|---|---|---|---|---|---|
| `dotnet test -c Release --no-build` | `Namines.Tests` | 3331 | 3331 | 0 | 0 | 25 dk 02 sn | Windows, Docker PG17 |
| `dotnet test -c Release --no-build` | `Namines.Tests.RunTests` | 19 | 19 | 0 | 0 | 1 dk 20 sn | Aynı |
| `dotnet build ... --no-incremental` | çözüm | | | 0 hata | | | 0 uyarı |

Taban 3184 → 3331 (+147). Yeniden adlandırılan/silinen test yok; D-1 ile kapsamı değişen testler (yetki yeni sözleşmelere uyarlanan hesap silme, denetim eklemeli izi, Stripe alanı tohumları) aynı ad ve niyetle duruyor.
