# Launch Integration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A single `POST /api/launch` orchestration that turns an approved
`/compile` schema into a running Desk panel (and, on request, a
Gateway-authenticated downloadable project) in one click — wiring Ground
(provision), the existing DDL executor (apply), Vault (first backup), and
Desk (deep-linked handoff) together for the first time.

**Architecture:** A new `LaunchService` (plain class, same shape as the
existing `GroundService`/`VaultService`) orchestrates three already-working
services in sequence: `GroundService.ProvisionAsync` → `IDbIntrospectionService.IntrospectAsync`
(decides empty vs. non-empty) → `IDatabaseExecutor.ExecuteScriptAsync` (only
if empty) → `VaultService.BackupAsync`. A thin `LaunchController` exposes it
over HTTP and mints a Desk handoff token in the same request. A second,
independent endpoint packages a downloadable project via the existing
`IScaffolderService`, post-processing the zip to add one new file instead of
touching any of the five per-language generators. On the frontend, one new
panel component drives the whole flow from `/compile`.

**Tech Stack:** .NET 8 (xUnit + Testcontainers.PostgreSql, no mocking
library — this codebase tests exclusively against real dependencies, see
`Namines.Tests/Integration/ChangeRequestIntegrationTests.cs`), Next.js 16 /
React 19 / Zustand (frontend), vitest.

**Spec:** `docs/superpowers/specs/2026-09-13-launch-integration-design.md`

## Global Constraints

- Launch only ever targets **PostgreSQL** — Ground's only registered
  providers (LocalPostgres/Neon/Supabase) are all Postgres-family. Always
  compile DDL for `DatabaseType.PostgreSQL` / `"PostgreSQL"` regardless of
  the `/compile` DDL tab's currently selected engine dropdown.
- The raw database connection string must **never** reach a client or a
  downloaded file. Only a revocable Gateway API key may be embedded in the
  downloadable project.
- The "already has tables" branch must **never** run DDL — it hands off to
  the existing `changeRequestService.createQuick` / `/review/{id}` flow and
  stops. No new versioning/diff logic is written for this plan.
- Every step's failure must be visible, never silently swallowed (matches
  Vault's own stated principle) — see the per-task error handling below.
- No new test-only mocking library is introduced. All new backend tests use
  real dependencies (a `Testcontainers.PostgreSql` container), matching
  every existing integration test in `Namines.Tests/Integration/`.

---

## File Structure

| File | Responsibility |
|---|---|
| `backend/Namines.API/Controllers/Shared/ConnectionTargetDescriber.cs` (new) | Extracted, shared `(host, database)` parser — was private/duplicated logic in `DatabaseExecutorController` |
| `backend/Namines.Infrastructure/Services/LaunchService.cs` (new) | All orchestration logic; the only class with real branching to test |
| `backend/Namines.API/Controllers/LaunchController.cs` (new) | Thin HTTP wrapper: `POST /api/launch`, `POST /api/launch/{projectId}/download` |
| `backend/Namines.Tests/Services/LaunchServiceTests.cs` (new) | Real-Postgres integration tests for both branches |
| `frontend/services/launchApi.ts` (new) | Client wrapper — mirrors `vaultGroundApi.ts`'s style |
| `frontend/components/compile/LaunchPanel.tsx` (new) | The button, provider picker, step checklist, Ready/NeedsReview states |
| `frontend/components/compile/LaunchPanel.test.tsx` (new) | vitest — step-transition behavior |
| `frontend/app/compile/page.tsx` (modify) | Add "Launch" entry to the sidebar, render `LaunchPanel` |
| `services/desk/app/handoff/route.ts` (modify) | Accept optional `projectId`/`view` form fields, seed `sessionStorage` before redirect |

---

### Task 1: Extract the shared connection-target describer

> ✅ **Bitmiştir.** Yazıldı — ama `backend/Namines.Core/ConnectionTargetDescriber.cs` olarak (plandaki `API/Controllers/Shared/` yerine), çünkü `LaunchService` de kullanıyor. `ConnectionTargetDescriberTests.cs` kapsıyor.

**Files:**
- Create: `backend/Namines.API/Controllers/Shared/ConnectionTargetDescriber.cs`
- Modify: `backend/Namines.API/Controllers/DatabaseExecutorController.cs:169-` (the existing `private static (string?, string?) DescribeTarget(...)` method)
- Test: `backend/Namines.Tests/Controllers/ConnectionTargetDescriberTests.cs`

**Interfaces:**
- Produces: `Namines.API.Controllers.Shared.ConnectionTargetDescriber.Describe(string? connectionString, DatabaseType dbType) -> (string? Host, string? Database)` — a `public static` method with the exact body currently in `DatabaseExecutorController.DescribeTarget`.

This is a pure move (no behavior change) so `LaunchController` can write the
same `SqlExecutionAudit` shape as `DatabaseExecutorController` without
duplicating multi-engine connection-string parsing.

- [ ] **Step 1: Read the existing method to copy verbatim**

Run: `sed -n '169,220p' backend/Namines.API/Controllers/DatabaseExecutorController.cs`

Copy its full body (it parses `Npgsql`/`SqlConnectionStringBuilder`/`MySqlConnectionStringBuilder`/Oracle-style connection strings — do not paraphrase it, move it exactly as written).

- [ ] **Step 2: Create the shared static class**

```csharp
// backend/Namines.API/Controllers/Shared/ConnectionTargetDescriber.cs
using Namines.Core.Enums;

namespace Namines.API.Controllers.Shared;

/// <summary>
/// Bağlantı dizesinden yalnızca host + veritabanı adını çıkarır (parola/kullanıcı
/// adı ASLA). Denetim kayıtlarının (<see cref="Namines.Core.Models.Auth.SqlExecutionAudit"/>)
/// "nerede çalıştı" sorusuna cevap vermesi için — <see cref="DatabaseExecutorController"/>
/// ve <see cref="LaunchController"/> arasında paylaşılıyor, ikinci bir kopya
/// çıkması aradaki bir motoru (ör. Oracle) yalnızca birinde güncel tutma riski
/// taşırdı.
/// </summary>
internal static class ConnectionTargetDescriber
{
    public static (string? Host, string? Database) Describe(string? connectionString, DatabaseType dbType)
    {
        // <-- paste the exact body from DatabaseExecutorController.DescribeTarget here -->
    }
}
```

- [ ] **Step 3: Point `DatabaseExecutorController` at the shared class**

In `backend/Namines.API/Controllers/DatabaseExecutorController.cs`:
- Delete the `private static (string? Host, string? Database) DescribeTarget(...)` method body entirely.
- Add `using Namines.API.Controllers.Shared;` to the top of the file.
- Replace the call site `DescribeTarget(request.ConnectionString, request.DbType)` with `ConnectionTargetDescriber.Describe(request.ConnectionString, request.DbType)`.

- [ ] **Step 4: Write a test proving the move didn't change behavior**

```csharp
// backend/Namines.Tests/Controllers/ConnectionTargetDescriberTests.cs
using Namines.API.Controllers.Shared;
using Namines.Core.Enums;
using Xunit;

namespace Namines.Tests.Controllers;

public class ConnectionTargetDescriberTests
{
    [Fact]
    public void Extracts_host_and_database_from_postgres_connection_string()
    {
        var (host, database) = ConnectionTargetDescriber.Describe(
            "Host=db.internal;Port=5432;Database=namines_control;Username=namines;Password=secret",
            DatabaseType.PostgreSQL);

        Assert.Equal("db.internal", host);
        Assert.Equal("namines_control", database);
    }

    [Fact]
    public void Never_returns_the_password()
    {
        var (host, database) = ConnectionTargetDescriber.Describe(
            "Host=db.internal;Port=5432;Database=namines_control;Username=namines;Password=super-secret-value",
            DatabaseType.PostgreSQL);

        Assert.DoesNotContain("super-secret-value", host ?? "");
        Assert.DoesNotContain("super-secret-value", database ?? "");
    }

    [Fact]
    public void Returns_nulls_for_a_null_connection_string()
    {
        var (host, database) = ConnectionTargetDescriber.Describe(null, DatabaseType.PostgreSQL);
        Assert.Null(host);
        Assert.Null(database);
    }
}
```

- [ ] **Step 5: Run the new test**

Run: `dotnet test backend/Namines.Tests/Namines.Tests.csproj --filter ConnectionTargetDescriberTests`
Expected: 3 passed.

