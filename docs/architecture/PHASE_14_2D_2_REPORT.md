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
| Independent audit | CONDITIONALLY ACCEPTED, bulgular düzeltildi, yeniden denetim BEKLİYOR | `PHASE_14_2D_2_INDEPENDENT_AUDIT.md`; kapanış §12 |

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

**Billing.** Kota rezervasyonu `AiQuotaService.TryReserveAsync` tek işlemde havuz (yalnızca Free), kullanıcı tavanı ve ekip havuzunu koşullu artırır; biri tutmazsa hiçbiri yazılmaz (karar önceliği havuz, kullanıcı, ekip). Önceki sürümde havuz ve ekip havuzu "oku, sonra yaz"dı: eşzamanlı isteklerde aşılabiliyordu. Gerçek PG testleri: 40 paralel rezervasyon tek kullanıcı tavanında tam 10 izin; 24 kullanıcı Free havuzda tam 10 izin; 12 üye ekip havuzunda tam 6 izin; reddedilen rezervasyon hiçbir sayaca dokunmaz; çift iade sıfırın altına inmez. `AIQuotaMiddleware` ana yolu da artık önce `TryReserveAsync` ile rezerve eder, hata ya da 2xx dışı yanıtta iade eder (denetim M-3 sonrası; §12). Bu sınır kapandı.

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
- **JWT/yenileme jetonu sürekliliği** canlı HTTP'de ayrıca sınanmadı (§5, Identity); parola özeti ve SecurityStamp'ın aynı kaldığı parmak izi ile gösterildi.
- **Denetim sonrası açık kalanlar (§12):** M-2 süreç-içi testi kilit olmadan da geçiyor (ayırt edici kanıt canlı HTTP ölçümü); m-7 geçmiş yeniden yazılmadı; m-8 kimlik varlıkları Core'da kaldı (borç `MODULE_OWNERSHIP.md`'de); Down sonra eski ikili veri kontrolü yapılmadı.
- Namines MCP araçları (`namines_*`) kullanılamadı; yerine gerçek PostgreSQL.
- Üretim veritabanında çalıştırılmadı, hiçbir şey push edilmedi. Geliştirme notu: önceki denetimden kalan API süreci (PID 38252) artık çalışmıyordu; derleme doğrudan ana kopyada yapıldı.

## 11. Test sonuçları

| Komut | Proje | Toplam | Geçen | Kalan | Atlanan | Süre | Ortam |
|---|---|---|---|---|---|---|---|
| `dotnet test -c Release --no-build` | `Namines.Tests` | 3347 | 3347 | 0 | 0 | 24 dk 05 sn | Windows, Docker PG17 |
| `dotnet test -c Release --no-build` | `Namines.Tests.RunTests` | 19 | 19 | 0 | 0 | 1 dk 09 sn | Aynı |
| `dotnet build ... --no-incremental` | çözüm | | | 0 hata | | | 0 uyarı |

Taban 3184 → 3331 (+147) → denetim düzeltmeleriyle 3347 (+16: 4 webhook, 2 migration, 1 denetim izi rolü, 4 kota ara katmanı, 2 kimlik sınırı, 3 yeni taranan derleme). Yeniden adlandırılan/kaldırılan 5 test vardı ve burada ilk yazılan "hiçbiri" cümlesi yanlıştı; liste §12 m-5'te. Derleme `--no-incremental`: 0 uyarı, 0 hata.

## 12. Bağımsız denetim bulgularının kapanışı

Denetim kararı CONDITIONALLY ACCEPTED (`PHASE_14_2D_2_INDEPENDENT_AUDIT.md`, `4d255de`). Aşağıdaki her madde aynı fazda düzeltildi; kanıt, çalıştırılan komut ya da testtir. Düzeltmeler dört commit'te: webhook/kota/denetim izi/Stripe kimlikleri (`a17c3bc`), Identity servisleri (`16a5241`), mimari taramalar (`181294d`), belgeler.

