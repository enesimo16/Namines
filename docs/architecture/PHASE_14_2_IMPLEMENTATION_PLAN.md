# PHASE 14.2 — DATABASE NORMALIZATION (3NF) & MODULAR SCHEMA MIGRATION PLAN

> **Tarih:** 2026-11-03  
> **Durum:** PLANLAMA AŞAMASI (Hiçbir DDL / Migration çalıştırılmamıştır)  
> **Kapsam:** 11 Modüllü Hedef Mimariye Geçiş, 3NF Normalizasyonu, Şema İzolasyonu ve PostgreSQL Migration Stratejisi

---

## 1. Mevcut Veritabanı Yapısı ve Tablo Sahiplikleri

### 1.1. Genel Şema ve `DbContext` Dağılımı

| Şema | Modül / Sorumlu DbContext | Mevcut Tablolar | Durum |
|---|---|---|---|
| `public` | `AuthDbContext` | 27 Namines Tablosu + 7 ASP.NET Identity Tablosu | **Legacy / Parçalanacak** |
| `namines_vault` | `VaultDbContext` | `vault_backups`, `vault_restores`, `vault_schedules` | ✅ İzole (Faz 14.1) |
| `ground` | `GroundDbContext` | `ground_databases` | ✅ İzole (Faz 14.1) |
| `flow` | `FlowDbContext` | `automation_rules`, `automation_actions`, `automation_run_logs` | ✅ İzole (Faz 14.1) |
| `desk` | `DeskDbContext` | `desk_handoff_tokens`, `sql_query_history`, `saved_queries` | ✅ İzole (Faz 14.1) |
| `identity` | `IdentityModuleDbContext` *(Hedef)* | `users`, `roles`, `organizations`, `organization_members`, `team_invites`... | Hazırda Facade/Model |
| `studio` | `StudioDbContext` *(Hedef)* | `projects`, `branches`, `schema_versions`, `cross_database_relations` | Hazırda Facade/Model |
| `billing` | `BillingDbContext` *(Hedef)* | `usage_events`, `user_ai_quotas`, `billing_settings`, `stripe_events`... | Hazırda Facade/Model |
| `governance` | `GovernanceDbContext` *(Hedef)*| `change_requests`, `approvals`, `audit_logs`, `user_ai_policies` | Hazırda Facade/Model |
| `hub` | `HubDbContext` *(Hedef)* | `repo_issues`, `repo_issue_comments`, `repo_stars`, `repositories` | Hazırda Facade/Model |
| `gateway` | `GatewayDbContext` *(Hedef)* | `gateway_api_keys`, `table_permissions`, `audit_entries` | Hazırda Facade/Model |
| `ops` | `OpsDbContext` *(Hedef)* | `feedbacks`, `sql_execution_audits`, `feature_flags`, `ai_tool_routes` | Hazırda Facade/Model |

---

### 1.2. `AuthDbContext` İçinde Kalan 27 DbSet'in Hedef Modül Eşlemesi

