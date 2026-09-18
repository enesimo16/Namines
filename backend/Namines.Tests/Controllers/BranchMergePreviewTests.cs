using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Namines.API.Controllers;
using Namines.Core.Models;
using Namines.Core.Models.Auth;
using Namines.Infrastructure.Data;

namespace Namines.Tests.Controllers;

/// <summary>
/// github/F4 — merge önizlemesi.
///
/// <b>Ortak ata veri modelinde ZATEN var</b> (<see cref="Branch.ParentBranchId"/> +
/// <see cref="Branch.ForkedFromVersion"/>); bu testler onun doğru çözüldüğünü ve
/// çözülemediğinde dürüstçe reddedildiğini kilitliyor — ortak atası olmayan iki
/// şemayı "3-yollu birleştirdik" diye sunmak, iki yollu diff'i yeni bir adla
/// satmak olurdu.
/// </summary>
public sealed class BranchMergePreviewTests : IAsyncLifetime
{
    private SqliteConnection _connection = null!;
    private DbContextOptions<AuthDbContext> _options = null!;

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        await _connection.OpenAsync();
        _options = new DbContextOptionsBuilder<AuthDbContext>().UseSqlite(_connection).Options;

        await using var db = new AuthDbContext(_options);
        await db.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync() => await _connection.DisposeAsync();

    private AuthDbContext NewContext() => new(_options);

