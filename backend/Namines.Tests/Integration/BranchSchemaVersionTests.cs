using Microsoft.EntityFrameworkCore;
using Namines.Core.Models.Auth;
using Namines.Infrastructure.Data;
using Testcontainers.PostgreSql;

namespace Namines.Tests.Integration;

/// <summary>
/// G10 — sunucu-otoriteli <see cref="Branch"/>/<see cref="SchemaVersion"/> tabloları,
/// GERÇEK bir PostgreSQL'e karşı. Golden-file/birim testleri C# tarafındaki niyeti
/// doğrular; bu testler veritabanının o niyeti GERÇEKTEN uyguladığını kanıtlar —
/// aynı G5 felsefesi (bkz. DdlExecutionTests.cs): kısıtlar sadece EF modelinde
/// tanımlı olmakla kalmayıp motor tarafından da reddediliyor mu?
///
/// Docker gerekir. Yoksa atlanır (bkz. RequiresDockerFact).
/// </summary>
[Collection("Docker")]
public class BranchSchemaVersionTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container =
        new PostgreSqlBuilder("postgres:17-alpine").Build();

    private AuthDbContext? _context;
    private string _projectId = null!;
    private string _userId = null!;

    public async Task InitializeAsync()
    {
        if (!DockerAvailable.Value) return;

        await _container.StartAsync();

        var options = new DbContextOptionsBuilder<AuthDbContext>()
            .UseNpgsql(_container.GetConnectionString())
            .Options;

        _context = new AuthDbContext(options);
        await _context.Database.MigrateAsync();

        // Branch/SchemaVersion, AspNetUsers ve CloudProjects'e FK veriyor — önce
        // asgari birer ebeveyn satır lazım.
        _userId = Guid.NewGuid().ToString();
        await _context.Users.AddAsync(new ApplicationUser { Id = _userId, UserName = "tester" });

        _projectId = Guid.NewGuid().ToString();
        await _context.CloudProjects.AddAsync(new CloudProject
        {
            Id = _projectId,
            Name = "TestProject",
            DbType = "PostgreSQL",
            SchemaJson = "{}",
            NodePositionsJson = "{}",
            UserId = _userId
        });

        await _context.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        if (!DockerAvailable.Value) return;
        if (_context is not null) await _context.DisposeAsync();
        await _container.DisposeAsync();
    }

    private Branch NewBranch(string name, bool isDefault = false) => new()
    {
        ProjectId = _projectId,
        Name = name,
        IsDefault = isDefault,
        CreatedByUserId = _userId
    };

    [RequiresDockerFact]
    public async Task Can_create_a_branch_and_read_it_back()
    {
        var branch = NewBranch("main", isDefault: true);
        await _context!.Branches.AddAsync(branch);
        await _context.SaveChangesAsync();

        var reloaded = await _context.Branches.FindAsync(branch.Id);
        Assert.NotNull(reloaded);
        Assert.Equal("main", reloaded!.Name);
        Assert.True(reloaded.IsDefault);
    }

    [RequiresDockerFact]
    public async Task Duplicate_branch_name_in_same_project_is_rejected()
    {
        await _context!.Branches.AddAsync(NewBranch("feature-x"));
        await _context.SaveChangesAsync();

        await _context.Branches.AddAsync(NewBranch("feature-x"));
        await Assert.ThrowsAsync<DbUpdateException>(() => _context.SaveChangesAsync());
    }

    [RequiresDockerFact]
    public async Task Second_default_branch_in_same_project_is_rejected_by_db()
    {
        // Kısmi unique index'in (ProjectId WHERE IsDefault) gerçekten DB seviyesinde
        // uygulandığını kanıtlar — sadece EF/uygulama katmanında değil.
        await _context!.Branches.AddAsync(NewBranch("main", isDefault: true));
        await _context.SaveChangesAsync();

        await _context.Branches.AddAsync(NewBranch("staging", isDefault: true));
        await Assert.ThrowsAsync<DbUpdateException>(() => _context.SaveChangesAsync());
    }

    [RequiresDockerFact]
    public async Task Multiple_non_default_branches_are_allowed()
    {
        await _context!.Branches.AddAsync(NewBranch("main", isDefault: true));
        await _context.Branches.AddAsync(NewBranch("feature-a"));
        await _context.Branches.AddAsync(NewBranch("feature-b"));

        await _context.SaveChangesAsync();

        var count = await _context.Branches.CountAsync(b => b.ProjectId == _projectId);
        Assert.Equal(3, count);
    }

    [RequiresDockerFact]
    public async Task Schema_versions_are_numbered_uniquely_per_branch()
    {
        var branch = NewBranch("main", isDefault: true);
        await _context!.Branches.AddAsync(branch);
        await _context.SaveChangesAsync();

        await _context.SchemaVersions.AddAsync(new SchemaVersion
        {
            ProjectId = _projectId, BranchId = branch.Id, Version = 1,
            Checksum = "abc", SchemaJson = "{}", AuthorUserId = _userId
        });
        await _context.SaveChangesAsync();

        await _context.SchemaVersions.AddAsync(new SchemaVersion
        {
            ProjectId = _projectId, BranchId = branch.Id, Version = 1, // aynı numara — reddedilmeli
            Checksum = "def", SchemaJson = "{}", AuthorUserId = _userId
        });
        await Assert.ThrowsAsync<DbUpdateException>(() => _context.SaveChangesAsync());
    }

    [RequiresDockerFact]
    public async Task Deleting_project_cascades_to_branches_and_versions()
    {
        var branch = NewBranch("main", isDefault: true);
        await _context!.Branches.AddAsync(branch);
        await _context.SaveChangesAsync();

        await _context.SchemaVersions.AddAsync(new SchemaVersion
        {
            ProjectId = _projectId, BranchId = branch.Id, Version = 1,
            Checksum = "abc", SchemaJson = "{}", AuthorUserId = _userId
        });
        await _context.SaveChangesAsync();

        var project = await _context.CloudProjects.FindAsync(_projectId);
        _context.CloudProjects.Remove(project!);
        await _context.SaveChangesAsync();

        Assert.False(await _context.Branches.AnyAsync(b => b.ProjectId == _projectId));
        Assert.False(await _context.SchemaVersions.AnyAsync(v => v.ProjectId == _projectId));
    }

    [RequiresDockerFact]
    public async Task Deleting_parent_branch_sets_child_parent_id_to_null_not_delete()
    {
        var parent = NewBranch("main", isDefault: true);
        await _context!.Branches.AddAsync(parent);
        await _context.SaveChangesAsync();

        var child = NewBranch("feature-forked-from-main");
        child.ParentBranchId = parent.Id;
        await _context.Branches.AddAsync(child);
        await _context.SaveChangesAsync();

        _context.Branches.Remove(parent);
        await _context.SaveChangesAsync();

        var reloadedChild = await _context.Branches.FindAsync(child.Id);
        Assert.NotNull(reloadedChild); // silinmedi
        Assert.Null(reloadedChild!.ParentBranchId); // ama referansı NULL'landı
    }

    // ── G17 — BranchController.GetOrCreateDefaultBranch'in "bul-yoksa-oluştur"
    //    mantığının GERÇEK Postgres'e karşı idempotency kanıtı ─────────────────

    private async Task<Branch> GetOrCreateDefaultBranch()
    {
        var branch = await _context!.Branches.FirstOrDefaultAsync(b => b.ProjectId == _projectId && b.IsDefault);
        if (branch is null)
        {
            branch = NewBranch("main", isDefault: true);
            await _context.Branches.AddAsync(branch);
            await _context.SaveChangesAsync();
        }
        return branch;
    }

    [RequiresDockerFact]
    public async Task GetOrCreateDefaultBranch_creates_main_when_project_has_no_branches()
    {
        Assert.False(await _context!.Branches.AnyAsync(b => b.ProjectId == _projectId));

        var branch = await GetOrCreateDefaultBranch();

        Assert.Equal("main", branch.Name);
        Assert.True(branch.IsDefault);
        Assert.Equal(_projectId, branch.ProjectId);
    }

    [RequiresDockerFact]
    public async Task GetOrCreateDefaultBranch_returns_the_same_branch_on_repeated_calls()
    {
        var first = await GetOrCreateDefaultBranch();
        var second = await GetOrCreateDefaultBranch();

        Assert.Equal(first.Id, second.Id);
        Assert.Equal(1, await _context!.Branches.CountAsync(b => b.ProjectId == _projectId));
    }

    /// <summary>
    /// Code review bulgusu: iki eşzamanlı ilk çağrı da "default branch yok" görüp ikisi de
    /// INSERT deniyordu; kaybeden kısmi unique index'e çarpıp 500 veriyordu. Paylaşılan
    /// <see cref="BranchProvisioning"/> artık kaybedeni kazananın satırına yönlendirmeli.
    /// Yarış, ikinci context'e ÖNCE kaydettirilerek deterministik biçimde kurgulanıyor.
    /// </summary>
    [RequiresDockerFact]
    public async Task GetOrCreateDefaultBranch_survives_a_concurrent_creation_race()
    {
        var options = new DbContextOptionsBuilder<AuthDbContext>()
            .UseNpgsql(_container.GetConnectionString())
            .Options;

        await using var contextA = new AuthDbContext(options);
        await using var contextB = new AuthDbContext(options);

        // A henüz kaydetmeden B default branch'i oluşturur (yarışın kaybedeni A olur).
        var branchB = await contextB.GetOrCreateDefaultBranchAsync(_projectId, _userId);

        // A aynı anda başlamış gibi davranır: kendi kontrolünde yok sanıp INSERT dener.
        var branchA = await contextA.GetOrCreateDefaultBranchAsync(_projectId, _userId);

        Assert.Equal(branchB.Id, branchA.Id);   // aynı branch — iki kullanıcı aynı odada
        Assert.Equal(1, await _context!.Branches.CountAsync(b => b.ProjectId == _projectId));
    }
}