- [ ] **Step 6: Run the full existing DatabaseExecutorController-adjacent tests to confirm no regression**

Run: `dotnet test backend/Namines.Tests/Namines.Tests.csproj --filter FullyQualifiedName~DatabaseExecutor`
Expected: PASS (same count as before the move — this is a pure refactor).

- [ ] **Step 7: Commit**

```bash
git add backend/Namines.API/Controllers/Shared/ConnectionTargetDescriber.cs backend/Namines.API/Controllers/DatabaseExecutorController.cs backend/Namines.Tests/Controllers/ConnectionTargetDescriberTests.cs
git commit -m "refactor: extract ConnectionTargetDescriber for reuse by LaunchController

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

---

### Task 2: `LaunchService` — the empty-target path (provision → introspect → apply → backup)

> ✅ **Bitmiştir.** `Infrastructure/Services/LaunchService.cs` yazıldı; testleri gerçek Docker gerektirdiği için `Namines.Tests.RunTests/LaunchServiceTests.cs` altında.

**Files:**
- Create: `backend/Namines.Infrastructure/Services/LaunchService.cs`
- Test: `backend/Namines.Tests/Services/LaunchServiceTests.cs`

**Interfaces:**
- Consumes:
  - `GroundService.FindProvider(string name) -> IDatabaseProvider?`
  - `GroundService.ProvisionAsync(CloudProject project, string userId, IDatabaseProvider provider, CancellationToken ct) -> Task<GroundResult>` where `GroundResult(bool Ok, string? Error, GroundDatabase? Database)`
  - `IDbIntrospectionService.IntrospectAsync(string connectionString, string dbType, CancellationToken ct) -> Task<DatabaseSchema>` (`DatabaseSchema.Tables` is a `List<SchemaTable>`)
  - `IDatabaseExecutor.ExecuteScriptAsync(string connectionString, string ddlScript, DatabaseType dbType, CancellationToken ct) -> Task<ExecutionResult>` where `ExecutionResult(bool Success, string? ErrorMessage, int StatementsExecuted, bool PartialApplyPossible)`
  - `VaultService.BackupAsync(CloudProject project, string userId, VaultBackupKind kind, CancellationToken ct) -> Task<VaultResult>` where `VaultResult(bool Ok, string? Error, string? BackupId)`
  - `IConnectionSecretProtector.Unprotect(string ciphertext) -> string`
- Produces (for Task 3's controller and Task 4's tests):
  - `public sealed record LaunchResult(LaunchStatus Status, string? Error = null, bool DdlApplied = false, string? BackupWarning = null)`
  - `public enum LaunchStatus { Ready, NeedsReview, ProvisionFailed, DdlFailed }`
  - `public Task<LaunchResult> LaunchAsync(CloudProject project, string userId, string providerName, string ddlScript, CancellationToken ct)`

```csharp
// backend/Namines.Infrastructure/Services/LaunchService.cs
using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Namines.Core.Enums;
using Namines.Core.Interfaces;
using Namines.Core.Models;
using Namines.Core.Models.Auth;
using Namines.Core.Security;

namespace Namines.Infrastructure.Services;

public enum LaunchStatus { Ready, NeedsReview, ProvisionFailed, DdlFailed }

/// <param name="DdlApplied">Testler ve arayüz için: DDL gerçekten çalıştı mı,
/// yoksa hedefte zaten tablo olduğu için mi atlandı.</param>
/// <param name="BackupWarning">İlk Vault yedeği başarısız olduysa BURADA taşınır
/// — Ready durumunu ENGELLEMEZ (panel yine kullanılabilir), ama sessizce
/// yutulmaz: arayüz bunu görünür bir uyarı olarak göstermek ZORUNDA.</param>
public sealed record LaunchResult(
    LaunchStatus Status,
    string? Error = null,
    bool DdlApplied = false,
    string? BackupWarning = null);

/// <summary>
/// "/compile"'da onaylanmış bir şemayı saniyeler içinde çalışan bir Desk
/// paneline dönüştüren omurga. Ground (provizyon), var olan DDL yürütücü
/// (uygula) ve Vault'u (ilk yedek) TEK bir sırayla birbirine bağlar —
/// üçü de zaten ayrı ayrı canlı kanıtlanmış, burada yeni olan yalnızca sıra.
///
/// <b>"İlk kurulum mu / güncelleme mi" bir istemci bayrağı DEĞİL</b> — hedefin
/// canlı introspection'ı karar veriyor (adım 3). Böylece Desk'ten ya da elle
/// tablo eklenmiş bir projede bile doğru dala düşer.
///
/// <b>Hedef doluysa DDL'e hiç dokunulmaz.</b> Var olan Change Review akışına
/// (ChangeRequestController.CreateQuick) devretmek çağıranın işi — bu servis
/// yalnızca <see cref="LaunchStatus.NeedsReview"/> döner, hiçbir şey yazmaz.
/// </summary>
public class LaunchService
{
    private readonly GroundService _ground;
    private readonly IDbIntrospectionService _introspection;
    private readonly IDatabaseExecutor _executor;
    private readonly VaultService _vault;
    private readonly IConnectionSecretProtector _protector;
    private readonly ILogger<LaunchService> _logger;

    public LaunchService(
        GroundService ground,
        IDbIntrospectionService introspection,
        IDatabaseExecutor executor,
        VaultService vault,
        IConnectionSecretProtector protector,
        ILogger<LaunchService> logger)
    {
        _ground = ground;
        _introspection = introspection;
        _executor = executor;
        _vault = vault;
        _protector = protector;
        _logger = logger;
    }

    public async Task<LaunchResult> LaunchAsync(
        CloudProject project, string userId, string providerName, string ddlScript, CancellationToken ct)
    {
        var provider = _ground.FindProvider(providerName);
        if (provider is null)
            return new LaunchResult(LaunchStatus.ProvisionFailed, Error: $"No such provider: '{providerName}'.");

        if (await provider.ProbeAsync(ct) is { } probeProblem)
            return new LaunchResult(LaunchStatus.ProvisionFailed, Error: probeProblem);

        var provisionResult = await _ground.ProvisionAsync(project, userId, provider, ct);
        if (!provisionResult.Ok)
            return new LaunchResult(LaunchStatus.ProvisionFailed, Error: provisionResult.Error);

        // ProvisionAsync, CloudProject.EncryptedConnectionString'i BAŞARILI olduğunda
        // günceller (bkz. GroundService.ProvisionAsync) — 'project' aynı EF izlenen
        // örnek olduğu için burada zaten güncel.
        if (string.IsNullOrWhiteSpace(project.EncryptedConnectionString))
            return new LaunchResult(LaunchStatus.ProvisionFailed,
                Error: "Provisioning reported success but left no connection string.");

        var connectionString = _protector.Unprotect(project.EncryptedConnectionString);

        DatabaseSchema liveSchema;
        try
        {
            liveSchema = await _introspection.IntrospectAsync(connectionString, "PostgreSQL", ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Launch: {ProjectId} icin hedef okunamadi.", project.Id);
            return new LaunchResult(LaunchStatus.ProvisionFailed,
                Error: $"The new database could not be read back: {ex.Message}");
        }

        if (liveSchema.Tables.Count > 0)
        {
            // Hedefte zaten veri var — DDL'e HİÇ DOKUNMUYORUZ. Çağıran (LaunchController)
            // bunu Change Review akışına devreder.
            return new LaunchResult(LaunchStatus.NeedsReview);
        }

        var execution = await _executor.ExecuteScriptAsync(connectionString, ddlScript, DatabaseType.PostgreSQL, ct);
        if (!execution.Success)
        {
            _logger.LogError(
                "Launch: {ProjectId} icin DDL basarisiz ({Statements} ifade calisti, kismi uygulama {Partial}): {Error}",
                project.Id, execution.StatementsExecuted, execution.PartialApplyPossible, execution.ErrorMessage);
            return new LaunchResult(LaunchStatus.DdlFailed, Error: execution.ErrorMessage);
        }

        string? backupWarning = null;
        try
        {
            var backup = await _vault.BackupAsync(project, userId, VaultBackupKind.Manual, ct);
            if (!backup.Ok)
                backupWarning = $"Initial backup failed: {backup.Error}. Back it up manually from the Vault tab.";
        }
        catch (Exception ex)
        {
            // İlk yedek Ready'yi ENGELLEMİYOR — ama sessizce yutulmuyor da.
            _logger.LogError(ex, "Launch: {ProjectId} icin ilk yedek basarisiz.", project.Id);
            backupWarning = "Initial backup failed unexpectedly. Back it up manually from the Vault tab.";
        }

        return new LaunchResult(LaunchStatus.Ready, DdlApplied: true, BackupWarning: backupWarning);
    }
}
```

- [ ] **Step 1: Write the failing tests first (TDD — create the file below, then Task 2's implementation makes it pass)**

```csharp
// backend/Namines.Tests/Services/LaunchServiceTests.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Namines.Core.Models.Auth;
using Namines.Core.Security;
using Namines.Ground.Providers;
using Namines.Infrastructure.Data;
using Namines.Infrastructure.Security;
using Namines.Infrastructure.Services;
using Namines.Vault.Abstractions;
using Namines.Vault.Providers;
using Namines.Vault.Storage;
using Testcontainers.PostgreSql;
using Xunit;

namespace Namines.Tests.Services;

/// <summary>
/// LaunchService'in iki dalını GERÇEK bir PostgreSQL'e karşı kanıtlar: bir
/// konteynerin admin bağlantısı hem AuthDbContext'in kontrol veritabanı hem
/// de LocalPostgresProvider'ın "kendi sunucumuz" hedefi olarak kullanılıyor —
/// tıpkı gerçek geliştirme ortamındaki gibi (bkz. docker-compose.yml,
/// namines_control + namines_db_* aynı sunucuda).
/// </summary>
[Collection("Docker")]
public class LaunchServiceTests : IAsyncLifetime
{
    // DbHostAccessPolicyTests.FakeEnv ile aynı desen — o sınıf private olduğu
    // için burada AYNI 5 satır tekrarlanıyor, paylaşılan bir test double
    // çıkarmak bu tek kullanım için gereksiz bir soyutlama olurdu.
    private sealed class FakeEnv : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "Tests";
        public string ContentRootPath { get; set; } = "";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }

    private readonly PostgreSqlContainer _container =
        new PostgreSqlBuilder("postgres:17-alpine").Build();

    private AuthDbContext? _context;
    private LaunchService? _launch;
    private string _userId = null!;
    private IConfiguration _config = null!;

    public async Task InitializeAsync()
    {
        if (!DockerAvailable.Value) return;

        await _container.StartAsync();

        var options = new DbContextOptionsBuilder<AuthDbContext>()
            .UseNpgsql(_container.GetConnectionString())
            .Options;
        _context = new AuthDbContext(options);
        await _context.Database.MigrateAsync();

        _userId = Guid.NewGuid().ToString();
        await _context.Users.AddAsync(new ApplicationUser { Id = _userId, UserName = "launch-test" });
        await _context.SaveChangesAsync();

        _config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Ground:LocalPostgres:AdminConnectionString"] = _container.GetConnectionString(),
            ["Security:ConnectionEncryptionKey"] = "test-launch-service-key-at-least-32-chars",
            ["Security:AllowPrivateDbHosts"] = "true",
            ["Vault:BackupEncryptionKey"] = "test-launch-vault-key-at-least-32-chars",
            ["Vault:StoragePath"] = Path.Combine(Path.GetTempPath(), "namines-launch-tests-" + Guid.NewGuid()),
        }).Build();

        var protector = new AesGcmConnectionSecretProtector(_config);
        var hostPolicy = new DbHostAccessPolicy(
            new FakeEnv(), _config, NullLogger<DbHostAccessPolicy>.Instance);

        var ground = new GroundService(
            _context, protector,
            new[] { new LocalPostgresProvider(_config, NullLogger<LocalPostgresProvider>.Instance) },
            NullLogger<GroundService>.Instance);

        var vault = new VaultService(
            _context, protector, hostPolicy,
            new IBackupProvider[] { new PostgresBackupProvider(_config, NullLogger<PostgresBackupProvider>.Instance) },
            new FileSystemBackupStore(_config),
            new BackupCipher(_config),
            NullLogger<VaultService>.Instance);

        _launch = new LaunchService(
            ground,
            new DbIntrospectionService(NullLogger<DbIntrospectionService>.Instance, hostPolicy),
            new DatabaseExecutorService(hostPolicy),
            vault,
            protector,
            NullLogger<LaunchService>.Instance);
    }

    public async Task DisposeAsync()
    {
        if (!DockerAvailable.Value) return;
        if (_context is not null) await _context.DisposeAsync();
        await _container.DisposeAsync();
    }

    private async Task<CloudProject> NewBareProjectAsync()
    {
        var project = new CloudProject
        {
            Id = Guid.NewGuid().ToString(),
            Name = "Launch Test " + Guid.NewGuid().ToString("N")[..8],
            DbType = "PostgreSQL",
            SchemaJson = "{}",
            NodePositionsJson = "{}",
            UserId = _userId,
        };
        await _context!.CloudProjects.AddAsync(project);
        await _context.SaveChangesAsync();
        return project;
    }

    private const string OneTableDdl = """
        CREATE TABLE widgets (
            id SERIAL PRIMARY KEY,
            name TEXT NOT NULL
        );
        """;

    [RequiresDockerFact]
    public async Task Empty_target_gets_provisioned_and_ddl_applied_and_backed_up()
    {
        var project = await NewBareProjectAsync();

        var result = await _launch!.LaunchAsync(project, _userId, "LocalPostgres", OneTableDdl, default);

        Assert.Equal(LaunchStatus.Ready, result.Status);
        Assert.True(result.DdlApplied);
        Assert.Null(result.BackupWarning);

        // Bağımsız doğrulama: LaunchService'in ördüğü aynı EF izleme hattından
        // DEĞİL, taze bir DbContext'le tekrar okumak.
        var reloadOptions = new DbContextOptionsBuilder<AuthDbContext>()
            .UseNpgsql(_container.GetConnectionString()).Options;
        await using var reloaded = new AuthDbContext(reloadOptions);
        var refreshed = await reloaded.CloudProjects.AsNoTracking()
            .FirstAsync(p => p.Id == project.Id);
        Assert.NotNull(refreshed.EncryptedConnectionString);

        var backupCount = await reloaded.VaultBackups.CountAsync(b => b.ProjectId == project.Id);
        Assert.Equal(1, backupCount);
    }

    [RequiresDockerFact]
    public async Task Target_with_existing_tables_needs_review_and_never_touches_ddl()
    {
        var project = await NewBareProjectAsync();

        // Önce sıfırdan bir kez çalıştır (hedefi "dolu" hale getirmek için) —
        // ikinci çağrının davranışını test ediyoruz, birincisini değil.
        var first = await _launch!.LaunchAsync(project, _userId, "LocalPostgres", OneTableDdl, default);
        Assert.Equal(LaunchStatus.Ready, first.Status);

        const string secondDdl = """
            CREATE TABLE gadgets (
                id SERIAL PRIMARY KEY
            );
            """;
        var second = await _launch!.LaunchAsync(project, _userId, "LocalPostgres", secondDdl, default);

        Assert.Equal(LaunchStatus.NeedsReview, second.Status);
        Assert.False(second.DdlApplied);

        // "gadgets" ASLA oluşturulmamalı — DDL'e hiç dokunulmadığının kanıtı.
        var reloadOptions = new DbContextOptionsBuilder<AuthDbContext>()
            .UseNpgsql(_container.GetConnectionString()).Options;
        await using var reloaded = new AuthDbContext(reloadOptions);
        var refreshed = await reloaded.CloudProjects.AsNoTracking().FirstAsync(p => p.Id == project.Id);
        var connectionString = new AesGcmConnectionSecretProtector(_config)
            .Unprotect(refreshed.EncryptedConnectionString!);

        await using var direct = new Npgsql.NpgsqlConnection(connectionString);
        await direct.OpenAsync();
        await using var cmd = new Npgsql.NpgsqlCommand(
            "SELECT 1 FROM information_schema.tables WHERE table_name = 'gadgets'", direct);
        Assert.Null(await cmd.ExecuteScalarAsync());
    }
}
```

- [ ] **Step 2: Run it to confirm it fails (LaunchService doesn't exist yet)**

Run: `dotnet test backend/Namines.Tests/Namines.Tests.csproj --filter LaunchServiceTests`
Expected: build error — `LaunchService`/`LaunchStatus`/`LaunchResult` not found.

- [ ] **Step 3: Create `backend/Namines.Infrastructure/Services/LaunchService.cs`**

Use the full `LaunchService` code block shown above under **Interfaces**.

- [ ] **Step 4: Build and run the tests**

Run: `dotnet build backend/Namines.sln`
Expected: 0 errors (fix any constructor-argument mismatches against the real
`GroundService`/`VaultService`/`DatabaseExecutorService`/`DbIntrospectionService`
constructors you find while wiring — those are the source of truth, not this
plan, if a signature has drifted since this plan was written).

Run: `dotnet test backend/Namines.Tests/Namines.Tests.csproj --filter LaunchServiceTests`
Expected: 2 passed if Docker is running, 2 skipped (not failed) otherwise.

- [ ] **Step 5: Commit**

```bash
git add backend/Namines.Infrastructure/Services/LaunchService.cs backend/Namines.Tests/Services/LaunchServiceTests.cs
git commit -m "feat: add LaunchService — provision, introspect, apply DDL, first backup

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