| # | `AuthDbContext` DbSet | Hedef Modül | Hedef Şema & Tablo | Gerekçe & Sorumluluk Alanı |
|---|---|---|---|---|
| 1 | `CloudProjects` | **Studio** | `studio.projects` (+ 3NF parçaları) | Proje metaverisi, şema tanımı ve tasarım durumu. |
| 2 | `Branches` | **Studio** | `studio.branches` | Şema dallanma motoru ve versiyon kökleri. |
| 3 | `SchemaVersions` | **Studio** | `studio.schema_versions` | Şema versiyon geçmişi ve DDL snapshot'ları. |
| 4 | `CrossDatabaseRelations` | **Studio** | `studio.cross_database_relations` | Projeler arası mantıksal veritabanı ilişkileri. |
| 5 | `Organizations` | **Identity** | `identity.organizations` | Ekip ve organizasyon üst kimliği. |
| 6 | `OrganizationMembers` | **Identity** | `identity.organization_members` | Organizasyon içi üyelik ve RBAC rolleri. |
| 7 | `TeamInvites` | **Identity** | `identity.team_invites` | Ekip davet jetonları ve üyelik onayları. |
| 8 | `ChangeRequests` | **Governance** | `governance.change_requests` | Veritabanı PR'ları, onaylar ve risk değerlendirmesi. |
| 9 | `ChangeRequestApprovals` | **Governance** | `governance.change_request_approvals` | İki kişilik onay mekanizması oyları. |
| 10 | `ChangeRequestAuditLogs` | **Governance** | `governance.change_request_audit_logs` | CR onay zinciri ve migration uygulama denetimi. |
| 11 | `UserAIPolicies` | **Governance** | `governance.user_ai_policies` | AI araç kullanım izinleri ve otonomi modları. |
| 12 | `UserAIQuotas` | **Billing** | `billing.user_ai_quotas` | Kullanıcı AI token kotaları ve kalan haklar. |
| 13 | `UsageEvents` | **Billing** | `billing.usage_events` | Ölçümlenen faturalama olayları (en hızlı büyüyen tablo). |
| 14 | `UserBillingSettings` | **Billing** | `billing.user_billing_settings` | Kullanıcı harcama limitleri ve ödeme tercihleri. |
| 15 | `GlobalAiUsages` | **Billing** | `billing.global_ai_usages` | Günlük sistem geneli token kullanım havuzu. |
| 16 | `OrgAiUsages` | **Billing** | `billing.org_ai_usages` | Günlük organizasyon token kullanım havuzu. |
| 17 | `StripeWebhookEvents` | **Billing** | `billing.stripe_webhook_events` | İşlenmiş Stripe olay kimlikleri (idempotency kontrolü). |
| 18 | `GatewayApiKeys` | **Gateway** | `gateway.gateway_api_keys` | Gateway REST API erişim anahtarları ve oran sınırları. |
| 19 | `GatewayTablePermissions` | **Gateway** | `gateway.gateway_table_permissions` | Tablo bazlı okuma/yazma/maskeleme izinleri. |
| 20 | `GatewayAuditEntries` | **Gateway** | `gateway.gateway_audit_entries` | Gateway üzerinden yapılan tüm yazma işlemlerinin denetimi. |
| 21 | `RepoIssues` | **Hub** | `hub.repo_issues` | Hub üzerindeki mimari sorunlar ve tartışmalar. |
| 22 | `RepoIssueComments` | **Hub** | `hub.repo_issue_comments` | Sorunlara yazılan yorumlar ve yanıtlar. |
| 23 | `RepoStars` | **Hub** | `hub.repo_stars` | Proje yıldızlama ve topluluk popülerlik metrikleri. |
| 24 | `Feedbacks` | **Ops** | `ops.feedbacks` | Kullanıcı geri bildirimleri ve hata raporları. |
| 25 | `SqlExecutionAudits` | **Ops** | `ops.sql_execution_audits` | Canlı veritabanına uygulanan tüm SQL betiklerinin denetimi. |
| 26 | `FeatureFlags` | **Ops** | `ops.admin_feature_flags` | Sistem genelinde özellik açma/kapama (Kill-Switch). |
| 27 | `AiToolRoutes` | **Ops** | `ops.ai_tool_routes` | Dinamik model yönlendirme (OpenAI, Anthropic, Groq...). |

---

## 2. 3NF ve Normalizasyon Denetimi (Tablo Bazlı İnceleme)

### 2.1. Normalizasyon İlkeleri ve Pragmatik Sınırlar
- **Katı 3NF Dogmatizminden Kaçınma:**
  - `SchemaJson` ve `NodePositionsJson` alanları ilişkisel tablolara parçalanmayacaktır. Bu alanlar canvas ve şema modelinin atomik **Aggregate Root** belgeleridir. Bunları yüzlerce alt tabloya bölmek performans felaketine ve işlem karmaşıklığına yol açar.
  - Denetim (Audit) tablolarındaki anlık görüntü sütunları (`TargetHost`, `TargetDatabase`, `ChangeRequestTitle`, `ActorUserId`), hedef varlık silinse bile tarihsel izlenebilirliği korumak amacıyla bilerek **denormalize** bırakılmıştır.
