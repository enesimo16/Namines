# Independent Principal Engineer Acceptance Audit (`FINAL_ACCEPTANCE_AUDIT.md`)

**Date:** 2026-11-02  
**Auditor Role:** Independent Principal Engineer (Adversarial Acceptance Review)  
**Target Repository:** `Namines` (`backend/Namines.sln`)  
**Decision:** **CONDITIONALLY ACCEPTED** (Foundational Modular Monolith & 4 Core Domain Modules Verified; 7 Facade Modules, Physical Per-Module EF Migrations, and Full `AuthDbContext` Retirement Remain Incomplete)

---

## 1. Executive Verdict

The previous implementation report **overstated** the completion of the Modular Monolith, Layered Architecture, PostgreSQL schema isolation, and 3NF transformation across all 11 domains.

While significant, high-quality architectural work was genuinely implemented and verified for **4 extracted modules** (`Vault`, `Ground`, `Flow`, `Desk`) and the shared kernel (`Namines.BuildingBlocks`), the remaining **7 modules** (`Identity`, `Studio`, `Billing`, `Governance`, `Hub`, `Gateway`, `Ops`) exist only as **DbContext + Module Facade shells** inside `Namines.BuildingBlocks/Modules/` with **zero extracted application services** and **zero production consumers**. Furthermore, `AuthDbContext` still retains all **37 `DbSet`s** and is still directly injected into **49 production classes** across `Namines.API` and `Namines.Infrastructure`.

During this acceptance audit, **two concrete implementation defects** were uncovered and fixed:
1. **`SharedTransactionManager` Connection Corruption Risk:** Secondary `DbContext` instances had their `DbConnection` overwritten via `SetDbConnection(primaryConnection)` without restoring their original `DbConnection` or clearing `UseTransactionAsync(null)` in a `finally` block. This was fixed and verified with unit/integration tests.
2. **`SemanticCacheService` DI Lifetime Defect:** Registered as `AddScoped<SemanticCacheService>()` in `ServiceCollectionExtensions.cs:273` despite being an in-memory cache service intended to live across requests (and alongside `AddMemoryCache()`). Fixed to `AddSingleton<SemanticCacheService>()`.

Because all **2,589 non-Docker unit/architecture/snapshot tests** pass (0 failures), the 4 core modules (`Vault`, `Ground`, `Flow`, `Desk`) are genuinely extracted with real controller/worker consumers, and critical tenant/security boundaries (`ITenantProjectAuthority`, `InboundWebhookSignatureVerifier`, SSRF/DNS rebinding defenses, cache isolation) are enforced and tested, the current phase is **CONDITIONALLY ACCEPTED** as **Phase 1–2 (Core Module Extraction & Foundation)** rather than full 11-module retirement of `AuthDbContext`.

---

## 2. Verified Architectural Achievements

The following claims from the previous report were **independently verified against actual source files, symbols, DI registrations, and tests**:

| Claim | Concrete Evidence (Files & Symbols) | Verification Status |
|---|---|---|
| **Shared Kernel (`Namines.BuildingBlocks`)** | `backend/Namines.BuildingBlocks/Namines.BuildingBlocks.csproj` (zero project references); contains `ITenantProjectAuthority`, `ISharedTransactionManager`, `ModuleMigrationOrchestrator`, `InboundWebhookSignatureVerifier`. | **VERIFIED** |
| **Vault Module Layered Extraction** | `backend/Namines.Vault/` (`Domain/`, `Application/VaultApplicationService.cs`, `Infrastructure/Persistence/VaultDbContext.cs`, `Infrastructure/Workers/VaultBackupWorker.cs`, `VaultScheduleBackgroundService.cs`). Consumed by `VaultController.cs` and `AdminController.cs`. | **VERIFIED** |
| **Ground Module Layered Extraction** | `backend/Namines.Ground/` (`Domain/`, `Application/GroundApplicationService.cs`, `Infrastructure/Persistence/GroundDbContext.cs`, `Infrastructure/Workers/GroundPurgeBackgroundService.cs`). Consumed by `GroundController.cs` and `AdminController.cs`. | **VERIFIED** |
| **Flow Module Layered Extraction** | `backend/Namines.Flow/` (`Domain/`, `Application/FlowApplicationService.cs`, `Infrastructure/Persistence/FlowDbContext.cs`, `Infrastructure/Workers/FlowExecutorWorker.cs`, `FlowRunLogRetentionService.cs`). Consumed by `AutomationController.cs` and `AdminController.cs`. | **VERIFIED** |
| **Desk Module Layered Extraction** | `backend/Namines.Desk/` (`Domain/`, `Application/DeskApplicationService.cs`, `Infrastructure/Persistence/DeskDbContext.cs`). Consumed by `AuthController.cs`, `GatewayKeyController.cs`, `AdminController.cs`, and `SqlWorkbenchService.cs`. | **VERIFIED** |
| **AdminController Decoupled from Module DbSets** | `backend/Namines.API/Controllers/AdminController.cs` injects `IVaultModule`, `IGroundModule`, `IFlowModule`, `IDeskModule` and calls summary methods (`GetProjectBackupSummaryAsync`, `GetProjectGroundSummaryAsync`, `GetProjectFlowSummaryAsync`, `GetProjectDeskSummaryAsync`) instead of querying `AuthDbContext.VaultBackups`, `GroundDatabases`, `AutomationRules`, `AutomationRunLogs`, or `SqlQueryHistory`. | **VERIFIED** |
| **Live Tenant Authority (`ITenantProjectAuthority`)** | `backend/Namines.Infrastructure/Persistence/TenantProjectAuthority.cs` queries `CloudProject` live by `ProjectId` and verifies `UserId` or `OrganizationMember` membership, eliminating trust in stale denormalized `OrganizationId` fields inside `Vault` and `Ground`. | **VERIFIED** |