---

### Task 3: `LaunchController` — `POST /api/launch`

> ✅ **Bitmiştir.** `LaunchController` `POST /api/launch` ile canlı; servis DI'ye kayıtlı.

**Files:**
- Create: `backend/Namines.API/Controllers/LaunchController.cs`
- Modify: `backend/Namines.API/Extensions/ServiceCollectionExtensions.cs` (register `LaunchService`)

**Interfaces:**
- Consumes: `LaunchService.LaunchAsync(...)` from Task 2; `OrgAccess.GetRoleAsync(this AuthDbContext, string projectId, string userId, CancellationToken ct) -> Task<OrgRole?>` (existing, `Namines.Infrastructure.Data`); `AuthDbContext.CreateDeskHandoffTokenAsync(this AuthDbContext, string userId, CancellationToken ct) -> Task<string>` (existing, `Namines.Infrastructure.Data.DeskHandoff`); `schemaService`-equivalent DDL is passed in by the frontend, not generated server-side.
- Produces: `POST /api/launch` request body `{ projectId: string, provider: string, ddlScript: string }`; response shapes documented in Step 2 below — this is what Task 9's frontend panel consumes.

- [ ] **Step 1: Register `LaunchService` in DI**

In `backend/Namines.API/Extensions/ServiceCollectionExtensions.cs`, find the
line `services.AddScoped<IDatabaseExecutor, DatabaseExecutorService>();`
(around line 98) and add directly below it:

```csharp
        services.AddScoped<Namines.Infrastructure.Services.LaunchService>();
```

(`GroundService` and `VaultService` are already registered elsewhere in this
same file — confirm with `grep -n "AddScoped<GroundService>\|AddScoped<VaultService>" backend/Namines.API/Extensions/ServiceCollectionExtensions.cs`
before adding a duplicate registration.)

- [ ] **Step 2: Create the controller**

```csharp
// backend/Namines.API/Controllers/LaunchController.cs
using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Namines.Core.Models.Auth;
using Namines.Infrastructure.Data;
using Namines.Infrastructure.Services;

namespace Namines.API.Controllers;

public sealed record LaunchRequest(string ProjectId, string Provider, string DdlScript);

/// <summary>
/// "/compile"'da onaylanmış bir şemayı tek istekte çalışan bir Desk paneline
/// dönüştüren uç. Orkestrasyonun TAMAMI <see cref="LaunchService"/>'te —
/// burası yalnızca yetki kontrolü, HTTP şekli ve Desk handoff jetonu.
///
/// <b>Owner-only:</b> Ground provizyonu (GroundController) ve SQL konsolu
/// (SqlConsole) ile aynı eşik — bu uç de veritabanı düzeyinde gerçek bir
/// kaynak açıp DDL çalıştırıyor.
/// </summary>
[Authorize]
[ApiController]
[Route("api/launch")]
public class LaunchController : ControllerBase
{
    private readonly AuthDbContext _context;
    private readonly LaunchService _launch;

    public LaunchController(AuthDbContext context, LaunchService launch)
    {
        _context = context;
        _launch = launch;
    }

    private string? CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier);

    [HttpPost]
    public async Task<IActionResult> Launch([FromBody] LaunchRequest request, CancellationToken ct)
    {
        var userId = CurrentUserId;
        if (userId is null) return Unauthorized();

        if (string.IsNullOrWhiteSpace(request.ProjectId)
            || string.IsNullOrWhiteSpace(request.Provider)
            || string.IsNullOrWhiteSpace(request.DdlScript))
            return BadRequest(new { error = "projectId, provider and ddlScript are required." });

        if (await _context.GetRoleAsync(request.ProjectId, userId, ct) != OrgRole.Owner)
            return Forbid();

        var project = await _context.CloudProjects.FirstOrDefaultAsync(p => p.Id == request.ProjectId, ct);
        if (project is null) return NotFound(new { error = "Project not found." });

        var result = await _launch.LaunchAsync(project, userId, request.Provider, request.DdlScript, ct);

        switch (result.Status)
        {
            case LaunchStatus.ProvisionFailed:
                return BadRequest(new { status = "ProvisionFailed", error = result.Error });

            case LaunchStatus.DdlFailed:
                return BadRequest(new { status = "DdlFailed", error = result.Error });

            case LaunchStatus.NeedsReview:
                return Ok(new { status = "NeedsReview" });

            case LaunchStatus.Ready:
                // Desk handoff jetonu BURADA, aynı istekte üretiliyor: kullanıcı
                // zaten Owner olarak doğrulandı, ikinci bir tıklama/istek gerekmiyor.
                var deskHandoffToken = await _context.CreateDeskHandoffTokenAsync(userId, ct);
                return Ok(new
                {
                    status = "Ready",
                    projectId = project.Id,
                    deskHandoffToken,
                    backupWarning = result.BackupWarning,
                });

            default:
                throw new InvalidOperationException($"Unhandled LaunchStatus: {result.Status}");
        }
    }
}
```

- [ ] **Step 3: Build**

Run: `dotnet build backend/Namines.sln`
Expected: 0 errors.

- [ ] **Step 4: Commit**

```bash
git add backend/Namines.API/Controllers/LaunchController.cs backend/Namines.API/Extensions/ServiceCollectionExtensions.cs
git commit -m "feat: add POST /api/launch — Owner-gated HTTP entry point for LaunchService

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

---

### Task 4: Desk handoff route — deep-link to a specific project + view

> ✅ **Bitmiştir.** `services/desk/app/handoff/route.ts` proje ve görünüm derin bağlantısını taşıyor.

**Files:**
- Modify: `services/desk/app/handoff/route.ts`

**Interfaces:**
- Consumes: nothing new from earlier tasks — this task only widens the existing form-field contract.
- Produces: the `POST` handler now accepts two additional, **optional**
  `FormData` fields, `projectId` and `view`, read exactly like the existing
  `token` field is read today.

- [ ] **Step 1: Read the two fields alongside `token`**

In `services/desk/app/handoff/route.ts`, right after the existing block:

```ts
  const form = await req.formData();
  const token = form.get('token');
```

add:

```ts
  const projectId = form.get('projectId');
  const view = form.get('view');
```

- [ ] **Step 2: Seed `sessionStorage` before the redirect, only if both are present**

Find the existing success response (the one containing
`sessionStorage.setItem('namines-desk-token', ...)` and `location.replace('/')`).
Replace that `<script>` block with:

```ts
  const deepLinkScript = (typeof projectId === 'string' && projectId && typeof view === 'string' && view)
    ? `try {
         sessionStorage.setItem('namines-desk-project', ${JSON.stringify(projectId)});
         sessionStorage.setItem('namines-desk-view', ${JSON.stringify(view)});
       } catch (e) {}`
    : '';

  return htmlResponse(`<!doctype html>
<html><head><meta charset="utf-8"><title>Namines Desk</title></head>
<body style="background:#0b0b0b;color:#eceff1;font-family:ui-sans-serif,system-ui,sans-serif;display:flex;align-items:center;justify-content:center;min-height:100vh;margin:0">
<p>Giriş yapılıyor…</p>
<script>
  try { sessionStorage.setItem('namines-desk-token', ${JSON.stringify(jwt)}); } catch (e) {}
  ${deepLinkScript}
  location.replace('/');
</script>
</body></html>`, 200);
```

This is additive only — a POST with no `projectId`/`view` fields (today's
`openNaminesDesk()` call in `/compile`) behaves byte-for-byte as before,
because `deepLinkScript` is the empty string in that case.

- [ ] **Step 3: Manually verify the existing (non-deep-link) handoff still works**

Run: `npm --prefix services/desk run dev` (or use the already-running
preview server if one is up) and, from the main app's `/compile` page,
click the existing "Namines Desk" button. Confirm it still lands on Desk's
project overview exactly as before — this proves the additive change didn't
break the button that's been working all along.

- [ ] **Step 4: Commit**

```bash
git add services/desk/app/handoff/route.ts
git commit -m "feat(desk): accept optional projectId/view in handoff for deep-linking

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