- **Gerçek İhlallerin Ayrıştırılması:**
  - Virgülle ayrılmış çok değerli dizgeler (CSV) 1NF ihlalidir.
  - Tek bir tabloda 4 farklı iş alanının (Studio, Gateway Secret, Hub, Share Token) toplanması geçişli bağımlılık ve 3NF ihlalidir.

---

### 2.2. Detaylı Tablo Değerlendirmesi

#### A. `CloudProjects` (God Table İhlali → 4 Normalize Tablo)
- **Mevcut Sütunlar:** `Id`, `OrganizationId`, `UserId`, `Name`, `Description`, `DbType`, `SchemaJson`, `NodePositionsJson`, `EncryptedConnectionString`, `ConnectionDbType`, `AllowDeskSql`, `Slug`, `Visibility`, `Category`, `Tags`, `ForkedFromProjectId`, `LinkedGithubRepo`, `StarCount`, `ForkCount`, `ShareTokenHash`, `ShareToken`, `AutoApproveSafeChanges`, `CreatedAt`, `UpdatedAt`, `xmin`.
- **Birincil Anahtar:** `Id (text / uuid)`
- **Yabancı Anahtarlar:** `UserId -> AspNetUsers.Id`, `OrganizationId -> Organizations.Id`, `ForkedFromProjectId -> CloudProjects.Id`.
- **Fonksiyonel Bağımlılıklar:**
  - `Id -> Name, DbType, SchemaJson, NodePositionsJson, CreatedAt` (Çekirdek Proje)
  - `Id -> EncryptedConnectionString, ConnectionDbType, AllowDeskSql` (Bağlantı Sırrı - sadece canlı bağlı projelerde var)
  - `Id -> Slug, Visibility, Category, StarCount, ForkCount, LinkedGithubRepo` (Hub Metaverisi)
  - `Id -> ShareTokenHash, ShareToken` (Paylaşım Jetonu)
  - `Id -> Tags` (CSV Listesi - 1NF İhlali)
- **Normalizasyon Kararı:**
  1. `studio.projects`: Çekirdek proje kimliği, şema ve ayarları.
  2. `studio.project_connections` (1:0..1): `EncryptedConnectionString`, `ConnectionDbType`, `AllowDeskSql`. (NULL sütunlar elenir, sır güvenliği artar).
  3. `studio.project_shares` (1:0..1): `TokenHash` (Düz metin `ShareToken` silinir, DB-34 çözülür).
  4. `hub.repositories` (1:0..1): `Slug`, `Description`, `Visibility`, `Category`, `StarCount`, `ForkCount`, `LinkedGithubRepo`.
  5. `hub.project_tags` (1:N): `ProjectId`, `Tag` (1NF CSV temizliği).
- **Risk Değerlendirmesi:** Yüksek (Core entity); `Expand -> Backfill -> Dual-Read -> Contract` deseni zorunludur.

#### B. `GatewayApiKeys` (1NF CSV İhlalleri)
- **Mevcut Sütunlar:** `Id`, `ProjectId`, `KeyHash`, `Prefix`, `Name`, `CreatedAt`, `ExpiresAt`, `LastUsedAt`, `IsActive`, `CreatedByUserId`, `RateLimitPerMinute`, `AllowedOrigins`, `AllowedIps`.
- **İhlal:** `AllowedOrigins` (`"https://a.com,https://b.com"`) ve `AllowedIps` (`"10.0.0.1/24,192.168.1.1"`) sütunları virgüllü dizgedir (1NF ihlali).
- **Normalizasyon Kararı:**
  - `gateway.api_key_allowed_origins`: `(ApiKeyId, Origin)` PK.
  - `gateway.api_key_allowed_ips`: `(ApiKeyId, CidrOrIp)` PK.
- **Risk:** Düşük; Gateway doğrulama katmanı bu tabloları `IN` sorgusu veya bellekte HashSet ile okur.

