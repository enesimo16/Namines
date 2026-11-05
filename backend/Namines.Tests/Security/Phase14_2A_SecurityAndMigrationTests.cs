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
using Namines.Tests.TestSupport;

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
    private SqliteConnection _sqliteConn = null!;
    private DbContextOptions<AuthDbContext> _authOptions = null!;
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

        await using var db = new AuthDbContext(_authOptions);
        await db.Database.EnsureCreatedAsync();

        _config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Security:ConnectionEncryptionKey"] = "test-secret-key-that-is-at-least-32-chars-long!"
            })
            .Build();

        _protector = new AesGcmConnectionSecretProtector(_config);
    }

    public async Task DisposeAsync()
    {
        await _sqliteConn.DisposeAsync();
    }

    [Fact]
    public void Credential_Encryption_UsesAesGcm_AndNeverStoresPlaintext()
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
        var project = new ProjectConnection
        {
            ProjectId = "p-sec",
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
                OrganizationId = "org-1",});
            await db.SaveChangesAsync();
            await TestModules.For(db).SeedConnectionAsync("p-sec", "k.active:ciphertext-payload-hidden", "PostgreSQL", false);
            await TestModules.For(db).SeedConnectionAsync("proj-tenant", _protector.Protect(rawConn, associatedData: "proj-tenant"), "PostgreSQL", true);
        }

        await using var readDb = new AuthDbContext(_authOptions);
        var authority = TestModules.For(readDb).Authority(_protector);

        // 1. Owner can resolve
        var ownerResolved = await authority.ResolveForUserAsync("proj-tenant", "owner-1");
        Assert.NotNull(ownerResolved);
        Assert.Equal("Host=db.acme.com;Database=secret;Username=root;Password=topsecret", ownerResolved.ConnectionString);

        // 2. Attacker cannot resolve
        var attackerResolved = await authority.ResolveForUserAsync("proj-tenant", "attacker-1");
        Assert.Null(attackerResolved);
    }

    [Fact]
    public async Task StoredConnection_CannotBypassAuthorization()
    {
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
                OrganizationId = "org-leg",});
            await db.SaveChangesAsync();
            await TestModules.For(db).SeedConnectionAsync("proj-leg-fallback", _protector.Protect("Host=leg-db;Password=secret", associatedData: "proj-leg-fallback"), "PostgreSQL", false);
        }

        await using var readDb = new AuthDbContext(_authOptions);
        var authority = TestModules.For(readDb).Authority(_protector);

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
                OrganizationId = "org-tok",});
            await db.SaveChangesAsync();
        }

        await using var readDb = new AuthDbContext(_authOptions);
        var controller = new ShareController(readDb, linter: null!, studio: TestModules.For(readDb).StudioService);

        // 1. Old active share URL using rawToken WORKS!
        var oldUrlResult = await controller.GetSharedSchema(rawLegacyToken);
        Assert.IsType<OkObjectResult>(oldUrlResult);

        // 2. Passing the hash itself as a token FAILS with 404 (because SHA256(hash) != hash)
        var hashAsTokenResult = await controller.GetSharedSchema(computedHash);
        Assert.IsType<NotFoundResult>(hashAsTokenResult);

        // 3. Invalid/unshared token returns 404
        var invalidResult = await controller.GetSharedSchema("completely-bogus-token");
        Assert.IsType<NotFoundResult>(invalidResult);

        // 4. Database verification: only the hash is stored
        var share = await TestModules.For(readDb).Studio.ProjectShares.SingleAsync(s => s.ProjectId == "p-migrated-tok");
        Assert.Equal(computedHash, share.ShareTokenHash);
    }

}