---

### Task 5: Download endpoint — Gateway-keyed project zip

> ✅ **Bitmiştir.** `POST /api/launch/{projectId}/download` yazıldı; `LaunchDownloadZipTests.cs` kapsıyor.

**Files:**
- Create: `backend/Namines.API/Controllers/LaunchController.cs:` (add a second action to the same controller from Task 3 — same responsibility: "turn an approved schema into something usable")
- Test: `backend/Namines.Tests/Controllers/LaunchDownloadZipTests.cs`

**Interfaces:**
- Consumes: `IScaffolderService.GenerateFullStackProjectAsync(DatabaseSchema schema) -> Task<byte[]>` (existing, unmodified); `GatewayAccess.CreateKey(string projectId, string name, string createdByUserId, bool canWrite, DateTime? expiresAt) -> (GatewayApiKey Entity, string RawKey)` (existing, `Namines.Core.Security`, or wherever `GatewayAccess` lives per Task 3's `GatewayKeyController` reference — confirm namespace with `grep -n "^using\|^namespace" backend/Namines.API/Controllers/GatewayKeyController.cs` before writing the `using`).
- Produces: `POST /api/launch/{projectId}/download` returning the same
  `application/zip` shape as `ScaffolderController.ExportProject`, plus one
  extra root-level file.

- [ ] **Step 1: Add the DTO and the zip-post-processing helper**

At the top of `LaunchController.cs`, add the needed usings:

```csharp
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Namines.Core.Interfaces;
using Namines.Core.Models;
```

Add a small private static helper at the bottom of the `LaunchController`
class:

```csharp
    /// <summary>
    /// Var olan zip'e TEK bir kök dosya ekler. Beş üretici sınıfın
    /// (DotnetBackendScaffold / PythonScaffold / FrontendSdkScaffold / ...)
    /// hiçbiri değişmiyor — bu, "ham parola asla zip'e girmez" kararının
    /// TEK dokunduğu yer.
    /// </summary>
    private static byte[] AppendGatewayReadme(byte[] zipBytes, string gatewayUrl, string rawKey)
    {
        using var stream = new MemoryStream();
        stream.Write(zipBytes, 0, zipBytes.Length);
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true))
        {
            var entry = archive.CreateEntry("NAMINES-GATEWAY.md");
            using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
            writer.Write($"""
                # Connecting to your database

                This project's database connection is managed by Namines and its raw
                credentials are never written to a file — not even this one.

                Instead, a Gateway API key was created for this project. It reads and
                writes through Namines' own API, is scoped to this project, and can be
                revoked at any time from Desk's "API keys" screen.

                ```
                NAMINES_GATEWAY_URL={gatewayUrl}
                NAMINES_GATEWAY_KEY={rawKey}
                ```

                **Store this key now** — Namines cannot show it to you again. If you
                lose it, revoke it in Desk and create a new one.
                """);
        }
        return stream.ToArray();
    }
```

- [ ] **Step 2: Add the download action**

```csharp
    [HttpPost("{projectId}/download")]
    public async Task<IActionResult> Download(
        string projectId,
        [FromServices] IScaffolderService scaffolder,
        CancellationToken ct)
    {
        var userId = CurrentUserId;
        if (userId is null) return Unauthorized();

        if (await _context.GetRoleAsync(projectId, userId, ct) != OrgRole.Owner)
            return Forbid();

        var project = await _context.CloudProjects.FirstOrDefaultAsync(p => p.Id == projectId, ct);
        if (project is null) return NotFound(new { error = "Project not found." });

        DatabaseSchema schema;
        try
        {
            schema = JsonSerializer.Deserialize<DatabaseSchema>(project.SchemaJson,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new DatabaseSchema();
        }
        catch (JsonException)
        {
            return BadRequest(new { error = "The project's stored schema is not valid." });
        }

        var (entity, rawKey) = GatewayAccess.CreateKey(
            projectId, "Downloaded project", userId, canWrite: true, expiresAt: null);
        _context.GatewayApiKeys.Add(entity);
        await _context.SaveChangesAsync(ct);

        var zipBytes = await scaffolder.GenerateFullStackProjectAsync(schema);
        var apiOrigin = $"{Request.Scheme}://{Request.Host}";
        var withReadme = AppendGatewayReadme(zipBytes, $"{apiOrigin}/api/gateway", rawKey);

        return File(withReadme, "application/zip", "namines-project.zip");
    }
```

- [ ] **Step 3: Write the test**

```csharp
// backend/Namines.Tests/Controllers/LaunchDownloadZipTests.cs
using System.IO;
using System.IO.Compression;
using System.Linq;
using Xunit;

namespace Namines.Tests.Controllers;

/// <summary>
/// AppendGatewayReadme'nin sözleşmesi: mevcut zip içeriği KORUNUR, tek bir
/// yeni kök dosya eklenir, ve o dosya ham bir DB parolası DEĞİL bir Gateway
/// anahtarı taşır.
/// </summary>
public class LaunchDownloadZipTests
{
    private static byte[] BuildMinimalZip(string entryName, string content)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, true))
        {
            var entry = archive.CreateEntry(entryName);
            using var writer = new StreamWriter(entry.Open());
            writer.Write(content);
        }
        return stream.ToArray();
    }

    [Fact]
    public void Appends_gateway_readme_without_losing_existing_entries()
    {
        var original = BuildMinimalZip("backend/Program.cs", "// existing scaffolded file");

        var method = typeof(Namines.API.Controllers.LaunchController).GetMethod(
            "AppendGatewayReadme",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        var result = (byte[])method.Invoke(null, new object[]
        {
            original, "http://localhost:5000/api/gateway", "ngw_test_raw_key_value",
        })!;

        using var stream = new MemoryStream(result);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

        Assert.NotNull(archive.GetEntry("backend/Program.cs"));

        var readme = archive.GetEntry("NAMINES-GATEWAY.md");
        Assert.NotNull(readme);
        using var reader = new StreamReader(readme!.Open());
        var text = reader.ReadToEnd();

        Assert.Contains("ngw_test_raw_key_value", text);
        Assert.Contains("NAMINES_GATEWAY_URL=http://localhost:5000/api/gateway", text);
        Assert.DoesNotContain("Password=", text);
    }
}
```

- [ ] **Step 4: Run it**

Run: `dotnet test backend/Namines.Tests/Namines.Tests.csproj --filter LaunchDownloadZipTests`
Expected: 1 passed.

- [ ] **Step 5: Build the whole solution**

Run: `dotnet build backend/Namines.sln`
Expected: 0 errors.

- [ ] **Step 6: Commit**

```bash
git add backend/Namines.API/Controllers/LaunchController.cs backend/Namines.Tests/Controllers/LaunchDownloadZipTests.cs
git commit -m "feat: add POST /api/launch/{projectId}/download — Gateway key, never a raw password

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

---

### Task 6: Frontend client — `launchApi.ts`

> ✅ **Bitmiştir.** `frontend/services/launchApi.ts` yazıldı.

**Files:**
- Create: `frontend/services/launchApi.ts`

**Interfaces:**
- Consumes: `api` default export from `frontend/services/api.ts` (existing
  axios instance, cookie-authenticated); `groundApi.providers()` from
  `frontend/services/vaultGroundApi.ts` (existing — reused as-is for the
  provider picker, no new provider-listing code).
- Produces:
  - `export class LaunchError extends Error { status: number }`
  - `export type LaunchOutcome = { status: 'Ready'; projectId: string; deskHandoffToken: string; backupWarning: string | null } | { status: 'NeedsReview' } | { status: 'ProvisionFailed' | 'DdlFailed'; error: string }`
  - `export const launchApi = { launch(projectId, provider, ddlScript): Promise<LaunchOutcome>, download(projectId): Promise<{ blob: Blob; fileName: string }> }`

```typescript
// frontend/services/launchApi.ts
import api from './api';

/**
 * "/compile"'da onaylanmış bir şemayı çalışan bir Desk paneline (ve isteğe
 * bağlı indirilebilir bir kod projesine) dönüştüren tek uç için istemci.
 * Sağlayıcı listesi için AYRI bir çağrı YOK — Ground'un zaten var olan
 * `groundApi.providers()` (services/vaultGroundApi.ts) burada da kullanılıyor.
 */

export class LaunchError extends Error {
  constructor(message: string, readonly status: number) { super(message); }
}

export type LaunchOutcome =
  | { status: 'Ready'; projectId: string; deskHandoffToken: string; backupWarning: string | null }
  | { status: 'NeedsReview' }
  | { status: 'ProvisionFailed' | 'DdlFailed'; error: string };

function toLaunchError(err: unknown, fallback: string): never {
  const status = (err as { response?: { status?: number; data?: { error?: string } } })?.response?.status ?? 0;
  const body = (err as { response?: { data?: { error?: string } } })?.response?.data;
  throw new LaunchError(body?.error ?? fallback, status);
}

export const launchApi = {
  launch: async (projectId: string, provider: string, ddlScript: string): Promise<LaunchOutcome> => {
    try {
      const res = await api.post('/launch', { projectId, provider, ddlScript });
      return res.data;
    } catch (err) {
      // 400 gövdesi zaten { status, error } şeklinde geliyor — bunu olduğu gibi taşı.
      const body = (err as { response?: { data?: LaunchOutcome } })?.response?.data;
      if (body && 'status' in body) return body;
      toLaunchError(err, 'Launch failed.');
    }
  },

  download: async (projectId: string): Promise<{ blob: Blob; fileName: string }> => {
    try {
      const res = await api.post(`/launch/${encodeURIComponent(projectId)}/download`, null, {
        responseType: 'blob',
      });
      const disposition = res.headers['content-disposition'] ?? '';
      const match = /filename="?([^"]+)"?/.exec(disposition);
      return { blob: res.data, fileName: match?.[1] ?? 'namines-project.zip' };
    } catch (err) {
      toLaunchError(err, 'Download failed.');
    }
  },
};
```

- [ ] **Step 1: Type-check**

Run: `cd frontend && npx tsc --noEmit`
Expected: 0 errors.

- [ ] **Step 2: Commit**

```bash
git add frontend/services/launchApi.ts
git commit -m "feat: add launchApi client wrapper for POST /api/launch

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