#### C. `GatewayTablePermissions` (1NF CSV İhlali)
- **Mevcut Sütunlar:** `Id`, `ProjectId`, `TableName`, `AllowRead`, `AllowInsert`, `AllowUpdate`, `AllowDelete`, `AllowRpc`, `MaskedColumns`, `CreatedAt`, `UpdatedAt`.
- **İhlal:** `MaskedColumns` (`"tc_kimlik,kredi_karti,maas"`) sütunu 1NF ihlalidir.
- **Normalizasyon Kararı:**
  - `gateway.table_masked_columns`: `(PermissionId, ColumnName)` PK.
- **Risk:** Düşük; kolon maskeleme kontrolü ilişkisel tablo üzerinden kolayca yapılır.

#### D. `RepoIssues` (1NF CSV İhlali)
- **Mevcut Sütunlar:** `Id`, `ProjectId`, `IssueNumber`, `Title`, `Body`, `CreatedByUserId`, `CreatedAt`, `UpdatedAt`, `IsClosed`, `ClosedAt`, `ClosedByUserId`, `RelatedTableName`, `NslRuleCode`, `Labels`.
- **İhlal:** `Labels` (`"bug,security,high-priority"`) sütunu 1NF ihlalidir.
- **Normalizasyon Kararı:**
  - `hub.issue_labels`: `(IssueId, Label)` PK, `IX_issue_labels_Label` indeksi.
- **Risk:** Düşük; etiket bazlı filtreleme sorguları çok daha hızlı hale gelir.

#### E. `UserAIPolicies` (Tekrarlayan Sütun Grupları - 1NF)
- **Mevcut Sütunlar:** `Id`, `UserId`, `SmartSeed`, `Documentation`, `Scaffolding`, `SchemaGeneration`, `SchemaRevision`, `DbaAnalysis`, `Migration`, `Voice`, `UpdatedAt`.
- **İhlal:** 8 ayrı araç için sabit enum sütunları açılmıştır. Yeni bir AI aracı eklendiğinde DDL değişikliği ve migration gerekmektedir.
- **Normalizasyon Kararı:**
  - `governance.user_ai_tool_policies`: `(UserId, ToolKey)` PK, `Mode` (int), `UpdatedAt`.
- **Risk:** Düşük; arayüz ve yetki servisi dinamik hale gelir.

#### F. `ChangeRequests` (Nullable Test Koşusu Sütunları)
- **Mevcut Sütunlar:** `Id`, `ProjectId`, `BranchId`, `Title`, `Description`, `Status`, `RiskLevel`, `HeadVersionId`, `BaseVersionId`, `CreatedByUserId`, `CreatedAt`, `ReviewedAt`, `MergedAt`, `TestRunSuccess`, `TestRunSupported`, `TestRunMessage`, `TestRunFailedStatement`, `TestRunDurationMs`, `TestRunAt`.
- **İhlal:** 6 adet nullable `TestRun*` sütunu CR satırında tutulmaktadır. Birden fazla kez "Run Tests" tıklandığında eski test sonucu ezilmekte, geçmiş kaybolmaktadır.
- **Normalizasyon Kararı:**
  - `governance.change_request_test_runs`: `(Id [PK], ChangeRequestId [FK], Success, Supported, Message, FailedStatement, DurationMs, ExecutedAt)`.
- **Risk:** Düşük; test geçmişi korunur.

#### G. `AspNetUsers` (Stripe Abonelik Alanları)
- **Mevcut Sütunlar:** Identity çekirdek alanları + `StripeCustomerId`, `StripeSubscriptionId`, `SubscriptionStatus`, `CurrentPeriodEnd`, `PlanCode`.
- **İhlal:** Kullanıcı kimlik tablosu ile ödeme sağlayıcısı (Stripe) alanları karışmıştır.
- **Normalizasyon Kararı:**
  - `billing.subscriptions`: `UserId (PK, FK -> identity.users)`, `StripeCustomerId`, `StripeSubscriptionId`, `PlanCode`, `Status`, `CurrentPeriodEnd`, `UpdatedAt`.
- **Risk:** Orta; ödeme webhook'ları ve kullanıcı profil sorguları bu tabloya yönlendirilir.

---

## 3. PostgreSQL Güvenli Migration Stratejisi

### 3.1. Sıfır Veri Kaybı İlkesi (`Expand -> Backfill -> Verify -> Contract`)