### M-1 Eşlemeyi kuran checkout asla "eski" sayılmaz
- Neden: `StripeWebhookService` sıra kontrolü, eşlemesi olmayan müşterinin daha yeni `subscription.updated` olayını da "uygulandı" sayıyordu; sonra gelen eski `checkout.session.completed` stale diye atılıyor, kullanıcı hiç bağlanmıyordu ve Stripe yeniden denemez.
- Düzeltme: sıra hesabı yalnızca gerçekten UYGULANMIŞ durum olaylarına bakar. Eşlemesi olmayan müşterinin güncelleme/silme olayı yazacak satır olmadığı için etkisiz kalır ve sıra hesabına girmez; checkout eşlemeyi kuruyorsa (müşteri bağlı değil) her zaman uygulanır. Diğer olay türleri aynı tuzağa bakıldı: yalnızca üç durum olayı (`checkout.session.completed`, `customer.subscription.updated`, `customer.subscription.deleted`) sıra hesabına girer, diğer türler durum yazmaz.
- Test (gerçek PG): `A_checkout_that_creates_the_mapping_is_never_dropped_as_stale_even_after_a_newer_update`; karşıt: `A_checkout_older_than_an_applied_state_event_of_an_already_mapped_customer_is_stale` (bağlı müşteride eski checkout hâlâ stale).
- Canlı HTTP (denetçinin `wh.js` betiği, imzalı olaylarla): `4d255de` derlemesi `checkout completed @+40 (older) for wu4 ... {"stale":true} mapped=0 type=0`; düzeltilmiş derleme `mapped=1 type=2`.

### M-2 Aynı müşterinin farklı olayları seri işlenir
- Neden: iki ayrı olay (updated@t, deleted@t+1) paralel gelince her biri diğerini görmeden "en yeniyim" diyordu; sonucu commit sırası belirliyordu.
- Düzeltme: işlem içinde `pg_advisory_xact_lock(hashtextextended('stripe-customer:' + müşteri, 0))`; sıra kontrolü KİLİT ALTINDA ve Stripe'ın olayı üretme zamanına (`Event.Created`) göre yapılır. Kilit işlem sonunda düşer; olay kimliği birincil anahtarı aynı işlemde kalır.
- Canlı HTTP, 25 tur `updated(active)@t` paralel `deleted@t+1`: `4d255de` derlemesinde `final != canceled` = **8**, düzeltilmişte **0**. Bu, ayırt edici kanıttır.
- Test (gerçek PG): `Different_events_of_one_customer_arriving_in_parallel_always_end_at_the_newest_state` (30 tur, `FOR UPDATE` tutucusuyla commit sırası zorlanıyor). **Dürüst sınır:** bu süreç-içi test kilit devre dışı bırakıldığında da geçiyor, yani `4d255de`'de düşmüyor; yarışı süreç-içinde güvenilir üretemedim ve nedenini çözemedim. Ayırt edici kanıt yukarıdaki canlı HTTP ölçümüdür; test regresyon bekçisi olarak duruyor, yarışa karşı tek başına koruma sayılmamalı.