---

### Task 7: `LaunchPanel.tsx` — the step checklist component

> ✅ **Bitmiştir.** `components/compile/LaunchPanel.tsx` + testi yazıldı.

**Files:**
- Create: `frontend/components/compile/LaunchPanel.tsx`
- Test: `frontend/components/compile/LaunchPanel.test.tsx`

**Interfaces:**
- Consumes: `launchApi.launch`/`launchApi.download`/`LaunchError`/`LaunchOutcome`
  (Task 6); `groundApi.providers()` and `type GroundProvider` from
  `frontend/services/vaultGroundApi.ts` (existing); `Panel`, `PanelBar`,
  `ActionButton`, `PanelEmpty` from `frontend/components/compile/PanelKit.tsx`
  (existing — see `frontend/app/vault/page.tsx` for the exact usage pattern
  already established this session); `useAuthStore` (existing, for
  `createDeskHandoffToken`-style flows — actually the token now comes back
  from `launchApi.launch` itself, no separate call needed).
- Produces: `export default function LaunchPanel({ projectId, projectName, ddlScript }: { projectId: string | null; projectName: string; ddlScript: string })` — a self-contained panel; Task 8 renders it inside `/compile`'s existing tab content area.

```tsx
// frontend/components/compile/LaunchPanel.tsx
'use client';

import { useEffect, useState } from 'react';
import { Rocket, CheckCircle2, XCircle, Loader2, ExternalLink, Download, GitPullRequestArrow } from 'lucide-react';
import { launchApi, LaunchError, type LaunchOutcome } from '../../services/launchApi';
import { groundApi, type GroundProvider } from '../../services/vaultGroundApi';
import { changeRequestService } from '../../services/api';
import { useRouter } from 'next/navigation';
import { Panel, PanelBar, ActionButton, PanelEmpty } from './PanelKit';
import { DatabaseSchema } from '../../types/schema';

type StepState = 'pending' | 'active' | 'done' | 'error';

interface StepRow { key: string; label: string; state: StepState; detail?: string }

const DESK_URL = process.env.NEXT_PUBLIC_DESK_URL ?? 'http://localhost:3200';

/**
 * "/compile"'daki tek tık akışı: Ground'da veritabanı aç, DDL'i uygula, ilk
 * Vault yedeğini al, Desk'i doğrudan bu projenin Data ekranına açan bir
 * jetonla döndür — ya da hedefte zaten veri varsa (canlı introspection
 * karar veriyor, biz DEĞİL) mevcut Change Review akışına yönlendir.
 *
 * Kod projesi indirme AYRI bir eylem: "Ready" durumundan sonra, ham DB
 * parolası ASLA — bir Gateway API anahtarı gömülü gelir (bkz. backend
 * LaunchController.Download).
 */
export default function LaunchPanel({
  projectId, projectName, ddlScript, schema,
}: {
  projectId: string | null;
  projectName: string;
  ddlScript: string;
  schema: DatabaseSchema;
}) {
  const router = useRouter();
  const [providers, setProviders] = useState<GroundProvider[] | null>(null);
  const [selectedProvider, setSelectedProvider] = useState<string | null>(null);
  const [steps, setSteps] = useState<StepRow[] | null>(null);
  const [outcome, setOutcome] = useState<LaunchOutcome | null>(null);
  const [downloading, setDownloading] = useState(false);
  const [downloadError, setDownloadError] = useState<string | null>(null);

  useEffect(() => {
    groundApi.providers().then(setProviders).catch(() => setProviders([]));
  }, []);

  const readyProviders = (providers ?? []).filter(p => p.problem === null);

  async function handleLaunch() {
    if (!projectId || !selectedProvider) return;

    setOutcome(null);
    setSteps([
      { key: 'provision', label: 'Opening database…', state: 'active' },
      { key: 'apply', label: 'Applying schema…', state: 'pending' },
      { key: 'backup', label: 'Taking first backup…', state: 'pending' },
      { key: 'ready', label: 'Ready', state: 'pending' },
    ]);

    try {
      const result = await launchApi.launch(projectId, selectedProvider, ddlScript);
      setOutcome(result);

      if (result.status === 'Ready') {
        setSteps([
          { key: 'provision', label: 'Database opened', state: 'done' },
          { key: 'apply', label: 'Schema applied', state: 'done' },
          {
            key: 'backup', label: result.backupWarning ? result.backupWarning : 'First backup taken',
            state: result.backupWarning ? 'error' : 'done',
          },
          { key: 'ready', label: 'Ready', state: 'done' },
        ]);
      } else if (result.status === 'NeedsReview') {
        setSteps([
          { key: 'provision', label: 'Database already has tables', state: 'done' },
          { key: 'apply', label: 'Schema NOT applied — sent to review instead', state: 'error' },
        ]);
      } else {
        setSteps(prev => (prev ?? []).map(s =>
          s.state === 'active' ? { ...s, state: 'error', detail: result.error } : s));
      }
    } catch (err) {
      const message = err instanceof LaunchError ? err.message : 'Launch failed unexpectedly.';
      setSteps(prev => (prev ?? []).map(s =>
        s.state === 'active' ? { ...s, state: 'error', detail: message } : s));
    }
  }

  async function handleNeedsReview() {
    if (!projectId) return;
    try {
      const { id } = await changeRequestService.createQuick(
        projectId, schema, 'Launch: schema update');
      router.push(`/review/${id}`);
    } catch {
      // handleLaunch'un kendi hata satırı zaten görünür; burada sessizce geç.
    }
  }

  function openDesk(deskHandoffToken: string, pid: string) {
    const form = document.createElement('form');
    form.method = 'POST';
    form.action = `${DESK_URL}/handoff`;
    form.target = '_blank';
    form.style.display = 'none';

    for (const [name, value] of [['token', deskHandoffToken], ['projectId', pid], ['view', 'data']]) {
      const input = document.createElement('input');
      input.type = 'hidden';
      input.name = name;
      input.value = value;
      form.appendChild(input);
    }

    document.body.appendChild(form);
    form.submit();
    document.body.removeChild(form);
  }

  async function handleDownload() {
    if (!projectId) return;
    setDownloading(true);
    setDownloadError(null);
    try {
      const { blob, fileName } = await launchApi.download(projectId);
      const url = URL.createObjectURL(blob);
      const link = document.createElement('a');
      link.href = url;
      link.download = fileName;
      link.click();
      URL.revokeObjectURL(url);
    } catch (err) {
      setDownloadError(err instanceof LaunchError ? err.message : 'Download failed.');
    } finally {
      setDownloading(false);
    }
  }

  if (!projectId) {
    return (
      <Panel>
        <PanelEmpty icon={Rocket} title="Save your project first"
          hint="Launch needs a saved project to open a database against. Give it a moment to sync, then try again." />
      </Panel>
    );
  }

  return (
    <Panel scroll className="h-full">
      <PanelBar left={<span className="text-[12px] font-semibold text-content-primary">Launch — {projectName}</span>} />
      <div className="p-3 space-y-3">
        {!steps && (
          <>
            <p className="text-[11px] text-content-muted leading-relaxed">
              Opens a real PostgreSQL database, applies this schema to it, takes a first
              backup, and hands you a working Desk panel — all in one step. If the target
              already has data, this sends the change to review instead of touching it.
            </p>
            {!providers ? (
              <div className="flex items-center gap-2 text-[11px] text-content-muted">
                <Loader2 className="w-3.5 h-3.5 animate-spin" /> Loading providers…
              </div>
            ) : readyProviders.length === 0 ? (
              <PanelEmpty icon={Rocket} title="No provider is ready"
                hint="An admin needs to configure at least one Ground provider (LocalPostgres, Neon, or Supabase) on the server." />
            ) : (
              <div className="space-y-2">
                {readyProviders.map(p => (
                  <label key={p.name}
                    className={`flex items-center gap-2 p-2.5 rounded-[var(--radius-control)] border cursor-pointer text-[11px] ${
                      selectedProvider === p.name ? 'border-accent-hover bg-accent-subtle' : 'border-surface-500 bg-surface-600'
                    }`}>
                    <input type="radio" name="provider" value={p.name}
                      checked={selectedProvider === p.name}
                      onChange={() => setSelectedProvider(p.name)} />
                    <span className="font-semibold text-content-primary">{p.name}</span>
                  </label>
                ))}
                <ActionButton icon={Rocket} tone="primary" full
                  disabled={!selectedProvider} onClick={handleLaunch}>
                  Launch
                </ActionButton>
              </div>
            )}
          </>
        )}

        {steps && (
          <div className="space-y-1.5">
            {steps.map(s => (
              <div key={s.key} className="flex items-start gap-2 text-[11px]">
                {s.state === 'done' && <CheckCircle2 className="w-3.5 h-3.5 text-accent-text shrink-0 mt-0.5" />}
                {s.state === 'error' && <XCircle className="w-3.5 h-3.5 text-[var(--color-danger)] shrink-0 mt-0.5" />}
                {s.state === 'active' && <Loader2 className="w-3.5 h-3.5 animate-spin text-content-muted shrink-0 mt-0.5" />}
                {s.state === 'pending' && <span className="w-3.5 h-3.5 rounded-full border border-surface-500 shrink-0 mt-0.5" />}
                <div>
                  <p className={s.state === 'error' ? 'text-[var(--color-danger)]' : 'text-content-secondary'}>{s.label}</p>
                  {s.detail && <p className="text-content-muted text-[10.5px] mt-0.5">{s.detail}</p>}
                </div>
              </div>
            ))}
          </div>
        )}

        {outcome?.status === 'Ready' && (
          <div className="flex flex-wrap gap-2 pt-1">
            <ActionButton icon={ExternalLink} tone="primary"
              onClick={() => openDesk(outcome.deskHandoffToken, outcome.projectId)}>
              Open Desk
            </ActionButton>
            <ActionButton icon={Download} busy={downloading} onClick={handleDownload}>
              Download project
            </ActionButton>
          </div>
        )}
        {downloadError && <p className="text-[11px] text-[var(--color-danger)]">{downloadError}</p>}

        {outcome?.status === 'NeedsReview' && (
          <ActionButton icon={GitPullRequestArrow} tone="primary" onClick={handleNeedsReview}>
            Review changes
          </ActionButton>
        )}

        {(outcome?.status === 'ProvisionFailed' || outcome?.status === 'DdlFailed') && (
          <ActionButton tone="primary" onClick={() => { setSteps(null); setOutcome(null); }}>
            Try again
          </ActionButton>
        )}
      </div>
    </Panel>
  );
}
```