---

## 3. Disproved or Partially Implemented Claims

| Previous Claim | Actual Repository State | Verdict |
|---|---|---|
| *"All 11 modules are extracted into layered modules"* | Only **4 modules** (`Vault`, `Ground`, `Flow`, `Desk`) have dedicated `.csproj` assemblies and `Application`/`Domain`/`Infrastructure` layers. The other **7 modules** (`Identity`, `Studio`, `Billing`, `Governance`, `Hub`, `Gateway`, `Ops`) live inside a single folder (`backend/Namines.BuildingBlocks/Modules/`) as `DbContext` + `I*Module` facade classes that expose only `TDbContext DbContext { get; }`. | **PARTIALLY IMPLEMENTED (4/11 Full, 7/11 Facade Shells)** |
| *"Cross-module database access via `AuthDbContext` is eliminated"* | `AuthDbContext` still contains **all 37 original `DbSet`s** (`AuthDbContext.cs:12-69`) and is injected into **49 production classes**. Furthermore, legacy `VaultService` and `GroundService` still exist in `Namines.Infrastructure/Services/` and are injected by `LaunchService.cs` and `ProjectDeletionService.cs`. | **DISPROVED** |
| *"3NF database normalization is complete across the database"* | C# entity models (`ProjectConnection`, `ProjectGitRepository`, `ProjectPublication`, `SchemaShareLink`) and idempotent SQL scripts (`ThreeNfAndSchemaNormalizationSql.cs`) were created and unit-tested, but **no EF Core migration runs `ThreeNfAndSchemaNormalizationSql`**, and **no production service reads/writes the 4 decomposed `studio`/`hub` tables yet**. Nine other proposed 1NF/2NF/3NF items remain unimplemented. | **PARTIALLY IMPLEMENTED (Models & SQL Artifact Only)** |
| *"Per-module isolated EF Core migrations and `ModuleMigrationOrchestrator` are active"* | `ModuleMigrationOrchestrator` is registered in DI and tested in unit tests, but **none of the 11 module `DbContext`s have physical EF Core Migrations or ModelSnapshots**, and `Program.cs` does not call `ModuleMigrationOrchestrator.MigrateAllAsync()`. | **DISPROVED (DI Wiring Only)** |

---

## 4. `AuthDbContext` Retirement Analysis

### 4.1 Current `DbSet` Count & Dual-Context Exposure
`backend/Namines.Infrastructure/Persistence/AuthDbContext.cs` still defines **37 `DbSet<T>` properties** covering every table in the system.

Worse, **10 tables are currently mapped in both `AuthDbContext` (legacy) and an extracted module `DbContext` (active)**, creating dual-context write paths:

| Table (`Schema.Table`) | Primary Module `DbContext` | Still in `AuthDbContext`? | Legacy Classes Still Writing/Reading via `AuthDbContext` | Dual-Write Risk |
|---|---|---|---|---|
| `namines_vault.vault_backups` | `VaultDbContext` | YES (`VaultBackups`) | `LaunchService` (via legacy `VaultService`), `ProjectDeletionService` (via legacy `VaultService`), `DataRetentionService` | **HIGH** |
| `namines_vault.vault_restores` | `VaultDbContext` | YES (`VaultRestores`) | Legacy `VaultService` | **MEDIUM** |
| `namines_vault.vault_schedules` | `VaultDbContext` | YES (`VaultSchedules`) | `LaunchService` (via legacy `VaultService`), `ProjectDeletionService` | **HIGH** |
| `ground.ground_databases` | `GroundDbContext` | YES (`GroundDatabases`) | `LaunchService` (via legacy `GroundService`), `ProjectDeletionService` (via legacy `GroundService`) | **HIGH** |
| `flow.automation_rules` | `FlowDbContext` | YES (`AutomationRules`) | Legacy `AutomationExecutor` (still registered in `ServiceCollectionExtensions.cs:270`) | **MEDIUM** |
| `flow.automation_run_logs` | `FlowDbContext` | YES (`AutomationRunLogs`) | Legacy `AutomationExecutor`, `DataRetentionService` | **MEDIUM** |
| `flow.automation_webhook_deliveries` | `FlowDbContext` | YES (`AutomationWebhookDeliveries`) | Legacy `AutomationExecutor`, `DataRetentionService` | **MEDIUM** |
| `desk.desk_connections` | `DeskDbContext` | YES (`DeskConnections`) | None in production (migrated to `IDeskModule`) | **LOW** |
| `desk.desk_handoff_tokens` | `DeskDbContext` | YES (`DeskHandoffTokens`) | `AuthController.cs` (lines 330, 346 via `DeskHandoff`), `DataRetentionService.cs` (line 137) | **HIGH** |
| `desk.sql_query_history` | `DeskDbContext` | YES (`SqlQueryHistory`) | `DataRetentionService.cs` (line 103) | **MEDIUM** |

### 4.2 Complete Table Ownership & Access Matrix (All 37 `AuthDbContext` DbSets)