### M-3 AI kotası: önce atomik rezervasyon
- Neden: `AIQuotaMiddleware` salt-okunur `CheckAsync` + yanıttan sonra `ConsumeAsync`; paralel istekler kullanıcı tavanını, havuzu ve ekip havuzunu aşıyordu.
- Düzeltme: ana yol `TryReserveAsync` ile isteği ÖNCE rezerve eder (tek koşullu `ExecuteUpdate`, tek işlem); hata ya da 2xx dışı yanıtta `RefundAsync` ile iade eder. `/api/schema/generate` kendi rezervasyonunu yapan uçtur (`HasReservedInScope`) ve eski yoldan geçer.
- Test (gerçek PG, gerçek ara katman, 80 ms'lik sahte sağlayıcı): `Phase14_2D2QuotaMiddlewareTests` 4 test: kullanıcı tavanı (12 paralel, 3 geçer), serbest havuz (24 kullanıcı, 10 geçer), ekip havuzu (8 üye x 16 istek, tam `limit/6000` geçer), başarısız yanıt iadesi. Eski ara katmanda 4 testten 3'ü düşüyor, yenide hepsi geçiyor.

### M-4 Kimlik yönetimi Identity modülünde
- `AuthController` 41, `AdminController` 10 `UserManager` çağrısı taşıyordu. Şimdi `IIdentityAccounts` (kayıt, parola/kilit/MFA kapıları, hesap silme yeniden doğrulaması, profil, oturum iptali, MFA kurulum/aç/kapat, OAuth kullanıcı çözümü) ve `IAdminUserService` (yönetici yetkisi, hedef kullanıcı okuma) Identity modülünde; denetleyiciler yalnızca sonucu HTTP'ye eşler. `PlatformAdminAuthority` Identity'ye taşındı ve aynı kuralı paylaşır.
- Davranış, rota, durum kodu ve ileti değişmedi: yetki matrisi **829 karşılaştırma, 0 fark** (hem tabana hem bir önceki yeni çalıştırmaya karşı); `smoke-d1` 95/95, `smoke-d2` 44/44, yanıt şekilleri önceki çalıştırmayla `cmp` ile birebir aynı.
- **GET üzerinde `IsDev=true` yazımı (karar):** kaldırıldı. Yönetici yetkisi zaten yapılandırılmış `Dev:Email`'den (ya da `IsDev`, `Admin` rolünden) gelir; bayrağı yazmak yetki için gerekli değildi, bir okuma isteği kalıcı yetki durumunu değiştiriyor ve hatayı yutuyordu. Meşru davranış korunuyor: yapılandırılmış geliştirici hesabı yönetici sayılır ve parola girişinde (`PasswordSignInAsync`) `IsDev`, `EmailConfirmed`, `Corporate` ile yükseltilir (eskiden de oradaydı). Test: `Admin_resolution_is_read_only_for_the_configured_developer_account`.
- Mimari test: `No_controller_takes_UserManager_SignInManager_or_RoleManager` (kurucu, alan, özellik, eylem parametresi; 20'den fazla denetleyici taranır).

### m-1 Denetim izi değiştirilemezliği
- `namines_app` `SET LOCAL namines.audit_retention='on'; DELETE` ile tetikleyiciyi aşabiliyordu. Tetikleyici DELETE'e artık yalnızca ayar açık VE `current_user` tablo sahibiyse izin verir; temizlik tek yoldan, `governance.purge_change_request_audit(cutoff, batch)` SECURITY DEFINER işlevinden geçer (`search_path` sabit, PUBLIC'ten EXECUTE geri alındı, yalnızca `namines_app`'e verildi, 90 günden yeni satırı silmez: 22023). `DataRetentionService` ayarı en az 90 güne çeker.
- Test: `A_hijacked_application_role_cannot_delete_audit_rows_even_with_the_session_setting` `namines_app` rolüyle: ayar+DELETE `42501`, `DISABLE TRIGGER` reddedilir, işlev yeni satırı reddeder, eski satırı siler, ayar işlem sonunda kapanır.

### m-2 Taramalar Billing, Governance, Identity'yi kapsıyor
- Tablo sahipliği ve SQL metni taramasının sahip listesi ve taranan derlemeleri 4 modülden 7'ye çıktı (Identity'nin `public` tabloları dahil); Domain katmanı testi artık EF Core/Npgsql ÖZNİTELİKLERİNİ de tarar (öznitelik tür başvurusu olarak görünmüyordu).
- Üç kaçan mutasyon artık düşüyor (geçici değişiklik, derleme, test, geri alma): Governance SQL'inde `billing.customers` -> `Sql_text_never_names_another_modules_table(Namines.Governance)`; Billing SQL'inde `public."AspNetUsers"` -> `...(Namines.Billing)`; Billing domain sınıfında `[Microsoft.EntityFrameworkCore.Index("UserId")]` -> `Domain_namespaces_do_not_depend_on_persistence_or_ef` (ve model kayması nedeniyle `Every_context_has_no_pending_model_changes`). Değişiklikten önce üçü de geçiyordu.

### m-3 Stripe kimlikleri
- Karar: müşteri ve abonelik kimlikleri migration'da kırpılır; boş "yok" sayılır (NULL); kırpılmış değerler yinelenirse migration durur (müşteri ve abonelik için ayrı ileti). `billing.subscriptions.StripeSubscriptionId` filtreli (NULL hariç) benzersiz indeks aldı. Down kırpılmış değeri geri koyar (boş -> NULL, boşluklu kimlik kırpılmış döner; özgün boşluk bilgisi kaybolur, anlam kaybolmaz). Migration özetinde belgeli.
- Testler: `Padded_and_blank_stripe_ids_are_trimmed_into_the_new_tables_and_come_back_trimmed_on_down`, `The_same_stripe_subscription_on_two_users_stops_the_migration_and_changes_nothing`.

### m-4 `IMPLEMENTATION_STATUS.md`
- Bütün dosya son duruma göre yeniden yazıldı: başlık ve tarih, 14.2D-1 ACCEPTED, Aşama 5/6 tamamlandı, Aşama 8 açık, 14.2C satırındaki eski "controller'lar hâlâ AuthDbContext kullanıyor" cümlesi, §2 madde 3'teki zincir sahibi listesi (artık 11 bağlam), §3 denetim sonrası durum.

### m-5 Yeniden adlandırılan/kaldırılan testler (§11'deki önceki "hiçbiri" cümlesi yanlıştı)
Beş test adı değişti ya da kalktı; hepsi D-2'nin kendi sonucu (izin listesi boşaldı, sahip olmayan bağlam kalmadı):

| Eski | Durum | Yerine |
|---|---|---|
| `Controllers_do_not_use_any_DbContext_outside_the_explicit_D2_allowlist` | Kaldırıldı | `Controllers_do_not_use_any_DbContext` (izin listesi yok) ve `The_controller_allowlist_is_empty_after_14_2D_2` |
| `Allowlist_has_no_stale_entries` | Kaldırıldı | Liste yok; ayrı test gerekmiyor |
| `Non_owner_contexts_state_a_reason_and_have_no_migrations` | Kaldırıldı | Sahip olmayan bağlam kalmadı; `Every_DbContext_in_the_solution_has_a_declared_migration_status` ve zincir sırası testi |
| `AuthDbContext_has_no_dbset_or_model_entry_for_the_four_modules` | Değişti | `AuthDbContext_model_contains_only_identity_entity_types` |
| `All_Eleven_Modules_Register_DbContexts_And_Facades` | Yeniden adlandırıldı | `All_Eleven_Modules_Register_DbContexts_And_Services` (cephe kalmadı) |

IL taramasının `IDesignTimeDbContextFactory` muafiyeti daraltıldı: imza düzeyi tarama (alan, kurucu, metot imzası) artık hiçbir muafiyet taşımıyor; yalnızca IL düzeyinde, hizası kaymış baytları tesadüfen fabrikaya çözen tarayıcıya karşı fabrika türleri muaf. Fabrikalar kodda çağrılmaz (yalnızca `dotnet ef` bulur); muafiyet bu tek tür grubuyla sınırlı ve gerekçesi testin yorumunda yazılı.

### m-6 İmzalı ama ayrıştırılamayan gövde
- `ConstructEvent` `NullReferenceException`/JSON istisnası da 400'e çevrilir; ayrıştırma sonrası `Id` ya da `Data.Object` boşsa 400. Test: `A_signed_but_unparseable_body_is_a_400_not_a_500` (boş gövde, `{}`, `null`, `[]`, `data`'sız olay; hiçbir olay satırı yazılmaz).