1. **Aşama 1 — Expand (Genişletme):**
   - Yeni şemalar oluşturulur (`CREATE SCHEMA IF NOT EXISTS ...`).
   - Yeni normalize tablolar, kısıtlar ve indeksler oluşturulur.
   - Eski tablolar ve kolonlar yerinde bırakılır (çalışan uygulama kırılmaz).

2. **Aşama 2 — Backfill (Veri Aktarımı):**
   - `INSERT INTO ... SELECT ...` ile veriler eski tablolardan yeni tablolara dönüştürülerek kopyalanır.
   - Virgüllü dizgeler `string_to_array` ve `unnest` ile satırlara dönüştürülür:
     ```sql
     INSERT INTO hub.project_tags (project_id, tag)
     SELECT p."Id", trim(t)
     FROM public."CloudProjects" p,
          unnest(string_to_array(p."Tags", ',')) AS t
     WHERE p."Tags" IS NOT NULL AND trim(t) <> ''
     ON CONFLICT DO NOTHING;
     ```

3. **Aşama 3 — Verify (Doğrulama ve Bütünlük Kontrolü):**
   - Satır sayıları ve sağlama toplamları (checksum) işlem içinde doğrulanır:
     ```sql
     DO $$
     DECLARE
         v_old_count bigint;
         v_new_count bigint;
     BEGIN
         SELECT count(*) INTO v_old_count FROM public."CloudProjects";
         SELECT count(*) INTO v_new_count FROM studio.projects;
         IF v_old_count <> v_new_count THEN
             RAISE EXCEPTION 'Backfill dogrulama hatasi! Eski: %, Yeni: %', v_old_count, v_new_count;
         END IF;
     END $$;
     ```

4. **Aşama 4 — Switch & Contract (Geçiş ve Eski Kolonların Kaldırılması):**
   - Kod yeni modül tablolarını okuyup yazacak şekilde konuşlandırılır.
   - Doğrulama tamamlandıktan sonra eski sütunlar ve tablolar güvenle düşürülür (`ALTER TABLE ... DROP COLUMN ...`).

---

### 3.2. PostgreSQL Tablo ve Şema Taşıma Mekanizması (`ALTER TABLE SET SCHEMA`)

Mevcut tabloların yapısı bozulmadan yeni modül şemalarına taşınması gereken durumlarda (örneğin `UsageEvents` gibi çok büyük tablolar), veri kopyalamak yerine PostgreSQL'in metaveri seviyesinde anlık çalışan taşıma komutu kullanılacaktır:
```sql
ALTER TABLE public."UsageEvents" SET SCHEMA billing;
ALTER TABLE public."GatewayAuditEntries" SET SCHEMA gateway;
ALTER TABLE public."SqlExecutionAudits" SET SCHEMA ops;
```
*Bu işlem diskte veri bloklarını kopyalamaz; yalnızca pg_class kataloğunu günceller ve mikrosaniyeler içinde tamamlanır (downtime riski sıfırdır).*

---

### 3.3. Deterministik Migration Sıralaması (Topolojik Sıralama)

Modüller arasındaki bağımlılık zincirine göre migration çalıştırma sırası:

```
[10] Identity (Kullanıcılar, Roller, Organizasyonlar)
  ↓
[20] Studio (Projeler, Branşlar, Versiyonlar)
  ↓
[30] Billing (Abonelikler, Kotalar, Kullanım Olayları)
  ↓
[40] Governance (Change Request'ler, Onaylar, AI Politikaları)
  ↓
[50] Ground (Canlı Veritabanı Kayıtları)
  ↓
[60] Vault (Yedekler ve Zamanlayıcılar)
  ↓
[70] Flow (Otomasyon Kuralları ve Loglar)
  ↓
[80] Desk (SSO Jetonları, SQL Geçmişi)
  ↓
[90] Hub (Sorunlar, Yorumlar, Yıldızlar, Depolar)
  ↓
[100] Gateway (API Anahtarları, Tablo İzinleri, Denetim)
  ↓
[110] Ops (Geri Bildirimler, Bayraklar, Model Yolları)
```

---