| # | `AuthDbContext` DbSet | Target Module | Target Schema | Current Active Production Access Path |
|---|---|---|---|---|
| 1 | `Users` | `Identity` | `identity` (currently `public`) | `AuthDbContext` (AuthController, GithubController, CliAuthController, BillingService, etc.) |
| 2 | `Organizations` | `Identity` | `identity` (currently `public`) | `AuthDbContext` (OrganizationController, TeamService, AdminController) |
| 3 | `OrganizationMembers` | `Identity` | `identity` (currently `public`) | `AuthDbContext` (TenantProjectAuthority, TeamService, OrganizationController) |
| 4 | `OrganizationInvites` | `Identity` | `identity` (currently `public`) | `AuthDbContext` (TeamService, OrganizationController) |
| 5 | `CliAuthSessions` | `Identity` | `identity` (currently `public`) | `AuthDbContext` (CliAuthController, DataRetentionService) |
| 6 | `CloudProjects` | `Studio` | `studio` (currently `public`) | `AuthDbContext` (ProjectsController, HubController, BranchesController, LaunchService, etc.) |
| 7 | `SchemaBranches` | `Studio` | `studio` (currently `public`) | `AuthDbContext` (BranchesController, CanvasHub, GithubBotService, etc.) |
| 8 | `SchemaSnapshots` | `Studio` | `studio` (currently `public`) | `AuthDbContext` (SnapshotsController, BranchesController, DataRetentionService) |
| 9 | `CanvasComments` | `Studio` | `studio` (currently `public`) | `AuthDbContext` (CommentsController, CanvasHub) |
| 10 | `UserTables` | `Studio` | `studio` (currently `public`) | `AuthDbContext` (UserTablesController) |
| 11 | `ProjectActivities` | `Studio` | `studio` (currently `public`) | `AuthDbContext` (ProjectActivityService, DataRetentionService) |
| 12 | `Subscriptions` | `Billing` | `billing` (currently `public`) | `AuthDbContext` (BillingController, BillingService, AdminController) |
| 13 | `ProcessedWebhookEvents` | `Billing` | `billing` (currently `public`) | `AuthDbContext` (BillingController, DataRetentionService) |
| 14 | `UsageEvents` | `Billing` | `billing` (currently `public`) | `AuthDbContext` (UsageMeterService, BillingService, AdminController, DataRetentionService) |
| 15 | `ChangeRequests` | `Governance` | `governance` (currently `public`) | `AuthDbContext` (ChangeRequestsController, GithubBotService, LaunchService) |
| 16 | `ChangeRequestComments` | `Governance` | `governance` (currently `public`) | `AuthDbContext` (ChangeRequestsController) |
| 17 | `ChangeRequestAuditLogs` | `Governance` | `governance` (currently `public`) | `AuthDbContext` (ChangeRequestsController, DataRetentionService) |
| 18 | `AppliedMigrationRecords` | `Governance` | `governance` (currently `public`) | `AuthDbContext` (MigrationsController, ChangeRequestsController) |
| 19 | `LifecycleAuditLogs` | `Governance` | `governance` (currently `public`) | `AuthDbContext` (LifecycleAuditService, AdminController, DataRetentionService) |
| 20 | `RepoIssues` | `Hub` | `hub` (currently `public`) | `AuthDbContext` (HubController, AdminController) |
| 21 | `RepoIssueComments` | `Hub` | `hub` (currently `public`) | `AuthDbContext` (HubController) |
| 22 | `RepoStars` | `Hub` | `hub` (currently `public`) | `AuthDbContext` (HubController) |
| 23 | `GatewayApiKeys` | `Gateway` | `gateway` (currently `public`) | `AuthDbContext` (GatewayKeyController, GatewayController) |
| 24 | `GatewayAuditLogs` | `Gateway` | `gateway` (currently `public`) | `AuthDbContext` (GatewayController, GatewayKeyController, DataRetentionService) |
| 25 | `VaultBackups` | `Vault` | `namines_vault` | `VaultDbContext` (primary) + `AuthDbContext` (legacy `VaultService`, `DataRetentionService`) |
| 26 | `VaultRestores` | `Vault` | `namines_vault` | `VaultDbContext` (primary) + `AuthDbContext` (legacy `VaultService`) |
| 27 | `VaultSchedules` | `Vault` | `namines_vault` | `VaultDbContext` (primary) + `AuthDbContext` (legacy `VaultService`) |
| 28 | `GroundDatabases` | `Ground` | `ground` | `GroundDbContext` (primary) + `AuthDbContext` (legacy `GroundService`) |
| 29 | `AutomationRules` | `Flow` | `flow` | `FlowDbContext` (primary) + `AuthDbContext` (legacy `AutomationExecutor`) |
| 30 | `AutomationRunLogs` | `Flow` | `flow` | `FlowDbContext` (primary) + `AuthDbContext` (`DataRetentionService`) |
| 31 | `AutomationWebhookDeliveries` | `Flow` | `flow` | `FlowDbContext` (primary) + `AuthDbContext` (`DataRetentionService`) |
| 32 | `DeskConnections` | `Desk` | `desk` | `DeskDbContext` (primary) |
| 33 | `DeskHandoffTokens` | `Desk` | `desk` | `DeskDbContext` (primary) + `AuthDbContext` (`AuthController`, `DataRetentionService`) |
| 34 | `SqlQueryHistory` | `Desk` | `desk` | `DeskDbContext` (primary) + `AuthDbContext` (`DataRetentionService`) |
| 35 | `Feedbacks` | `Ops` | `ops` (currently `public`) | `AuthDbContext` (FeedbackController, AdminController) |
| 36 | `SystemSettings` | `Ops` | `ops` (currently `public`) | `AuthDbContext` (SystemSettingsService, AdminController) |
| 37 | `AdminAuditLogs` | `Ops` | `ops` (currently `public`) | `AuthDbContext` (AdminAuditService, AdminController, DataRetentionService) |

