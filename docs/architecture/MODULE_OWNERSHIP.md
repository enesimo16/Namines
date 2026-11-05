# MODULE OWNERSHIP — Modül Sahiplik Matrisi (Tablolar, Servisler, Worker'lar ve Uçlar)

> Tarih: 2026-11-02 · Her tablo, servis, arka plan işçisi ve HTTP ucu **tam olarak tek bir modüle** aittir.

---

## 1. Tam Sahiplik Tablosu

> **Gerçek durum (14.2D-1).** Aşağıdaki tablo HEDEF adları da içerir. Bugün fiziksel olarak:
> - `studio`: `projects`, `project_connections`, `project_shares`, `"Branches"`, `"SchemaVersions"`, `"CrossDatabaseRelations"`
> - `hub`: `repositories`, `project_tags`, `issue_labels`, `repo_issue_labels`, `"RepoIssues"`, `"RepoIssueComments"`, `"RepoStars"`
> - `gateway`: `"GatewayApiKeys"`, `"GatewayTablePermissions"`, `"GatewayAuditEntries"`, `api_key_allowed_origins`, `api_key_allowed_ips`, `table_masked_columns`
> - `ops`: `"Feedbacks"`, `"SqlExecutionAudits"`, `"FeatureFlags"`, `"AiToolRoutes"`
>
> Tablo adları taşımada değişmedi (`GatewayApiKeys` hedefteki `gateway_api_keys` olmadı): yeniden adlandırma ayrı bir karar, bu fazın kapsamı değil.
> Bu dört modülün bağlamı internal, kendi zinciri ve geçmiş tablosu var. Identity, Billing, Governance (tablolar `public`'te, 14.2D-2) ile Vault, Ground, Flow, Desk (tablolar kendi şemasında) DDL'i hâlâ `AuthDbContext` zincirinde.
> Proje yaşam döngüsü (oluşturma, güncelleme, org taşıması, otomatik onay) aktörlü `Namines.Studio.Application.IProjectLifecycleService`'te; okuma modeli Hub'ın uyguladığı `IProjectReadModelWriter` ile aynı işlemde. Gateway veri düzlemi kararları `Namines.Gateway.Application.IGatewayRequestGate`'te. Vault, Ground, Flow, Desk varlıklarının `AuthDbContext`'ten çıkması Faz 14.2D-2'nin işi.
> Sözleşmeler: `BuildingBlocks/Contracts/StudioContracts.cs` (`IStudioProjects`, `IStudioBranches`, `IStudioRelations`, `IStudioAdminQueries`, `IProjectDeletion`), `IStudioApplicationService`, `IProjectConnectionSecretProvider` (tek çözme kapısı), `ITenantProjectAuthority`, `IHubApplicationService`, `IGatewayAccess`/`IGatewayKeyService`/`IGatewayAuditLog`/`IGatewayMaintenance`, `IFeedbackService`, `ISqlExecutionAuditService`, `IFeatureFlagService`, `ICrossModuleTransaction` (`ITransactionParticipant` ile).
> Modüller arası FK kararı: `PHASE_14_2D_1_REPORT.md` §3.

| Sıra | Modül | PostgreSQL Şeması | DbContext | Sahip Olduğu Tablolar (Hedef 3NF) | Sahip Olduğu Uygulama Servisleri & İşçiler | Sahip Olduğu Controller / Uçlar |
|---|---|---|---|---|---|---|
| **1** | **Identity** | `identity` | `IdentityModuleDbContext` | `AspNetUsers`, `AspNetRoles`, `AspNetUserRoles`, `AspNetUserClaims`, `AspNetUserLogins`, `AspNetUserTokens`, `AspNetRoleClaims`, `organizations`, `organization_members`, `team_invites` | `AccountDeletionService`, `EncryptedUserStore`, `PwnedPasswordValidator`, `OrgAccess` (`ITenantProjectAuthority`) | `AuthController`, `TeamController`, `ProjectMemberController` |
| **2** | **Studio** | `studio` | `StudioDbContext` | `projects`, `project_connections`, `project_shares`, `cross_database_relations` | `DdlGeneratorFactory`, `EfCoreGenerator`, `PrismaGenerator`, `EjectGeneratorRegistry`, `LinterService`, `ScaffolderService`, `CoderAIPackagerService`, `DatabaseExecutorService`, `DbIntrospectionService`, `LaunchService`, `DockerSweeperBackgroundService`, `SandboxOutputRetentionService` | `SchemaController`, `CompileController`, `LintController`, `ScaffolderController`, `CoderAIController`, `DatabaseExecutorController`, `DbIntrospectController`, `ShareController`, `CrossDatabaseController`, `LaunchController`, `DockerController`, `DocumentationController`, `SmartSeedController`, `AIDbaController`, `VoiceController`, `CodeSchemaController`, `ReverseEngineerController`, `SourcesController` |
| **3** | **Governance** | `governance` | `GovernanceDbContext` | `branches`, `schema_versions`, `change_requests`, `change_request_approvals`, `change_request_test_runs`, `change_request_audit_logs` | `SchemaImpactAnalyzer`, `BranchDatabaseProvisioner`, `BranchTestRunnerService`, `MigrationService`, `ChangeRequestApprovalPolicy` | `BranchController`, `ChangeRequestController`, `MergeController`, `MigrationController` |
| **4** | **Hub** | `hub` | `HubDbContext` | `repositories`, `project_tags`, `repo_issues`, `issue_labels`, `repo_issue_comments`, `repo_stars` | `HubService`, `GithubBotService`, `GithubClient`, `RepositoryScanner`, `HubBlueprintCatalog` | `HubController`, `GithubScanController`, `GithubWebhookController` |
| **5** | **Gateway** | `gateway` | `GatewayDbContext` | `gateway_api_keys`, `api_key_allowed_origins`, `api_key_allowed_ips`, `gateway_table_permissions`, `table_masked_columns`, `gateway_audit_entries` | `GatewayService`, `GatewayAccess`, `GatewayAudit`, `GatewayAnalytics`, `GatewayRateLimiter`, `GatewayKeyRestrictions` | `GatewayController`, `GatewayKeyController` |
| **6** | **Desk** | `desk` | `DeskDbContext` | `desk_handoff_tokens`, `sql_query_history`, `saved_queries` | `DeskModule`, `SqlWorkbenchService`, `DeskHandoffService`, `DeskHandoffTokenValidator` | Desk Handoff & Workbench uçları (`/api/auth/desk-handoff`, `/api/gateway/{projectId}/sql/history`, `/saved`) |
| **7** | **Vault** | `namines_vault` | `VaultDbContext` | `vault_backups`, `vault_restores`, `vault_schedules` | `VaultModule`, `VaultService`, `VaultJobQueue`, `VaultBackupWorker`, `VaultScheduleBackgroundService`, `BackupCipher`, `S3BackupStore`, `FileSystemBackupStore`, `PostgresBackupProvider`, `MySqlFamilyBackupProvider` | `VaultController` (`/api/vault/*`) |
| **8** | **Ground** | `ground` | `GroundDbContext` | `ground_databases` | `GroundModule`, `GroundService`, `GroundPurgeBackgroundService`, `LocalPostgresProvider`, `NeonProvider`, `SupabaseProvider` | `GroundController` (`/api/ground/*`) |
| **9** | **Flow** | `flow` | `FlowDbContext` | `automation_rules`, `automation_actions`, `automation_run_logs` | `FlowModule`, `FlowService`, `FlowRuleMatcher`, `AutomationConditionEvaluator`, `AutomationExecutor`, `AutomationJobQueue`, `AutomationExecutorWorker`, `AutomationRunLogRetentionService` | `AutomationController` (`/api/automation/*`) |
| **10** | **Billing** | `billing` | `BillingDbContext` | `subscriptions`, `user_billing_settings`, `usage_events`, `user_ai_quotas`, `user_ai_tool_policies`, `user_ai_settings`, `global_ai_usages`, `org_ai_usages`, `stripe_webhook_events` | `AiQuotaService`, `UsageMeter`, `StripeSubscriptionCanceller` | `SubscriptionController`, `StripeWebhookController`, `QuotaController`, `PolicyController`, `UsageController` |
| **11** | **Ops** | `ops` | `OpsDbContext` | `feature_flags`, `ai_tool_routes`, `feedbacks`, `sql_execution_audits`, `lifecycle_audit_logs` | `FeatureFlagService`, `AiToolRouter`, `DataRetentionService`, `AdminTelemetryService` | `AdminController`, `FeaturesController`, `FeedbackController` |

---

## 2. Modüller Arası İzin Verilen İletişim Kanalları

1. **Proje Silme (`ProjectDeletion`):**
   - `Studio` / `Hub` silme isteğini başlatır -> `Ops` içinde `ProjectDeletionRequested` yaşam döngüsü denetim kaydı açılır -> `IVaultModule.PurgeProjectBackupsAsync(projectId)` -> `IGroundModule.DeprovisionProjectAsync(projectId)` -> `Studio` projeyi siler (diğer şemalardaki bağımlı kayıtlar FK `ON DELETE CASCADE` veya modül olayı `ProjectDeletedEvent` ile temizlenir).
2. **Kullanıcı Hesabı Silme (`AccountDeletion`):**
   - `Identity` modülü `AccountDeletionService` üzerinden ortak ekip projelerinin sahipliğini devreder, kişisel projeler için `ProjectDeletion` akışını çağırır, `Billing` modülünden Stripe iptalini (`ISubscriptionCanceller`) tetikler ve kullanıcıyı siler.
3. **Admin İstatistik ve Telemetri (`AdminController`):**
   - `AdminController` artık 13 tabloya doğrudan LINQ sorgusu atmaz; her modülün `.Contracts` içinde sunduğu salt-okunur özet metotlarını (`IVaultModule.GetAdminMetricsAsync`, `IGroundModule.GetAdminMetricsAsync`, `IFlowModule.GetAdminMetricsAsync`, `IDeskModule.GetAdminMetricsAsync`) çağırır.