### 3.4. Büyük Tablolarda Kilit (Lock) ve Downtime Önleme

- `UsageEvents`, `GatewayAuditEntries` ve `SqlExecutionAudits` tabloları yüz binlerce satır içerebilir.
- Bu tablolara yeni indeks eklenirken kesinlikle `CONCURRENTLY` kullanılmalıdır:
  ```sql
  CREATE INDEX CONCURRENTLY IF NOT EXISTS ix_usage_events_user_period
  ON billing.usage_events (user_id, billing_period, resource);
  ```
- Tablo kopyalama ve silme işlemleri transactional bloklarda (`BEGIN ... COMMIT`) tutulmalı, uzun süreli `ACCESS EXCLUSIVE` kilitleri engellenmelidir.

---

### 3.5. Rollback ve Acil Durum Kurtarma Planı

1. **İşlem İçi Geri Alma (Transaction Rollback):**
   - PostgreSQL DDL işlemleri tamamen transaction güvenlidir (`Transactional DDL`).
   - Migration betikleri `DO $$ BEGIN ... EXCEPTION WHEN OTHERS THEN RAISE; END $$;` bloklarıyla sarılır. Herhangi bir adım hata verirse tüm migration anında geri alınır (`ROLLBACK`), veritabanı tutarsız ara durumda kalmaz.
2. **Uygulama Sürüm Uyumluluğu:**
   - Kod geçişi öncesinde hem eski kolonları hem yeni tabloları destekleyen "köprü" katmanı (`TenantProjectAuthority` ve Facade'lar) devrede kalır.

---

## 4. 3NF ve Migration Kapsamı Önceliklendirme Matrisi

| Öncelik | Kapsam / İş | Gerekçe & Risk Seviyesi |
|---|---|---|
| **P0** | **Düz Metin Sırların ve Yetim Kaynakların Temizlenmesi**<br>• `CloudProjects.ShareToken` düz metin kolonunun kaldırılması<br>• `studio.project_connections` ve `studio.project_shares` ayrımı<br>• Modül tablolarındaki geçişli `OrganizationId` sütunlarının temizlenmesi | **Kritik Güvenlik ve Veri Bütünlüğü:** Sırların sızmasını önler, proje devirlerinde yetim kaynak kalmasını engeller. |
| **P1** | **`CloudProjects` God Table Ayrıştırması & 1NF CSV Kolonları**<br>• `studio.projects` ve `hub.repositories` ayrımı<br>• `hub.project_tags`, `hub.issue_labels`<br>• `gateway.api_key_allowed_origins`, `gateway.api_key_allowed_ips`<br>• `gateway.table_masked_columns` | **Mimari Sahiplik & 3NF:** Hub metaverisi ile Studio çekirdeği ayrışır, arama ve filtreleme performansı ilişkisel indekse kavuşur. |
| **P2** | **Kullanıcı Politikaları ve Test Koşuları İyileştirmesi**<br>• `governance.user_ai_tool_policies` (8 sütun → satır modeli)<br>• `governance.change_request_test_runs` (test tarihçesi)<br>• `billing.subscriptions` (Stripe alanlarının AspNetUsers'tan ayrılması)<br>• `ops.lifecycle_audit_logs` (Proje/hesap silme denetiminin CR logundan ayrılması) | **Bakım Kolaylığı & Genişletilebilirlik:** Yeni AI aracı eklendiğinde DDL gereksinimi biter; test ve yaşam döngüsü denetim kayıtları temizlenir. |
| **P3** | **Büyük Tablo Optimizasyonları (İsteğe Bağlı)**<br>• `billing.usage_events` için aylık aralık bazlı tablo bölümleme (Table Partitioning)<br>• `gateway.gateway_audit_entries` için arşivleme politikası | **Performans Optimizasyonu:** Gelecekte milyonlarca satıra ulaşıldığında sorgu hızını korur. |

---

## 5. Sıradaki Adım İçin Onay Noktası

Bu plan tamamen hazırdır ve analiz aşaması tamamlanmıştır. Kullanıcının açık onayı olmadan hiçbir veritabanı şema değişikliği veya kod migration'ı başlatılmayacaktır.