### 4.3 Concrete `AuthDbContext` Retirement Plan
1. **Immediate Step 1 (Remove 10 Extracted Module DbSets from `AuthDbContext`):**
   - Switch `LaunchService` and `ProjectDeletionService` from injecting legacy `VaultService` / `GroundService` to injecting `IVaultModule` / `IGroundModule`.
   - Switch `AuthController` (`desk-handoff` endpoints at lines 330, 346) from `DeskHandoff.IssueAsync(_db, ...)` to `IDeskModule.Application.IssueHandoffTokenAsync(...)`.
   - Switch `DataRetentionService` to invoke retention methods on `IVaultModule`, `IFlowModule`, and `IDeskModule`.
   - Delete legacy `VaultService.cs`, `GroundService.cs`, `AutomationExecutor.cs`, and duplicate workers in `Namines.Infrastructure/Services/`.
   - Remove the 10 `Vault`, `Ground`, `Flow`, and `Desk` `DbSet`s from `AuthDbContext`.
2. **Step 2 (Extract `Ops`, `Gateway`, `Billing`, `Governance`, `Hub` Application Layers):**
   - Move `SystemSettingsService`, `AdminAuditService`, `GatewayService`, `BillingService`, `UsageMeterService`, `LifecycleAuditService`, and `HubController` persistence queries behind their respective module facades (`IOpsModule`, `IGatewayModule`, `IBillingModule`, `IGovernanceModule`, `IHubModule`).
3. **Step 3 (Extract `Studio` and `Identity` Core Modules):**
   - Migrate `CloudProjects`/`SchemaBranches`/`SchemaSnapshots` queries to `StudioDbContext` and `Users`/`Organizations` to `IdentityDbContext`, retiring `AuthDbContext` completely.

---

## 5. 11-Module Verification Matrix

| # | Module | Dedicated `.csproj`? | Domain Layer | Application Layer | Isolated `DbContext` | Schema Name | DI Registered? | Real Production Consumers | Classification |
|---|---|---|---|---|---|---|---|---|---|
| 1 | **Vault** | YES (`Namines.Vault`) | YES (`BackupCipher`, providers) | YES (`VaultApplicationService`) | `VaultDbContext` | `namines_vault` | YES | `VaultController`, `AdminController`, `VaultBackupWorker`, `VaultScheduleBackgroundService` | **GENUINELY EXTRACTED MODULE** |
| 2 | **Ground** | YES (`Namines.Ground`) | YES (`Neon`, `Supabase`, `LocalPostgres`) | YES (`GroundApplicationService`) | `GroundDbContext` | `ground` | YES | `GroundController`, `AdminController`, `GroundPurgeBackgroundService` | **GENUINELY EXTRACTED MODULE** |
| 3 | **Flow** | YES (`Namines.Flow`) | YES (`FlowRuleMatcher`) | YES (`FlowApplicationService`) | `FlowDbContext` | `flow` | YES | `AutomationController`, `AdminController`, `FlowExecutorWorker`, `FlowRunLogRetentionService` | **GENUINELY EXTRACTED MODULE** |
| 4 | **Desk** | YES (`Namines.Desk`) | YES (`DeskHandoffTokenValidator`) | YES (`DeskApplicationService`) | `DeskDbContext` | `desk` | YES | `AuthController`, `GatewayKeyController`, `AdminController`, `SqlWorkbenchService` | **GENUINELY EXTRACTED MODULE** |
| 5 | **Identity** | NO (`BuildingBlocks/Modules/IdentityModule.cs`) | In `Namines.Core` | NONE (`DbContext` property only) | `IdentityDbContext` | `identity` | YES | **0** (tests only) | **FACADE-ONLY SHELL** |
| 6 | **Studio** | NO (`BuildingBlocks/Modules/StudioModule.cs`) | In `Namines.Core` | NONE (`DbContext` property only) | `StudioDbContext` | `studio` | YES | **0** (tests only) | **FACADE-ONLY SHELL** |
| 7 | **Billing** | NO (`BuildingBlocks/Modules/BillingModule.cs`) | In `Namines.Core` | NONE (`DbContext` property only) | `BillingDbContext` | `billing` | YES | **0** (tests only) | **FACADE-ONLY SHELL** |
| 8 | **Governance** | NO (`BuildingBlocks/Modules/GovernanceModule.cs`) | In `Namines.Core` | NONE (`DbContext` property only) | `GovernanceDbContext` | `governance` | YES | **0** (tests only) | **FACADE-ONLY SHELL** |
| 9 | **Hub** | NO (`BuildingBlocks/Modules/HubModule.cs`) | In `Namines.Core` | NONE (`DbContext` property only) | `HubDbContext` | `hub` | YES | **0** (tests only) | **FACADE-ONLY SHELL** |
| 10 | **Gateway** | NO (`BuildingBlocks/Modules/GatewayModule.cs`) | In `Namines.Core` | NONE (`DbContext` property only) | `GatewayDbContext` | `gateway` | YES | **0** (tests only) | **FACADE-ONLY SHELL** |
| 11 | **Ops** | NO (`BuildingBlocks/Modules/OpsModule.cs`) | In `Namines.Core` | NONE (`DbContext` property only) | `OpsDbContext` | `ops` | YES | **0** (tests only) | **FACADE-ONLY SHELL** |

---

## 6. 1NF / 2NF / 3NF Normalization Verification Matrix

Each of the 12 normalization items from the original architectural plan was inspected against `Namines.Core/Models/`, `Namines.BuildingBlocks/Modules/`, `ThreeNfAndSchemaNormalizationSql.cs`, and active services:

| # | Normalization Item | Current Storage in Codebase | Status | Technical Evidence & Justification |
|---|---|---|---|---|
| 1 | **Project Tags** | `CloudProject.Tags` (`string`, comma-separated, max 500 chars) | **NOT IMPLEMENTED** | Still stored as comma-separated string on `CloudProject` and filtered via string splitting in `HubController.cs`. Should be normalized to `hub.project_tags(project_id, tag)` if tag facet counts or exact index lookups are required. |
| 2 | **Issue Labels** | `RepoIssue.Labels` (`string`, comma-separated, max 200 chars) | **NOT IMPLEMENTED** | Still stored as comma-separated string on `RepoIssue.cs:26`. |
| 3 | **Gateway AllowedOrigins** | `GatewayApiKey.AllowedOrigins` (`string?`, comma-separated) | **NOT IMPLEMENTED** | Still parsed via `.Split(',')` in `GatewayService.cs`. |
| 4 | **Gateway AllowedIps** | `GatewayApiKey.AllowedIps` (`string?`, comma-separated CIDR/IPs) | **NOT IMPLEMENTED** | Still parsed via `.Split(',')` in `GatewayService.cs`. |
| 5 | **MaskedColumns** | `GatewayApiKey.MaskedColumnsJson` (`string?`, JSON array) | **NOT IMPLEMENTED** | Stored as JSON array on `GatewayApiKey.cs`. |
| 6 | **User AI Tool Policies** | `User.DisabledAiTools` (`string?`) + `UserAIPolicy` entity in `IdentityModule.cs` | **IMPLEMENTED BUT NOT VERIFIED** | `UserAIPolicy` entity (`Normalized3NfEntities.cs:62`) and `IdentityDbContext.UserAiPolicies` (`identity.user_ai_policies`) exist, but `User.cs` still holds `DisabledAiTools` and production services still read `User`. |
| 7 | **Change Request Test Runs** | `ChangeRequest.LastTestRun*` scalar columns on `ChangeRequest.cs` | **NOT IMPLEMENTED** | Test run status/logs are overwritten in-place on `ChangeRequest` rather than stored in an append-only `governance.change_request_test_runs` history table. |
| 8 | **Lifecycle Audit Logs** | `LifecycleAuditLog` (`AuthDbContext.LifecycleAuditLogs`) | **NOT REQUIRED — WITH TECHNICAL JUSTIFICATION** | `LifecycleAuditLog` is an immutable append-only audit trail (`Id`, `ProjectId`, `ActorUserId`, `Action`, `DetailJson`, `CreatedAt`). `DetailJson` is polymorphic event payload metadata and does not violate 3NF functional dependencies for primary query patterns (`(ProjectId, CreatedAt)`). |
| 9 | **Billing Subscriptions** | `Subscription` + `UserAIQuota` / `UserBillingSettings` (`Normalized3NfEntities.cs`) | **IMPLEMENTED BUT NOT VERIFIED** | `UserAIQuota` (`billing.user_ai_quotas`) and `UserBillingSettings` (`billing.user_billing_settings`) are modeled in `BillingDbContext`, and `Subscription` has a partial unique index `IX_Subscriptions_UserId_ActiveUnique` (`WHERE "Status" IN ('active', 'trialing', 'past_due')`), but `BillingService` still reads/writes `User` and `AuthDbContext.Subscriptions`. |
| 10 | **OrganizationId Duplication** | `VaultBackup.OrganizationId`, `GroundDatabase.OrganizationId` + `ITenantProjectAuthority` | **IMPLEMENTED BUT NOT VERIFIED** | Authorization now resolves `OrganizationId` live from `CloudProject.OrganizationId` via `TenantProjectAuthority.cs` (eliminating the transitive dependency bug in authorization), and `ThreeNfAndSchemaNormalizationSql` includes backfill/repair SQL, but the redundant `OrganizationId` columns remain on `VaultBackup`, `VaultRestore`, `VaultSchedule`, and `GroundDatabase` for backward compatibility. |
| 11 | **CloudProject Decomposition** | `ProjectConnection`, `ProjectGitRepository`, `ProjectPublication`, `SchemaShareLink` | **IMPLEMENTED BUT NOT VERIFIED** | The 4 normalized 3NF entities (`Normalized3NfEntities.cs`), EF mappings in `StudioDbContext` and `HubDbContext`, and idempotent data migration SQL (`ThreeNfAndSchemaNormalizationSql.cs`) are implemented and unit-tested, but `ProjectsController` and `HubController` still read/write `CloudProject` directly. |
| 12 | **Derived StarCount and ForkCount** | `CloudProject.StarCount`, `CloudProject.ForkCount` + `ThreeNfAndSchemaNormalizationSql` `CHECK >= 0` | **NOT REQUIRED — WITH TECHNICAL JUSTIFICATION** | Materialized counters on `CloudProject` (guarded by `CK_projects_star_count_non_negative` and `CK_projects_fork_count_non_negative` and reconciled from `RepoStars` / `ForkedFromProjectId`) are justified for indexed sorting on the public Explore feed (`IX_CloudProjects_Explore_Public`: `WHERE "Visibility" = 2 ORDER BY "StarCount" DESC, "CreatedAt" DESC`), avoiding expensive `COUNT(*)` group-by joins on every public page load. |

---

## 7. PostgreSQL Migration & Schema Audit

### 7.1 `DbContext`, Schema, and History Table Configuration
All 11 module `DbContext`s configure isolated schemas via `modelBuilder.HasDefaultSchema(SchemaName)` and isolated migration history tables `(SchemaName, "__EFMigrationsHistory")` inside `ServiceCollectionExtensions.cs`:

| Module `DbContext` | `SchemaName` Constant | `HasDefaultSchema` Configured? | `__EFMigrationsHistory` Schema | Physical EF Migrations Present? | Physical `*ModelSnapshot.cs` Present? |
|---|---|---|---|---|---|
| `AuthDbContext` (Legacy) | `public` (default) | Mixed (`namines_vault`, `ground`, `flow`, `desk`, `public`) | `public.__EFMigrationsHistory` | **YES** (25 migrations in `Namines.Infrastructure/Migrations/`) | **YES** (`AuthDbContextModelSnapshot.cs`) |
| `IdentityDbContext` | `identity` | YES | `identity.__EFMigrationsHistory` | **NO** | **NO** |
| `StudioDbContext` | `studio` | YES | `studio.__EFMigrationsHistory` | **NO** | **NO** |
| `BillingDbContext` | `billing` | YES | `billing.__EFMigrationsHistory` | **NO** | **NO** |
| `GovernanceDbContext` | `governance` | YES | `governance.__EFMigrationsHistory` | **NO** | **NO** |
| `HubDbContext` | `hub` | YES | `hub.__EFMigrationsHistory` | **NO** | **NO** |
| `GatewayDbContext` | `gateway` | YES | `gateway.__EFMigrationsHistory` | **NO** | **NO** |
| `VaultDbContext` | `namines_vault` | YES | `namines_vault.__EFMigrationsHistory` | **NO** | **NO** |
| `GroundDbContext` | `ground` | YES | `ground.__EFMigrationsHistory` | **NO** | **NO** |
| `FlowDbContext` | `flow` | YES | `flow.__EFMigrationsHistory` | **NO** | **NO** |
| `DeskDbContext` | `desk` | YES | `desk.__EFMigrationsHistory` | **NO** | **NO** |
| `OpsDbContext` | `ops` | YES | `ops.__EFMigrationsHistory` | **NO** | **NO** |

### 7.2 `CloudProjects` vs `studio.projects` Coexistence
- Currently, `AuthDbContext` maps `CloudProject` to `public."CloudProjects"`.
- `StudioDbContext` (`StudioModule.cs:37`) maps `CloudProject` to `studio.projects` along with `studio.project_connections`, `studio.project_git_repositories`, and `studio.schema_share_links`.
- `ThreeNfAndSchemaNormalizationSql.BuildUpSql()` creates the `studio` and `hub` decomposed tables (`studio.project_connections`, `studio.project_git_repositories`, `studio.schema_share_links`, `hub.project_publications`) with foreign keys referencing `public."CloudProjects"("Id") ON DELETE CASCADE` so non-destructive coexistence works during transition.
- However, because `StudioDbContext` has no physical EF Core migrations yet, `studio.projects` is **not** created in production when `AuthDbContext.Database.Migrate()` runs at startup (`Program.cs:288`).

---

## 8. Transaction & Concurrency Audit

### 8.1 Inspection of `SharedTransactionManager`
File inspected: `backend/Namines.BuildingBlocks/Persistence/SharedTransactionManager.cs`.

- **Defect Found During Audit:**
  When `ExecuteInTransactionAsync` enlisted secondary `DbContext` instances (`for (var i = 1; i < contexts.Count; i++)`), it checked `if (!ReferenceEquals(ctxConnection, connection))` and called `ctx.Database.SetDbConnection(connection)` followed by `await ctx.Database.UseTransactionAsync(dbTransaction, ct)`. However, after `CommitAsync` or `RollbackAsync`, it **never detached the disposed transaction (`UseTransactionAsync(null)`) and never restored the secondary `DbContext`'s original `DbConnection`**. Any subsequent use of a secondary `DbContext` within the same HTTP request scope would either reference a disposed transaction or remain bound to the primary context's connection.
- **Fix Applied & Verified:**
  1. Recorded `originalConnections[i]` before mutating secondary `DbContext` connections.
  2. Added a `finally` block that iterates over all secondary `DbContext`s, calls `await ctx.Database.UseTransactionAsync(null, CancellationToken.None)`, and restores `ctx.Database.SetDbConnection(originalConnection)` when the connection was swapped, wrapped in a `try/catch` so cleanup never masks a primary transaction exception.
  3. Updated `SharedTransactionManager_Commits_And_RollsBack_Across_Multiple_DbContexts` in `ModularMonolithArchitectureTests.cs` to pass a third `DbContext` (`ctxC`) with a distinct `SqliteConnection` instance and assert `Assert.Same(separateConn, ctxC.Database.GetDbConnection())` after transaction completion.

---

## 9. Security Audit

All 8 security controls (`SEC-01` through `SEC-08`) and network/tenant boundaries were inspected directly in code:

| Control ID | Area | Implementation File & Lines | Verdict | Findings & Residual Risks |
|---|---|---|---|---|
| **SEC-01** | **Live Tenant / Org Authorization** | `backend/Namines.Infrastructure/Persistence/TenantProjectAuthority.cs:19-61` | **PASS** | `AuthorizeProjectAsync` queries `CloudProject` live by `ProjectId` and checks `UserId == actorUserId` or `OrganizationMembers.AnyAsync(OrganizationId == project.OrganizationId && UserId == actorUserId && Role >= minimumRole)`. Consumed by `VaultApplicationService`, `GroundApplicationService`, `FlowApplicationService`, and `DeskApplicationService`. |
| **SEC-02** | **Inbound Webhook HMAC-SHA256 + Replay Protection** | `backend/Namines.BuildingBlocks/Security/InboundWebhookSignatureVerifier.cs:17-69` & `AutomationController.cs:376-409` | **PASS** | Uses `CryptographicOperations.FixedTimeEquals` over `HMACSHA256("{timestamp}.{rawBody}")` with a configurable timestamp skew window (`±300s`). |
| **SEC-03** | **Outbound Webhook SSRF & DNS Rebinding Protection** | `backend/Namines.Infrastructure/Services/WebhookUrlValidator.cs` & `FlowApplicationService.cs:291-355` | **PASS** | Validates scheme (`http`/`https`), blocks loopback/RFC1918/link-local/CGNAT/cloud metadata (`169.254.169.254`), checks IPv4-mapped IPv6 (`IsIPv4MappedToIPv6` -> `MapToIPv4()`), blocks IPv6 unique local (`fc00::/7`) and site-local (`fec0::/10`), resolves DNS via `SocketsHttpHandler.ConnectCallback` at connection time (preventing DNS rebinding), and sets `AllowAutoRedirect = false` (preventing redirect-based SSRF). |
| **SEC-04** | **Database Connection Guard (SSRF)** | `backend/Namines.Infrastructure/Security/DatabaseConnectionGuard.cs:13-220` | **PASS** | Blocks private/reserved IPs in production unless `DatabaseSecurity:AllowPrivateNetworkTargets=true`, and blocks dangerous provider parameters (`allowLoadLocalInfile`, ` server program`, etc.). |
| **SEC-05** | **Vault Backup Encryption Key Enforcement** | `backend/Namines.API/Extensions/ServiceCollectionExtensions.cs:204-213` | **PASS** | Throws `InvalidOperationException("Vault:BackupEncryptionKey must be configured in production.")` when `IsProduction()` and key is missing/whitespace; uses ephemeral random 32-byte key only in non-production. |
| **SEC-06** | **Desk Handoff Token One-Time Consumption** | `backend/Namines.Desk/Application/DeskApplicationService.cs:110-169` | **PASS** | Stores only SHA-256 hash (`DeskHandoffTokenValidator.HashRawToken`), enforces short TTL (`60s`), and atomically marks `ConsumedAt = DateTime.UtcNow`. *Residual Note:* `AuthController.cs:346` still calls legacy `DeskHandoff.ConsumeAsync(_db, ...)` which uses the same SHA-256 + `ConsumedAt` semantics on `AuthDbContext`, but should be switched to `IDeskModule.Application.ConsumeHandoffTokenAsync`. |
| **SEC-07** | **Gateway API Key Hashing & Prefix** | `backend/Namines.API/Controllers/GatewayKeyController.cs` & `GatewayService.cs` | **PASS** | Keys are stored as SHA-256 hashes (`KeyHash`) with a display prefix (`KeyPrefix`), never plaintext. |
| **SEC-08** | **Semantic Cache Key Tenant/Engine Isolation** | `backend/Namines.Infrastructure/AI/SemanticCacheService.cs:155-169` & `ServiceCollectionExtensions.cs:273` | **FIXED & PASS** | Cache key `BuildKey` hashes `prompt + dbType + mode + existingTablesContext + customSystemPrompt` (`ai_semcache_v1_...`). During audit, `SemanticCacheService` was found registered as `AddScoped` instead of `AddSingleton` in `ServiceCollectionExtensions.cs:273`; fixed to `AddSingleton`. |

---

## 10. Test Execution Report

All test commands were executed from `c:\Users\enesy\OneDrive\Masaüstü\apps\namines` with `$env:DOTNET_ROLL_FORWARD="Major"`.

| Test Suite | Command Executed | Passed | Failed | Skipped | Duration |
|---|---|---|---|---|---|
| **Full Non-Docker Test Suite (`Namines.Tests`)** | `dotnet test backend/Namines.Tests/Namines.Tests.csproj --filter "FullyQualifiedName!~Namines.Tests.Integration" --nologo --no-build` | **2,589** | **0** | **0** | `35.1 s` |
| **Modular Monolith, Admin Hardening & Migration Snapshot Suite** | `dotnet test backend/Namines.Tests/Namines.Tests.csproj --filter "FullyQualifiedName~ModularMonolith\|FullyQualifiedName~AdminHardeningTests\|FullyQualifiedName~AdminAiRoutingAndFeatureFlagsTests\|FullyQualifiedName~MigrationModelSnapshotTests" --nologo --no-restore` | **61** | **0** | **0** | `3.1 s` |
| **PostgreSQL Container Migration Hygiene Suite (`MigrationHygieneTests`)** | `dotnet test backend/Namines.Tests/Namines.Tests.csproj --filter "FullyQualifiedName~MigrationHygieneTests" --nologo --no-build -- xunit.parallelizeAssembly=false xunit.parallelizeTestCollections=false` | **5** | **0** | **0** | `22.0 s` |

---

## 11. Defects Found and Fixed During This Audit

1. **`SharedTransactionManager` Secondary `DbContext` Connection & Transaction Leak (`backend/Namines.BuildingBlocks/Persistence/SharedTransactionManager.cs`):**
   - **Problem:** Secondary `DbContext` instances had `SetDbConnection(primaryConnection)` and `UseTransactionAsync(dbTransaction)` called without restoring their original `DbConnection` or detaching the transaction (`UseTransactionAsync(null)`) in `finally`.
   - **Fix:** Added `originalConnections` array tracking and a `finally` block that detaches the transaction (`UseTransactionAsync(null)`) and restores each secondary `DbContext`'s original `DbConnection` safely.
   - **Test Added:** Updated `SharedTransactionManager_Commits_And_RollsBack_Across_Multiple_DbContexts` in `backend/Namines.Tests/Architecture/ModularMonolithArchitectureTests.cs` with a 3rd `DbContext` (`ctxC`) holding a separate connection to verify restoration.
2. **`SemanticCacheService` Scoped DI Registration (`backend/Namines.API/Extensions/ServiceCollectionExtensions.cs:273`):**
   - **Problem:** `services.AddScoped<SemanticCacheService>()` recreated the semantic cache wrapper on every HTTP request instead of sharing the singleton service instance.
   - **Fix:** Changed registration to `services.AddSingleton<SemanticCacheService>()`.