- [ ] **Step 1: Write the failing test**

```tsx
// frontend/components/compile/LaunchPanel.test.tsx
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import LaunchPanel from './LaunchPanel';
import { launchApi } from '../../services/launchApi';
import { groundApi } from '../../services/vaultGroundApi';

vi.mock('../../services/launchApi');
vi.mock('../../services/vaultGroundApi');
vi.mock('next/navigation', () => ({ useRouter: () => ({ push: vi.fn() }) }));

const emptySchema = { name: 'test', tables: [], relations: [] } as never;

describe('LaunchPanel', () => {
  beforeEach(() => {
    vi.mocked(groundApi.providers).mockResolvedValue([
      { name: 'LocalPostgres', liveVerified: true, supportsBranching: false, supportsRegionChoice: false, responsibility: '', problem: null },
    ]);
  });

  it('shows a save-project prompt when there is no project yet', () => {
    render(<LaunchPanel projectId={null} projectName="" ddlScript="" schema={emptySchema} />);
    expect(screen.getByText(/save your project first/i)).toBeInTheDocument();
  });

  it('walks through the checklist to Ready and shows Open Desk + Download', async () => {
    vi.mocked(launchApi.launch).mockResolvedValue({
      status: 'Ready', projectId: 'p1', deskHandoffToken: 'tok', backupWarning: null,
    });

    render(<LaunchPanel projectId="p1" projectName="Demo" ddlScript="CREATE TABLE x();" schema={emptySchema} />);

    await waitFor(() => expect(screen.getByText('LocalPostgres')).toBeInTheDocument());
    fireEvent.click(screen.getByText('LocalPostgres'));
    fireEvent.click(screen.getByRole('button', { name: /launch/i }));

    await waitFor(() => expect(screen.getByText('Open Desk')).toBeInTheDocument());
    expect(screen.getByText('Download project')).toBeInTheDocument();
    expect(screen.getByText('Ready')).toBeInTheDocument();
  });

  it('surfaces a visible backup warning instead of silently succeeding', async () => {
    vi.mocked(launchApi.launch).mockResolvedValue({
      status: 'Ready', projectId: 'p1', deskHandoffToken: 'tok',
      backupWarning: 'Initial backup failed: disk full. Back it up manually from the Vault tab.',
    });

    render(<LaunchPanel projectId="p1" projectName="Demo" ddlScript="CREATE TABLE x();" schema={emptySchema} />);
    await waitFor(() => expect(screen.getByText('LocalPostgres')).toBeInTheDocument());
    fireEvent.click(screen.getByText('LocalPostgres'));
    fireEvent.click(screen.getByRole('button', { name: /launch/i }));

    await waitFor(() =>
      expect(screen.getByText(/initial backup failed: disk full/i)).toBeInTheDocument());
  });

  it('shows the Review changes path when the target already has tables', async () => {
    vi.mocked(launchApi.launch).mockResolvedValue({ status: 'NeedsReview' });

    render(<LaunchPanel projectId="p1" projectName="Demo" ddlScript="CREATE TABLE x();" schema={emptySchema} />);
    await waitFor(() => expect(screen.getByText('LocalPostgres')).toBeInTheDocument());
    fireEvent.click(screen.getByText('LocalPostgres'));
    fireEvent.click(screen.getByRole('button', { name: /launch/i }));

    await waitFor(() => expect(screen.getByText('Review changes')).toBeInTheDocument());
    expect(screen.getByText(/schema NOT applied/i)).toBeInTheDocument();
  });
});
```

- [ ] **Step 2: Run to confirm it fails**

Run: `cd frontend && npx vitest run components/compile/LaunchPanel.test.tsx`
Expected: FAIL — `LaunchPanel` module not found.

- [ ] **Step 3: Create `LaunchPanel.tsx`**

Use the full component code block above.

