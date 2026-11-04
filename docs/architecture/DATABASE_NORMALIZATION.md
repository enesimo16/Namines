# DATABASE NORMALIZATION (3NF) & SCHEMA ISOLATION SPEC

> Tarih: 2026-11-02 · Kapsam: Control Plane PostgreSQL Veritabanı (1NF, 2NF, 3NF ve Şema Ayrımı)

---

## 1. Normalizasyon Kuralları (1NF → 2NF → 3NF)

1. **1NF (Birinci Normal Form — Atomiklik ve Tekrarlayan Grupların Kaldırılması):**
   - Hiçbir sütunda virgülle ayrılmış liste (`CSV`) tutulamaz.
   - Sabit sayıda tekrarlayan özellik sütunları (`SmartSeed`, `Documentation`, `Scaffolding`...) anahtar-değer ilişkisel tablosuna dönüştürülür.
2. **2NF (İkinci Normal Form — Tam Fonksiyonel Bağımlılık):**
   - Bileşik anahtarlı tablolarda anahtar olmayan her sütun, bileşik anahtarın tamamına bağımlı olmalıdır.
3. **3NF (Üçüncü Normal Form — Geçişli Bağımlılıkların Kaldırılması):**
   - Anahtar olmayan hiçbir sütun, anahtar olmayan başka bir sütuna bağımlı olamaz (`A -> B -> C` ise `B -> C` ayrı tabloya çıkarılır veya türetilmiş kopya kaldırılır).

---

## 2. Tespit Edilen İhlaller ve 3NF Dönüşüm Tablosu

### 2.1. 1NF Dönüşümleri (Atomik Olmayan Sütunlar ve Tekrarlayan Sütun Grupları)

| Mevcut Tablo & Sütun | İhlal Türü | Hedef Şema & Tablo | Hedef Sütunlar & Kısıtlar |
|---|---|---|---|
| `public."CloudProjects"."Tags"` (`"postgresql,3nf,saas"`) | 1NF (Çok değerli CSV) | `hub.project_tags` | `ProjectId (FK -> studio.projects.Id CASCADE)`, `Tag (varchar(64))`, **PK: `(ProjectId, Tag)`**, Index: `IX_project_tags_Tag` |
| `public."RepoIssues"."Labels"` (`"dba-risk,perf"`) | 1NF (Çok değerli CSV) | `hub.issue_labels` | `IssueId (FK -> hub.repo_issues.Id CASCADE)`, `Label (varchar(64))`, **PK: `(IssueId, Label)`**, Index: `IX_issue_labels_Label` |
| `public."GatewayApiKeys"."AllowedOrigins"` | 1NF (Çok değerli CSV) | `gateway.api_key_allowed_origins` | `ApiKeyId (FK -> gateway.gateway_api_keys.Id CASCADE)`, `Origin (varchar(256))`, **PK: `(ApiKeyId, Origin)`** |
| `public."GatewayApiKeys"."AllowedIps"` | 1NF (Çok değerli CSV) | `gateway.api_key_allowed_ips` | `ApiKeyId (FK -> gateway.gateway_api_keys.Id CASCADE)`, `CidrOrIp (varchar(64))`, **PK: `(ApiKeyId, CidrOrIp)`** |
| `public."GatewayTablePermissions"."MaskedColumns"` | 1NF (Çok değerli CSV) | `gateway.table_masked_columns` | `PermissionId (FK -> gateway.gateway_table_permissions.Id CASCADE)`, `ColumnName (varchar(128))`, **PK: `(PermissionId, ColumnName)`** |
| `public."UserAIPolicies"` (8 ayrı araç sütunu) | 1NF (Tekrarlayan sütun grubu) | `billing.user_ai_tool_policies` | `UserId (FK -> identity.AspNetUsers.Id CASCADE)`, `ToolKey (varchar(64))`, `Mode (int CHECK)`, `UpdatedAt (timestamptz)`, **PK: `(UserId, ToolKey)`** |

---

### 2.2. 3NF Dönüşümleri (Geçişli Bağımlılıklar ve "God Table" Ayrıştırması)

#### A. `CloudProjects` Tablosunun 4 Normalize Tabloya Ayrılması
Mevcut `CloudProjects` tablosu 4 farklı iş alanının verisini ve geçişli bağımlılıklarını tek satırda taşımaktadır:
1. **`studio.projects` (Çekirdek Proje Varlığı):**
   - `Id (PK)`, `OrganizationId (FK -> identity.organizations)`, `UserId (CreatedByUserId, FK -> identity.AspNetUsers)`, `Name`, `DbType`, `SchemaJson`, `NodePositionsJson`, `AutoApproveSafeChanges`, `CreatedAt`, `UpdatedAt`, `xmin (RowVersion)`.