3. **Module `DbContext` Missing `HasDefaultSchema(SchemaName)` (`GroundDbContext`, `VaultDbContext`, `FlowDbContext`, `DeskDbContext`):**
   - **Problem:** While individual entities called `.ToTable("...", SchemaName)`, `modelBuilder.HasDefaultSchema(SchemaName)` was not called on those 4 `DbContext`s, causing `model.GetDefaultSchema()` to return `null` instead of the module schema name.
   - **Fix:** Added `modelBuilder.HasDefaultSchema(SchemaName);` to `GroundDbContext.cs`, `VaultDbContext.cs`, `FlowDbContext.cs`, and `DeskDbContext.cs`.

---

## 12. Remaining Architectural Debt

1. **`AuthDbContext` Still Owns 37 `DbSet`s and 49 Consumers:**
   - Even for the 4 extracted modules (`Vault`, `Ground`, `Flow`, `Desk`), `AuthDbContext` still exposes their 10 `DbSet`s because `LaunchService`, `ProjectDeletionService`, `AuthController` (lines 330, 346), and `DataRetentionService` still use legacy paths.
2. **Legacy Duplicate Services in `Namines.Infrastructure/Services/`:**
   - `VaultService.cs`, `GroundService.cs`, `AutomationExecutor.cs`, `VaultBackupWorker.cs`, `VaultScheduleBackgroundService.cs`, `GroundPurgeBackgroundService.cs`, `AutomationExecutorWorker.cs`, and `AutomationRunLogRetentionService.cs` still exist in `Namines.Infrastructure/Services/`. While the duplicate workers are no longer registered in `ServiceCollectionExtensions.cs`, `VaultService`, `GroundService`, and `AutomationExecutor` are still registered and injected by `LaunchService` / `ProjectDeletionService`.
3. **7 Modules (`Identity`, `Studio`, `Billing`, `Governance`, `Hub`, `Gateway`, `Ops`) Are Facade Shells:**
   - They live in `Namines.BuildingBlocks/Modules/` and expose only `TDbContext DbContext { get; }` with zero extracted application services and zero controller consumers.
4. **No Physical Per-Module EF Core Migrations Yet:**
   - Only `AuthDbContext` has physical migrations in `Namines.Infrastructure/Migrations/`. `ModuleMigrationOrchestrator` is registered in DI, but `Program.cs` still runs `AuthDbContext.Database.Migrate()`.
5. **3NF Decomposed Tables Are Not Yet Active in Production Services:**
   - `ProjectConnection`, `ProjectGitRepository`, `ProjectPublication`, and `SchemaShareLink` exist as C# models and in `ThreeNfAndSchemaNormalizationSql`, but `ProjectsController` and `HubController` still read/write `CloudProject` columns directly.

---

## 13. Final Acceptance Decision

**`CONDITIONALLY ACCEPTED`**

### Rationale
- **Why not `REJECTED`?**
  - The shared kernel (`Namines.BuildingBlocks`), live tenant authority (`ITenantProjectAuthority`), cross-context transaction manager (`SharedTransactionManager`), and 4 domain modules (`Vault`, `Ground`, `Flow`, `Desk`) are genuinely implemented with layered architecture, isolated `DbContext`s, background workers, and real controller consumers (`VaultController`, `GroundController`, `AutomationController`, `AdminController`, `GatewayKeyController`, `SqlWorkbenchService`).
  - All 8 security controls (`SEC-01` through `SEC-08`) are implemented and verified.
  - All **2,589 unit/architecture/snapshot tests** pass with zero regressions.
- **Why not unconditionally `ACCEPTED`?**
  - Claiming full 11-module extraction, complete 3NF migration, and `AuthDbContext` retirement would be inaccurate while 7 modules remain facade shells, `AuthDbContext` retains 37 `DbSet`s, and `LaunchService` / `ProjectDeletionService` / `DataRetentionService` still touch `Vault`/`Ground`/`Flow`/`Desk` tables through `AuthDbContext`.

---

## 14. Exact Next Engineering Actions Required for Full Unconditional Acceptance

1. **Eliminate Dual-Context Access for `Vault`, `Ground`, `Flow`, and `Desk` (Highest Priority):**
   - Refactor `LaunchService.cs` and `ProjectDeletionService.cs` to inject `IVaultModule` and `IGroundModule` instead of legacy `VaultService` and `GroundService`.
   - Refactor `AuthController.cs` (`desk-handoff` endpoints) to use `IDeskModule.Application` instead of `DeskHandoff.cs`.
   - Refactor `DataRetentionService.cs` to call retention methods on `IVaultModule`, `IFlowModule`, and `IDeskModule`.
   - Delete legacy `VaultService.cs`, `GroundService.cs`, `AutomationExecutor.cs`, and unused legacy worker files from `Namines.Infrastructure/Services/`.
   - Remove the 10 `Vault`, `Ground`, `Flow`, and `Desk` `DbSet`s from `AuthDbContext` (reducing `AuthDbContext` from 37 to 27 `DbSet`s immediately).
2. **Generate Physical EF Core Initial Migrations for `VaultDbContext`, `GroundDbContext`, `FlowDbContext`, and `DeskDbContext`:**
   - Wire `ModuleMigrationOrchestrator` into `Program.cs` startup alongside `AuthDbContext` migration.
3. **Wire 3NF `CloudProject` Decomposition (`ThreeNfAndSchemaNormalizationSql`) into an EF Core Migration and Refactor `ProjectsController` / `HubController`:**
   - Dual-write/read `ProjectConnection`, `ProjectGitRepository`, `ProjectPublication`, and `SchemaShareLink` before deprecating the legacy scalar columns on `CloudProject`.
4. **Incrementally Extract Application Services for the Remaining 7 Modules (`Ops`, `Gateway`, `Billing`, `Governance`, `Hub`, `Studio`, `Identity`):**
   - Replace `TDbContext DbContext { get; }` facade shells with real use-case interfaces and retire `AuthDbContext` module by module.