### m-7 Geçmiş temizliği: yapılmadı, gerekçeli
`fea5610`, `b65fc95`, `bfa0893`, `e00b95f` API'yi, `c9fe0b8` ve `8bbac01` test projesini derlemiyor (ara adımlar; sonraki commit düzeltiyor). `00c287b..HEAD` aralığını (19 commit) yeniden yazmadım: yedek dal ve boş `git diff` kanıtıyla güvenli yapılabilir, ama diğer maddelerin önüne geçmemeliydi ve yeniden yazım denetim belgesinin atıf yaptığı hash'leri (`4d255de`, `1cc4ad8`) geçersiz kılar. Sonuç: bu aralıkta `git bisect` ara commit'lerde derleme hatası görebilir; son ağaç ve her düzeltme commit'i derleniyor ve test ediliyor. İstenirse ayrı bir iş olarak yapılabilir.

### m-8 Kimlik varlıklarının yeri: bilinçli borç
Taşınmadı: ~250 dosyada geçen ad alanı ve migration anlık görüntüleri tek PR'ı mekanik ve büyük yapar; neden ve somut sonraki adım `MODULE_OWNERSHIP.md` "Kimlik varlıklarının yeri" bölümünde.

### Ek kapılar
- `dotnet ef migrations has-pending-model-changes` 11 bağlamın hepsi için komut olarak yeniden çalıştırıldı (Billing, Governance, Studio, Hub, Gateway, Ops, Vault, Ground, Flow, Desk, Auth): hepsi "No changes have been made to the model since the last migration."
- Down sonra eski ikili ile veri kontrolü yapılmadı (ucuz değil: eski derleme, `dotnet ef` Down ve HTTP turu gerekir). Down'ın veriyi geri koyduğu migration testleri (`Padded_and_blank_...` dahil) yerine geçer; açık madde.
- Eski JWT canlı kontrolünü denetçi yaptı; yeniden yapılmadı.
- Frontend dosyası değişmedi; `tsc`/`eslint`/`vitest`/`next build` çalıştırılmadı.