    private static BranchController NewController(AuthDbContext db, string userId) =>
        new(db, new NoDatabases())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        new[] { new Claim(ClaimTypes.NameIdentifier, userId) }, "TestAuth")),
                },
            },
        };

    /// <summary>Merge önizlemesi veritabanı sağlamaya hiç dokunmaz; dokunursa test patlar.</summary>
    private sealed class NoDatabases : Core.Interfaces.IBranchDatabaseProvisioner
    {
        public Task<Core.Models.BranchDatabase> ProvisionAsync(string branchId, DatabaseSchema schema, Core.Enums.DatabaseType engine, CancellationToken ct = default) =>
            throw new NotSupportedException();
        public Task<Core.Models.BranchDatabase?> GetAsync(string branchId, CancellationToken ct = default) =>
            throw new NotSupportedException();
        public Task<IReadOnlyList<string>> ListOpenBranchIdsAsync(CancellationToken ct = default) =>
            throw new NotSupportedException();
        public Task DestroyAsync(string branchId, CancellationToken ct = default) =>
            throw new NotSupportedException();
        public Task<int> SeedAsync(string branchId, DatabaseSchema schema, int rowsPerTable, CancellationToken ct = default) =>
            throw new NotSupportedException();
        public Task<int> SweepExpiredAsync(CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    private static string SchemaJson(params (string Uuid, string Name, (string Uuid, string Name, string Type)[] Columns)[] tables)
    {
        var schema = new DatabaseSchema
        {
            Tables = tables.Select(t => new SchemaTable
            {
                Id = t.Uuid,
                StableUuid = t.Uuid,
                Name = t.Name,
                Columns = t.Columns.Select(c => new SchemaColumn
                {
                    Id = c.Uuid, StableUuid = c.Uuid, Name = c.Name, Type = c.Type,
                }).ToList(),
            }).ToList(),
        };

        return JsonSerializer.Serialize(schema);
    }

    private async Task<(string ProjectId, string MainId, string FeatureId)> SeedAsync(
        AuthDbContext db, string userId, string baseJson, string mainJson, string featureJson)
    {
        db.Users.Add(new ApplicationUser { Id = userId, UserName = userId });

        var projectId = "proj-1";
        db.CloudProjects.Add(new CloudProject
        {
            Id = projectId, Name = "p", DbType = "PostgreSQL",
            SchemaJson = "{}", NodePositionsJson = "{}", UserId = userId,
        });

        var main = new Branch { Id = "main-1", ProjectId = projectId, Name = "main", IsDefault = true, CreatedByUserId = userId };
        var feature = new Branch
        {
            Id = "feat-1", ProjectId = projectId, Name = "feature/status",
            ParentBranchId = main.Id, ForkedFromVersion = 1, CreatedByUserId = userId,
        };
        db.Branches.AddRange(main, feature);

        db.SchemaVersions.AddRange(
            new SchemaVersion { ProjectId = projectId, BranchId = main.Id, Version = 1, Checksum = "a", SchemaJson = baseJson },
            new SchemaVersion { ProjectId = projectId, BranchId = main.Id, Version = 2, Checksum = "b", SchemaJson = mainJson },
            new SchemaVersion { ProjectId = projectId, BranchId = feature.Id, Version = 1, Checksum = "c", SchemaJson = featureJson });

        await db.SaveChangesAsync();
        return (projectId, main.Id, feature.Id);
    }

    [Fact]
    public async Task Only_the_real_conflict_is_reported_and_the_rest_merges_itself()
    {
        // main kolonun tipini değiştirdi, feature yeni bir kolon ekledi:
        // ortak ata sayesinde ikisi de sorulmadan birleşmeli.
        var baseJson = SchemaJson(("t1", "users", new[] { ("c1", "email", "varchar") }));
        var mainJson = SchemaJson(("t1", "users", new[] { ("c1", "email", "text") }));
        var featureJson = SchemaJson(("t1", "users", new[] { ("c1", "email", "varchar"), ("c2", "phone", "varchar") }));

        await using var db = NewContext();
        var (_, _, featureId) = await SeedAsync(db, "u1", baseJson, mainJson, featureJson);

        var result = await NewController(db, "u1").MergePreview(featureId, default) as OkObjectResult;

        Assert.NotNull(result);
        var json = JsonSerializer.Serialize(result!.Value);
        Assert.Contains("\"conflicts\":[]", json);
        Assert.Contains("phone", json);      // feature'ın eklediği kolon birleşti
        Assert.Contains("\"baseVersion\":1", json);
    }

    [Fact]
    public async Task Two_branches_adding_the_same_column_name_is_reported()
    {
        // Kullanıcının anlattığı senaryo, uçtan uca.
        var baseJson = SchemaJson(("t1", "users", Array.Empty<(string, string, string)>()));
        var mainJson = SchemaJson(("t1", "users", new[] { ("c-main", "status", "varchar") }));
        var featureJson = SchemaJson(("t1", "users", new[] { ("c-feat", "status", "int") }));

        await using var db = NewContext();
        var (_, _, featureId) = await SeedAsync(db, "u1", baseJson, mainJson, featureJson);

        var result = await NewController(db, "u1").MergePreview(featureId, default) as OkObjectResult;

        var json = JsonSerializer.Serialize(result!.Value);
        Assert.Contains("NameCollision", json);
        Assert.Contains("status", json);
    }

    [Fact]
    public async Task A_branch_without_a_recorded_ancestor_is_refused_with_the_reason()
    {
        // Ata yoksa üç yollu birleştirme YAPILAMAZ. Sessizce iki yollu diff'e
        // düşmek, kullanıcıya olmayan bir güvence vermek olurdu.
        var json = SchemaJson(("t1", "users", Array.Empty<(string, string, string)>()));

        await using var db = NewContext();
        db.Users.Add(new ApplicationUser { Id = "u1", UserName = "u1" });
        db.CloudProjects.Add(new CloudProject
        {
            Id = "proj-1", Name = "p", DbType = "PostgreSQL",
            SchemaJson = "{}", NodePositionsJson = "{}", UserId = "u1",
        });
        db.Branches.Add(new Branch { Id = "root-1", ProjectId = "proj-1", Name = "main", IsDefault = true, CreatedByUserId = "u1" });
        db.SchemaVersions.Add(new SchemaVersion { ProjectId = "proj-1", BranchId = "root-1", Version = 1, Checksum = "a", SchemaJson = json });
        await db.SaveChangesAsync();

        var result = await NewController(db, "u1").MergePreview("root-1", default);

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Contains("ancestor", JsonSerializer.Serialize(bad.Value), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Another_users_branch_is_not_visible()
    {
        var baseJson = SchemaJson(("t1", "users", Array.Empty<(string, string, string)>()));

        await using var db = NewContext();
        var (_, _, featureId) = await SeedAsync(db, "owner", baseJson, baseJson, baseJson);
        db.Users.Add(new ApplicationUser { Id = "intruder", UserName = "intruder" });
        await db.SaveChangesAsync();

        var result = await NewController(db, "intruder").MergePreview(featureId, default);

        Assert.IsType<NotFoundObjectResult>(result);
    }
}