2. **`studio.project_connections` (Canlı Veritabanı Bağlantı Sırrı ve Desk İzni — 1:0..1):**
   - `ProjectId (PK, FK -> studio.projects.Id ON DELETE CASCADE)`, `EncryptedConnectionString (text NOT NULL)`, `ConnectionDbType (varchar(64) NOT NULL)`, `AllowDeskSql (boolean NOT NULL DEFAULT false)`, `UpdatedAt (timestamptz)`.
   - *3NF & Güvenlik Kazanımı:* Canlı bağlantısı olmayan projelerde NULL sütun taşınmaz; `EncryptedConnectionString` yalnızca bağlantı çözen servislerce okunur.
3. **`studio.project_shares` (Salt-Okunur Paylaşım Jetonu — 1:0..1):**
   - `ProjectId (PK, FK -> studio.projects.Id ON DELETE CASCADE)`, `TokenHash (varchar(64) UNIQUE NOT NULL)`, `CreatedAt (timestamptz)`.
   - *3NF & Güvenlik Kazanımı:* Düz metin `ShareToken` sütunu (DB-34) kalıcı olarak ortadan kalkar.
4. **`hub.repositories` (Namines Hub Yayın ve Sosyal Meta Verisi — 1:0..1):**
   - `ProjectId (PK, FK -> studio.projects.Id ON DELETE CASCADE)`, `Slug (varchar(128))`, `Description (varchar(1000))`, `Visibility (int CHECK)`, `Category (varchar(64))`, `ForkedFromProjectId (FK -> studio.projects.Id ON DELETE SET NULL)`, `LinkedGithubRepo (varchar(140))`, `StarCount (int CHECK >= 0)`, `ForkCount (int CHECK >= 0)`, `UpdatedAt (timestamptz)`.

#### B. Modül Tablolarındaki Geçişli `OrganizationId` Sütunlarının Kaldırılması (`ProjectId -> OrganizationId`)
- `namines_vault.vault_backups`, `namines_vault.vault_restores`, `namines_vault.vault_schedules` ve `ground.ground_databases` tablolarındaki `OrganizationId` sütunu `ProjectId` üzerinden geçişli bağımlıdır (`ProjectId -> studio.projects.OrganizationId`).
- Bu sütun kaldırılarak veri anomalisi (proje başka bir organizasyona devredildiğinde yedeklerin eski organizasyonda kalması) tamamen önlenir.

#### C. `ChangeRequests` ve `ChangeRequestAuditLogs` Ayrıştırması
1. **`governance.change_request_test_runs`:**
   - `ChangeRequests` üzerindeki 6 adet nullable `TestRun*` sütunu (`TestRunSuccess`, `TestRunSupported`, `TestRunMessage`, `TestRunFailedStatement`, `TestRunDurationMs`, `TestRunAt`) ayrı `governance.change_request_test_runs` tablosuna çıkarılır (`Id`, `ChangeRequestId`, `Success`, `Supported`, `Message`, `FailedStatement`, `DurationMs`, `ExecutedAt`). Böylece her test koşusu tarihçesiyle korunur ve 3NF sağlanır.
2. **`ops.lifecycle_audit_logs`:**
   - `ChangeRequestAuditLogs` içine sıkıştırılmış olan `ProjectDeletionRequested`, `ProjectDeleted`, `ProjectDeletionFailed`, `AccountDeleted` olayları kendi bağımsız `ops.lifecycle_audit_logs` tablosuna (`Id`, `EntityType`, `EntityId`, `EntityName`, `Action`, `ActorUserId`, `DetailsJson`, `CreatedAt`) ayrılır.

#### D. `AspNetUsers` İçindeki Stripe Abonelik Bilgilerinin Ayrıştırılması
- `AspNetUsers` üzerindeki `StripeCustomerId`, `StripeSubscriptionId`, `SubscriptionStatus`, `CurrentPeriodEnd`, `PlanCode` sütunları `billing.subscriptions` tablosuna (`UserId [PK/FK]`, `StripeCustomerId`, `StripeSubscriptionId`, `PlanCode`, `Status`, `CurrentPeriodEnd`, `UpdatedAt`) taşınır.

---

## 3. Veritabanı Bütünlük ve İndeks Kuralları (Tüm Şemalar)

- **Yabancı Anahtar (FK) Politikası:**
  - Kişisel/geçmiş veriler (`desk.sql_query_history`, `desk.saved_queries`, `desk.desk_handoff_tokens`, `hub.repo_stars`, `billing.user_ai_quotas`): `ON DELETE CASCADE`.
  - Dış fiziksel kaynak temsil eden tablolar (`namines_vault.vault_backups`, `ground.ground_databases`): `ON DELETE RESTRICT` (önce dış kaynak silinmeli).
  - Atıf (attribution) sütunları (`CreatedByUserId`, `AuthorUserId`, `PerformedByUserId`): `ON DELETE SET NULL`.
  - Denetim (audit) tabloları (`gateway.gateway_audit_entries`, `ops.sql_execution_audits`, `ops.lifecycle_audit_logs`, `governance.change_request_audit_logs`): Silinen varlıktan uzun yaşaması için hedef varlığa FK konulmaz, anlık görüntü (snapshot) sütunları korunur.
