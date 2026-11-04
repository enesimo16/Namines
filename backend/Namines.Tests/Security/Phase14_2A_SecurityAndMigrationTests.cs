using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Namines.API.Controllers;
using Namines.BuildingBlocks.Modules;
using Namines.BuildingBlocks.Persistence;
using Namines.BuildingBlocks.Security;
using Namines.Core.Models.Auth;
using Namines.Core.Security;
using Namines.Infrastructure.Data;
using Namines.Infrastructure.Security;
using Namines.Tests.Integration;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace Namines.Tests.Security;

/// <summary>
/// Phase 14.2A: Kritik Veri Güvenliği ve Sahiplik Normalizasyonu Kabul ve Güvenlik Denetim Testleri.
///
/// Denetlenen Kapsam:
/// 1. Credential Encryption, Retrieval and AES-256-GCM Protection
/// 2. Ciphertext Tampering & Cryptographic Authentication Verification
/// 3. Cross-Project Ciphertext Relocation Prevention via Project-Bound AAD
/// 4. Key Rotation and Legacy Credential Compatibility
/// 5. Credential Redaction (JSON serialization leakage prevention)
/// 6. Tenant Isolation: ResolveForUserAsync blocks unauthorized access before decryption
/// 7. Legacy Fallback cannot bypass tenant authorization
/// 8. ShareToken Cryptographic Distinction: rawToken != storedHash, old URLs continue to work, hash-only access denied
/// 9. Real PostgreSQL Container: Realistic Multi-Tenant Legacy Dataset Migration & Idempotency
/// </summary>
[Collection("Docker")]
public sealed class Phase14_2A_SecurityAndMigrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer? _pgContainer = DockerAvailable.Value ? new PostgreSqlBuilder("postgres:17-alpine").Build() : null;
    private SqliteConnection _sqliteConn = null!;
    private DbContextOptions<AuthDbContext> _authOptions = null!;
    private DbContextOptions<StudioDbContext> _studioOptions = null!;
    private IConnectionSecretProtector _protector = null!;
    private IConfiguration _config = null!;

    public async Task InitializeAsync()
    {
        // 1. In-memory SQLite for fast logical/unit verification
        _sqliteConn = new SqliteConnection("DataSource=:memory:");
        await _sqliteConn.OpenAsync();

        _authOptions = new DbContextOptionsBuilder<AuthDbContext>()
            .UseSqlite(_sqliteConn)
            .Options;

        _studioOptions = new DbContextOptionsBuilder<StudioDbContext>()
            .UseSqlite(_sqliteConn)
            .Options;

        await using var db = new AuthDbContext(_authOptions);
        await db.Database.EnsureCreatedAsync();

        _config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Security:ConnectionEncryptionKey"] = "test-secret-key-that-is-at-least-32-chars-long!"
            })
            .Build();

        _protector = new AesGcmConnectionSecretProtector(_config);

        // 2. Start PostgreSQL container if Docker is available
        if (_pgContainer != null)
        {
            await _pgContainer.StartAsync();
        }
    }

    public async Task DisposeAsync()
    {
        await _sqliteConn.DisposeAsync();
        if (_pgContainer != null)
        {
            await _pgContainer.DisposeAsync();
        }
    }

    [Fact]
    public async Task Credential_Encryption_UsesAesGcm_AndNeverStoresPlaintext()
    {
        var rawConn = "Host=prod.db.internal;Database=finance;Username=admin;Password=SuperSecret123!";
        var encrypted = _protector.Protect(rawConn);

        Assert.NotEqual(rawConn, encrypted);
        Assert.DoesNotContain("SuperSecret123!", encrypted);
        Assert.True(encrypted.StartsWith("k.") || encrypted.StartsWith("v1:")); // AES-GCM authenticated format

        var decrypted = _protector.Unprotect(encrypted);
        Assert.Equal(rawConn, decrypted);
    }

    [Fact]
    public void Ciphertext_Tampering_Throws_CryptographicException()
    {
        var rawConn = "Host=db.internal;Database=app;Username=app;Password=secret";
        var encrypted = _protector.Protect(rawConn);

        // Corrupt the ciphertext payload
        var parts = encrypted.Split(':');
        var rawCipherBytes = Convert.FromBase64String(parts[3]);
        rawCipherBytes[0] ^= 0xFF; // Flip bits
        var tamperedCipher = string.Join(':', parts[0], parts[1], parts[2], Convert.ToBase64String(rawCipherBytes));

        Assert.ThrowsAny<CryptographicException>(() => _protector.Unprotect(tamperedCipher));
    }

    [Fact]
    public void Ciphertext_CopiedToAnotherProject_FailsAuthentication_WhenBoundWithAAD()
    {
        var rawConn = "Host=acme.db;Database=financials;Username=cfo;Password=Confidential!";
        var projA = "proj-alpha-tenant-1";
        var projB = "proj-beta-tenant-2";

        // Secret is encrypted and cryptographically bound to Project A
        var ciphertextBoundToA = _protector.Protect(rawConn, associatedData: projA);

        // Attempting to decrypt under Project A succeeds
        var decryptedA = _protector.Unprotect(ciphertextBoundToA, associatedData: projA);
        Assert.Equal(rawConn, decryptedA);

        // Attempting to decrypt the transplanted ciphertext under Project B FAILS!
        Assert.ThrowsAny<CryptographicException>(() => _protector.Unprotect(ciphertextBoundToA, associatedData: projB));
    }

    [Fact]
    public void KeyRotation_PreservesAccess_ToExistingCredentials()
    {
        // 1. Secret encrypted with legacy single key
        var legacyConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Security:ConnectionEncryptionKey"] = "legacy-encryption-key-32-characters-minimum!"
            })
            .Build();
        var legacyProtector = new AesGcmConnectionSecretProtector(legacyConfig);
        var oldCiphertext = legacyProtector.Protect("Host=legacy-db;Database=old;Password=p1");
        Assert.StartsWith("v1:", oldCiphertext);

        // 2. Configure rotated keys with active key2 and legacy key preserved
        var rotatedConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Security:ConnectionEncryptionKey"] = "legacy-encryption-key-32-characters-minimum!",
                ["Security:ConnectionEncryptionKeys:0:Id"] = "key1",
                ["Security:ConnectionEncryptionKeys:0:Key"] = "key-number-one-at-least-32-chars-long!",
                ["Security:ConnectionEncryptionKeys:1:Id"] = "key2",
                ["Security:ConnectionEncryptionKeys:1:Key"] = "key-number-two-at-least-32-chars-long!"
            })
            .Build();
        var rotatedProtector = new AesGcmConnectionSecretProtector(rotatedConfig);

        // New encryption uses active key (key2)
        var newCiphertext = rotatedProtector.Protect("Host=new-db;Database=new;Password=p2");
        Assert.StartsWith("k.key2:", newCiphertext);

        // Rotated protector successfully decrypts BOTH legacy and new formats
        Assert.Equal("Host=legacy-db;Database=old;Password=p1", rotatedProtector.Unprotect(oldCiphertext));
        Assert.Equal("Host=new-db;Database=new;Password=p2", rotatedProtector.Unprotect(newCiphertext));
    }

    [Fact]
    public void Credential_Redaction_JsonIgnore_PreventsSecretLeakInApiSerialization()
    {
        var project = new CloudProject
        {
            Id = "p-sec",
            Name = "Secured",
            DbType = "PostgreSQL",
            SchemaJson = "{}",
            NodePositionsJson = "{}",
            UserId = "u1",
            EncryptedConnectionString = "k.active:ciphertext-payload-hidden"
        };

        var json = JsonSerializer.Serialize(project);
        Assert.DoesNotContain("EncryptedConnectionString", json);
        Assert.DoesNotContain("ciphertext-payload-hidden", json);
    }

    [Fact]
    public async Task TenantIsolation_ResolveForUserAsync_BlocksUnauthorizedAccess()
    {
        await using (var db = new AuthDbContext(_authOptions))
        {
            db.Users.Add(new ApplicationUser { Id = "owner-1", UserName = "owner" });
            db.Users.Add(new ApplicationUser { Id = "attacker-1", UserName = "attacker" });
            db.Organizations.Add(new Organization { Id = "org-1", Name = "Org 1", CreatedByUserId = "owner-1" });
            db.OrganizationMembers.Add(new OrganizationMember { OrganizationId = "org-1", UserId = "owner-1", Role = OrgRole.Owner });

            var rawConn = "Host=db.acme.com;Database=secret;Username=root;Password=topsecret";
            db.CloudProjects.Add(new CloudProject
            {
                Id = "proj-tenant",
                Name = "Acme DB",
                DbType = "PostgreSQL",
                SchemaJson = "{}",
                NodePositionsJson = "{}",
                UserId = "owner-1",
                OrganizationId = "org-1",
                EncryptedConnectionString = _protector.Protect(rawConn, associatedData: "proj-tenant"),
                ConnectionDbType = "PostgreSQL",
                AllowDeskSql = true
            });
            await db.SaveChangesAsync();
        }

        await using var readDb = new AuthDbContext(_authOptions);
        var authority = new TenantProjectAuthorityService(readDb, _protector);

        // 1. Owner can resolve
        var ownerResolved = await authority.ResolveForUserAsync("proj-tenant", "owner-1");
        Assert.NotNull(ownerResolved);
        Assert.Equal("Host=db.acme.com;Database=secret;Username=root;Password=topsecret", ownerResolved.ConnectionString);

        // 2. Attacker cannot resolve
        var attackerResolved = await authority.ResolveForUserAsync("proj-tenant", "attacker-1");
        Assert.Null(attackerResolved);
    }

    [Fact]
    public async Task LegacyFallback_CannotBypassAuthorization()
    {
        // Stored only in legacy CloudProjects without studio.project_connections row
        await using (var db = new AuthDbContext(_authOptions))
        {
            db.Users.Add(new ApplicationUser { Id = "usr-leg-1", UserName = "leg-owner" });
            db.Users.Add(new ApplicationUser { Id = "usr-leg-bad", UserName = "intruder" });
            db.Organizations.Add(new Organization { Id = "org-leg", Name = "Org Leg", CreatedByUserId = "usr-leg-1" });
            db.OrganizationMembers.Add(new OrganizationMember { OrganizationId = "org-leg", UserId = "usr-leg-1", Role = OrgRole.Owner });

            db.CloudProjects.Add(new CloudProject
            {
                Id = "proj-leg-fallback",
                Name = "Fallback Project",
                DbType = "PostgreSQL",
                SchemaJson = "{}",
                NodePositionsJson = "{}",
                UserId = "usr-leg-1",
                OrganizationId = "org-leg",
                EncryptedConnectionString = _protector.Protect("Host=leg-db;Password=secret", associatedData: "proj-leg-fallback"),
                ConnectionDbType = "PostgreSQL"
            });
            await db.SaveChangesAsync();
        }

        await using var readDb = new AuthDbContext(_authOptions);
        var authority = new TenantProjectAuthorityService(readDb, _protector);

        // Intruder gets null
        var intruderRes = await authority.ResolveForUserAsync("proj-leg-fallback", "usr-leg-bad");
        Assert.Null(intruderRes);

        // Legitimate owner gets decrypted connection
        var ownerRes = await authority.ResolveForUserAsync("proj-leg-fallback", "usr-leg-1");
        Assert.NotNull(ownerRes);
        Assert.Equal("Host=leg-db;Password=secret", ownerRes.ConnectionString);
    }

    [Fact]
    public async Task ShareToken_CryptographicDistinction_AndOldUrlsContinueToWork()
    {
        var rawLegacyToken = "legacy-shared-url-token-xyz-123";
        var computedHash = ShareTokenHasher.Hash(rawLegacyToken);

        // CRITICAL CRYPTOGRAPHIC CHECK: rawToken in URL != storedHash in database
        Assert.NotEqual(rawLegacyToken, computedHash);
        Assert.Equal(64, computedHash.Length); // 256-bit hex string

        await using (var db = new AuthDbContext(_authOptions))
        {
            db.Users.Add(new ApplicationUser { Id = "u-tok", UserName = "tok-user" });
            db.Organizations.Add(new Organization { Id = "org-tok", Name = "Org Tok", CreatedByUserId = "u-tok" });
            db.OrganizationMembers.Add(new OrganizationMember { OrganizationId = "org-tok", UserId = "u-tok", Role = OrgRole.Owner });

            db.CloudProjects.Add(new CloudProject
            {
                Id = "p-migrated-tok",
                Name = "Migrated Schema",
                DbType = "PostgreSQL",
                SchemaJson = "{\"tables\":[]}",
                NodePositionsJson = "{}",
                UserId = "u-tok",
                OrganizationId = "org-tok",
                ShareTokenHash = computedHash // Stored hash
                // Note: ShareToken plaintext is NULL
            });
            await db.SaveChangesAsync();
        }

        await using var readDb = new AuthDbContext(_authOptions);
        var controller = new ShareController(readDb, linter: null!, configuration: null!);

        // 1. Old active share URL using rawToken WORKS!
        var oldUrlResult = await controller.GetSharedSchema(rawLegacyToken);
        Assert.IsType<OkObjectResult>(oldUrlResult);

        // 2. Passing the hash itself as a token FAILS with 404 (because SHA256(hash) != hash)
        var hashAsTokenResult = await controller.GetSharedSchema(computedHash);
        Assert.IsType<NotFoundResult>(hashAsTokenResult);

        // 3. Invalid/unshared token returns 404
        var invalidResult = await controller.GetSharedSchema("completely-bogus-token");
        Assert.IsType<NotFoundResult>(invalidResult);

        // 4. Database verification: plaintext is null
        var entity = await readDb.CloudProjects.SingleAsync(p => p.Id == "p-migrated-tok");
        Assert.Null(entity.ShareToken);
        Assert.Equal(computedHash, entity.ShareTokenHash);
    }

    [RequiresDockerFact]
    public async Task PostgreSQL_ExpandBackfillVerifyContract_RealisticMultiTenantDataset()
    {
        if (_pgContainer == null) return;
        var connStr = _pgContainer.GetConnectionString();
        await using var conn = new NpgsqlConnection(connStr);
        await conn.OpenAsync();

        // 1. Create baseline legacy schema with multi-tenant data
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                CREATE TABLE IF NOT EXISTS "Organizations" (
                    "Id" text PRIMARY KEY,
                    "Name" text NOT NULL
                );

                CREATE TABLE IF NOT EXISTS "CloudProjects" (
                    "Id" text PRIMARY KEY,
                    "Name" text NOT NULL,
                    "DbType" text NOT NULL,
                    "UserId" text NOT NULL,
                    "OrganizationId" text NULL REFERENCES "Organizations"("Id") ON DELETE CASCADE,
                    "EncryptedConnectionString" text NULL,
                    "ConnectionDbType" text NULL,
                    "AllowDeskSql" boolean NOT NULL DEFAULT false,
                    "ShareToken" text NULL,
                    "ShareTokenHash" text NULL,
                    "UpdatedAt" timestamptz NOT NULL DEFAULT now()
                );

                CREATE SCHEMA IF NOT EXISTS namines_vault;
                CREATE TABLE IF NOT EXISTS namines_vault.vault_backups (
                    "Id" text PRIMARY KEY,
                    "ProjectId" text NOT NULL REFERENCES "CloudProjects"("Id") ON DELETE RESTRICT,
                    "OrganizationId" text NULL CONSTRAINT "FK_vault_backups_Organizations_OrganizationId" REFERENCES "Organizations"("Id") ON DELETE RESTRICT
                );

                -- Multi-tenant seed dataset:
                INSERT INTO "Organizations" ("Id", "Name") VALUES
                    ('org-alpha', 'Alpha Inc'),
                    ('org-beta', 'Beta Corp');

                INSERT INTO "CloudProjects" ("Id", "Name", "DbType", "UserId", "OrganizationId", "EncryptedConnectionString", "ConnectionDbType", "AllowDeskSql", "ShareToken", "ShareTokenHash")
                VALUES
                    -- Project 1: Plaintext ShareToken, needs hashing; has credentials
                    ('p1', 'Alpha Core', 'PostgreSQL', 'u1', 'org-alpha', 'k.key1:enc_alpha_1', 'PostgreSQL', true, 'raw-active-share-token-1', NULL),
                    -- Project 2: Pre-existing ShareTokenHash, plaintext token also present; has credentials
                    ('p2', 'Alpha Reporting', 'PostgreSQL', 'u1', 'org-alpha', 'k.key1:enc_alpha_2', 'PostgreSQL', false, 'raw-active-share-token-2', encode(sha256('raw-active-share-token-2'::bytea), 'hex')),
                    -- Project 3: Beta Tenant: No share link, has credentials
                    ('p3', 'Beta Backend', 'PostgreSQL', 'u2', 'org-beta', 'k.key1:enc_beta_1', 'PostgreSQL', true, NULL, NULL),
                    -- Project 4: Design-only project: No credentials, no share link
                    ('p4', 'Beta Prototype', 'PostgreSQL', 'u2', 'org-beta', NULL, NULL, false, NULL, NULL),
                    -- Project 5: Malformed legacy entry with empty strings
                    ('p5', 'Empty Edge Case', 'PostgreSQL', 'u2', 'org-beta', '', '', false, '', NULL);

                INSERT INTO namines_vault.vault_backups ("Id", "ProjectId", "OrganizationId")
                VALUES
                    ('b1', 'p1', 'org-alpha'),
                    ('b2', 'p3', 'org-beta');
                """;
            await cmd.ExecuteNonQueryAsync();
        }

        // 2. STEP 1: EXPAND
        await using (var expandCmd = conn.CreateCommand())
        {
            expandCmd.CommandText = Phase14_2A_SecurityMigrationSql.BuildExpandSql();
            await expandCmd.ExecuteNonQueryAsync();
        }

        // Verify studio.project_connections table exists
        await using (var checkCmd = conn.CreateCommand())
        {
            checkCmd.CommandText = "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = 'studio' AND table_name = 'project_connections';";
            var count = (long)(await checkCmd.ExecuteScalarAsync())!;
            Assert.Equal(1, count);
        }

        // 3. STEP 2: BACKFILL (Run twice to prove idempotency under repeated calls)
        for (int i = 0; i < 2; i++)
        {
            await using var backfillCmd = conn.CreateCommand();
            backfillCmd.CommandText = Phase14_2A_SecurityMigrationSql.BuildBackfillSql();
            await backfillCmd.ExecuteNonQueryAsync();
        }

        // 4. STEP 3: VERIFY (Transactional row count and consistency validation)
        await using (var verifyCmd = conn.CreateCommand())
        {
            verifyCmd.CommandText = Phase14_2A_SecurityMigrationSql.BuildVerifySql();
            await verifyCmd.ExecuteNonQueryAsync(); // Must not throw RAISE EXCEPTION
        }

        // Verify exact records in studio.project_connections:
        // p1, p2, p3 have valid non-empty credentials. p4 (null) and p5 (empty) must NOT be inserted.
        await using (var countCmd = conn.CreateCommand())
        {
            countCmd.CommandText = "SELECT COUNT(*) FROM studio.project_connections;";
            var pcCount = (long)(await countCmd.ExecuteScalarAsync())!;
            Assert.Equal(3, pcCount);
        }

        // Verify p1's ShareTokenHash was computed from 'raw-active-share-token-1'
        await using (var hashCmd = conn.CreateCommand())
        {
            hashCmd.CommandText = "SELECT \"ShareTokenHash\" FROM \"CloudProjects\" WHERE \"Id\" = 'p1';";
            var p1Hash = (string)(await hashCmd.ExecuteScalarAsync())!;
            Assert.Equal(ShareTokenHasher.Hash("raw-active-share-token-1"), p1Hash);
        }

        // 5. STEP 4: CONTRACT
        await using (var contractCmd = conn.CreateCommand())
        {
            contractCmd.CommandText = Phase14_2A_SecurityMigrationSql.BuildContractSql();
            await contractCmd.ExecuteNonQueryAsync();
        }

        // Verify plaintext ShareToken is completely wiped (NULL) across all rows
        await using (var checkPlainCmd = conn.CreateCommand())
        {
            checkPlainCmd.CommandText = "SELECT COUNT(*) FROM \"CloudProjects\" WHERE \"ShareToken\" IS NOT NULL;";
            var plainCount = (long)(await checkPlainCmd.ExecuteScalarAsync())!;
            Assert.Equal(0, plainCount);
        }

        // Verify redundant OrganizationId FK constraint on vault_backups was dropped
        await using (var checkFkCmd = conn.CreateCommand())
        {
            checkFkCmd.CommandText = """
                SELECT COUNT(*) FROM pg_constraint
                WHERE conname = 'FK_vault_backups_Organizations_OrganizationId';
                """;
            var fkCount = (long)(await checkFkCmd.ExecuteScalarAsync())!;
            Assert.Equal(0, fkCount);
        }

        // Verify CASCADE delete: deleting Project 1 removes its project_connections entry automatically
        await using (var deleteCmd = conn.CreateCommand())
        {
            deleteCmd.CommandText = """
                DELETE FROM namines_vault.vault_backups WHERE "ProjectId" = 'p1';
                DELETE FROM "CloudProjects" WHERE "Id" = 'p1';
                SELECT COUNT(*) FROM studio.project_connections WHERE "ProjectId" = 'p1';
                """;
            var remainingConn = (long)(await deleteCmd.ExecuteScalarAsync())!;
            Assert.Equal(0, remainingConn);
        }
    }
}