- [ ] **Step 4: Run the tests again**

Run: `cd frontend && npx vitest run components/compile/LaunchPanel.test.tsx`
Expected: 4 passed.

- [ ] **Step 5: Type-check**

Run: `cd frontend && npx tsc --noEmit`
Expected: 0 errors.

- [ ] **Step 6: Commit**

```bash
git add frontend/components/compile/LaunchPanel.tsx frontend/components/compile/LaunchPanel.test.tsx
git commit -m "feat: add LaunchPanel — step checklist UI for POST /api/launch

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

---

### Task 8: Wire `LaunchPanel` into `/compile`

> ✅ **Bitmiştir.** `/compile` sayfasına bağlandı.

**Files:**
- Modify: `frontend/app/compile/page.tsx`

**Interfaces:**
- Consumes: `LaunchPanel` (Task 7); `useProjectHistoryStore` (existing,
  `activeProjectId` — not currently imported in this file, needs adding);
  the existing `TABS`/`activeTab` state machine already in this file.

- [ ] **Step 1: Add the import and a new tab entry**

In `frontend/app/compile/page.tsx`, add to the existing imports:

```tsx
import { useProjectHistoryStore } from '../../store/useProjectHistoryStore';
import LaunchPanel from '../../components/compile/LaunchPanel';
```

Find the `TABS` array (currently ending with `SANDBOX`) and add one more
entry — reuse the `Rocket` icon already imported nowhere yet in this file,
so add `Rocket` to the existing `lucide-react` import line at the top:

```tsx
import { ArrowLeft, FileCode2, Boxes, Layers, Blocks, GitFork, Database, BookOpenText, FileText, Container, Download, PanelsTopLeft, ExternalLink, ShieldCheck, HardDrive, Rocket } from 'lucide-react';
```

```tsx
const TABS: { id: TabId; label: string; icon: typeof FileCode2 }[] = [
  { id: 'SQL',        label: 'DDL Script',       icon: FileCode2 },
  { id: 'EF',         label: 'EF Core',          icon: Boxes },
  { id: 'PRISMA',     label: 'Prisma',           icon: Layers },
  { id: 'EJECT',      label: 'Export to…',       icon: Blocks },
  { id: 'ER',         label: 'Mermaid ER',       icon: GitFork },
  { id: 'MOCK',       label: 'Test Data',        icon: Database },
  { id: 'DICTIONARY', label: 'Data Dictionary',  icon: BookOpenText },
  { id: 'README',     label: 'README.md',        icon: FileText },
  { id: 'SANDBOX',    label: 'Docker Sandbox',   icon: Container },
  { id: 'LAUNCH',     label: 'Launch',           icon: Rocket },
];
```

Add `'LAUNCH'` to the `TabId` union right above:

```tsx
type TabId = 'SQL' | 'EF' | 'PRISMA' | 'EJECT' | 'ER' | 'MOCK' | 'DICTIONARY' | 'README' | 'SANDBOX' | 'LAUNCH';
```

- [ ] **Step 2: Read the active project id**

Inside the `CompilePage` component body, alongside the existing
`const { schema, dbType, setDbType, projectName } = useSchemaStore();` line, add:

```tsx
  const activeProjectId = useProjectHistoryStore(s => s.activeProjectId);
```

- [ ] **Step 3: Force PostgreSQL DDL for the Launch tab specifically**

Find the existing content-render block (the one with
`{activeTab === 'SANDBOX' && <DockerSandboxPanel .../>}` and its siblings)
and add, in the same conditional chain:

```tsx
          {activeTab === 'LAUNCH' && (
            <LaunchPanel
              projectId={activeProjectId}
              projectName={projectName}
              // Ground yalnizca PostgreSQL destekliyor — sekmenin kendi DB
              // secicisindeki motoru degil, her zaman Postgres DDL'ini kullan.
              ddlScript={dbType === 'PostgreSQL' ? sql : ''}
              schema={schema}
            />
          )}
```

Since `sql` is only guaranteed to be the currently-selected `dbType`'s DDL
(see the existing `fetchSql` effect earlier in this file), and Launch always
needs PostgreSQL DDL regardless of what the DDL Script tab currently shows,
add a small dedicated fetch instead of reusing `sql` directly. Right after
the existing `fetchSql`-driven `useEffect`, add:

```tsx
  const [launchDdl, setLaunchDdl] = useState('');
  useEffect(() => {
    if (!schema) return;
    let ignore = false;
    schemaService.compileSql(schema, 'PostgreSQL').then(generated => {
      if (!ignore) setLaunchDdl(generated);
    }).catch(() => { if (!ignore) setLaunchDdl(''); });
    return () => { ignore = true; };
  }, [schema]);
```

and change the `LaunchPanel` usage above to pass `ddlScript={launchDdl}`
instead of the conditional `sql` expression.

- [ ] **Step 4: Add the sidebar entry — remove the ad-hoc conditional above and use the existing nav button pattern instead**

The existing sidebar `nav` block renders `TABS.map(...)` automatically —
because `LAUNCH` was added to the `TABS` array in Step 1, it already gets a
sidebar button with no further change needed. Delete nothing else here;
this step exists only to confirm (visually, in Step 6) that no separate
button markup is required, unlike the standalone "Namines Desk / Ground /
Vault" buttons which are NOT part of `TABS` (they navigate away instead of
switching a tab).

- [ ] **Step 5: Type-check**

Run: `cd frontend && npx tsc --noEmit`
Expected: 0 errors.

- [ ] **Step 6: Manual verification in the browser**

Start (or reuse) the frontend dev server, open `/compile` with a generated
schema, click the new "Launch" tab in the left sidebar, confirm the provider
picker renders. (Full click-through to `Ready` requires the backend +
Postgres running — covered by Task 2's automated test; this manual pass is
just confirming the tab wires up and renders without a console error.)

- [ ] **Step 7: Commit**

```bash
git add frontend/app/compile/page.tsx
git commit -m "feat: wire LaunchPanel into /compile as a new Launch tab

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

---

### Task 9: Full-plan self-review and final verification pass

> ✅ **Bitmiştir.** Uçtan uca doğrulandı; ayrıca bu turda şema JSON enum dönüştürücü hatası (`8f753a2`) bulunup düzeltildi.

- [ ] **Step 1: Re-read the spec against this plan**

Run: `cat docs/superpowers/specs/2026-09-13-launch-integration-design.md`

Confirm every numbered section (§2 orchestration, §3 Gateway-keyed download,
§4 UI/checklist, §5 error handling, §6 tests) maps to a task above:
§2→Tasks 2–3, §3→Task 5, §4→Tasks 6–8, §5→Task 2's `LaunchResult` shape +
Task 7's error rendering, §6→Tasks 2, 5, 7.

- [ ] **Step 2: Run the full backend test suite**

Run: `dotnet test backend/Namines.sln`
Expected: all green (or skipped-with-reason for Docker-less environments —
never a new red).

- [ ] **Step 3: Run the full frontend test suite and type/lint checks**

Run:
```bash
cd frontend
npx tsc --noEmit
npm run lint
npx vitest run
```
Expected: 0 type errors, 0 new lint errors, all vitest suites green.

- [ ] **Step 4: End-to-end manual smoke test (requires Docker + all three services running)**

1. Start Postgres: `docker compose up -d namines-control-db`
2. Start the backend, frontend, and Desk dev servers.
3. In the frontend, build any schema through to `/compile`.
4. Click the "Launch" tab → pick "LocalPostgres" → click "Launch".
5. Confirm the checklist reaches "Ready" and both "Open Desk" and "Download
   project" appear.
6. Click "Open Desk" — confirm a new tab opens Desk landed directly on that
   project's Data view (not the project overview list).
7. Click "Download project" — confirm a zip downloads containing
   `NAMINES-GATEWAY.md` with a Gateway key, and that no `Password=` string
   appears anywhere in the archive (`unzip -p namines-project.zip
   NAMINES-GATEWAY.md | grep -i password` should find nothing).
8. Run Launch a second time on the same project with a changed schema —
   confirm it shows "Review changes" instead of silently reapplying DDL, and
   that clicking it lands on `/review/{id}`.

- [ ] **Step 5: Final commit (only if Step 4 surfaced fixes)**

If the manual pass in Step 4 required any small fixes, commit them now with
a message describing exactly what the smoke test caught. If nothing needed
fixing, this step is a no-op — do not create an empty commit.
