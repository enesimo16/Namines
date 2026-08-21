using Microsoft.EntityFrameworkCore;
using Namines.Core.Models.Auth;
using Namines.Infrastructure.Data;
using Testcontainers.PostgreSql;

namespace Namines.Tests.Integration;

/// <summary>
/// 05 §6 RBAC — proje erişiminin organizasyon üyeliğine bağlanması.
///
/// Bu testlerin varlık sebebi somut bir hata: yetki sınırı `CloudProject.UserId`
/// olduğu sürece projeye ikinci bir kullanıcı eklenemiyordu, dolayısıyla
/// new-phase/29 §3'ün "Destructive/Breaking → 2 FARKLI kişi" kuralı
/// MATEMATİKSEL OLARAK uygulanamıyordu (sahibi kendi değişikliğini onaylayamaz,
/// başkası da CR'a erişemez → kalıcı kilit). Kural kodlanmış ve birim testleri
/// yeşildi; kaçmasının sebebi birim testlerin sahte kullanıcı ID'leriyle
/// çalışıp sahiplik katmanına hiç dokunmamasıydı.
/// </summary>
[Collection("Docker")]
public class OrgAccessTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container =
        new PostgreSqlBuilder("postgres:17-alpine").Build();

    private AuthDbContext _context = null!;
    private string _ownerId = null!;
    private string _editorId = null!;
    private string _viewerId = null!;
    private string _outsiderId = null!;
    private string _projectId = null!;
    private string _orgId = null!;

    public async Task InitializeAsync()
    {
        if (!DockerAvailable.Value) return;
        await _container.StartAsync();

        _context = new AuthDbContext(new DbContextOptionsBuilder<AuthDbContext>()
            .UseNpgsql(_container.GetConnectionString()).Options);
        await _context.Database.MigrateAsync();

        async Task<string> User(string name)
        {
            var id = Guid.NewGuid().ToString();
            await _context.Users.AddAsync(new ApplicationUser { Id = id, UserName = name });
            return id;
        }

        _ownerId = await User("owner");
        _editorId = await User("editor");
        _viewerId = await User("viewer");
        _outsiderId = await User("outsider");
        await _context.SaveChangesAsync();

        var org = new Organization { Name = "Acme", CreatedByUserId = _ownerId };
        await _context.Organizations.AddAsync(org);
        await _context.SaveChangesAsync();
        _orgId = org.Id;

        await _context.OrganizationMembers.AddRangeAsync(
            new OrganizationMember { OrganizationId = _orgId, UserId = _ownerId, Role = OrgRole.Owner },
            new OrganizationMember { OrganizationId = _orgId, UserId = _editorId, Role = OrgRole.Editor },
            new OrganizationMember { OrganizationId = _orgId, UserId = _viewerId, Role = OrgRole.Viewer });

        _projectId = Guid.NewGuid().ToString();
        await _context.CloudProjects.AddAsync(new CloudProject
        {
            Id = _projectId, Name = "P", DbType = "PostgreSQL",
            SchemaJson = "{}", NodePositionsJson = "{}",
            UserId = _ownerId, OrganizationId = _orgId
        });
        await _context.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        if (!DockerAvailable.Value) return;
        await _context.DisposeAsync();
        await _container.DisposeAsync();
    }

    [RequiresDockerFact]
    public async Task Every_member_can_view_but_outsider_cannot()
    {
        Assert.True(await _context.CanViewAsync(_projectId, _ownerId));
        Assert.True(await _context.CanViewAsync(_projectId, _editorId));
        Assert.True(await _context.CanViewAsync(_projectId, _viewerId));
        Assert.False(await _context.CanViewAsync(_projectId, _outsiderId));
    }

    [RequiresDockerFact]
    public async Task Viewer_cannot_edit_but_editor_and_owner_can()
    {
        Assert.True(await _context.CanEditAsync(_projectId, _ownerId));
        Assert.True(await _context.CanEditAsync(_projectId, _editorId));
        Assert.False(await _context.CanEditAsync(_projectId, _viewerId));   // 05 §6: viewer yorumlar, yazmaz
        Assert.False(await _context.CanEditAsync(_projectId, _outsiderId));
    }

    [RequiresDockerFact]
    public async Task Only_admin_and_owner_can_manage_members()
    {
        Assert.True(await _context.CanManageMembersAsync(_projectId, _ownerId));
        Assert.False(await _context.CanManageMembersAsync(_projectId, _editorId));
        Assert.False(await _context.CanManageMembersAsync(_projectId, _viewerId));
    }

    [RequiresDockerFact]
    public async Task Billing_role_grants_no_project_access()
    {
        // 05 §6'da `billing` sıralı bir seviye DEĞİL — yalnızca faturalama.
        // Sayısal olarak Owner'dan büyük olduğu için ">=" karşılaştırması
        // kullanılsaydı sessizce her yetkiyi alırdı; açıkça dışlanıyor.
        var billingId = Guid.NewGuid().ToString();
        await _context.Users.AddAsync(new ApplicationUser { Id = billingId, UserName = "billing" });
        await _context.OrganizationMembers.AddAsync(new OrganizationMember
        {
            OrganizationId = _orgId, UserId = billingId, Role = OrgRole.Billing
        });
        await _context.SaveChangesAsync();

        Assert.True(await _context.CanViewAsync(_projectId, billingId));   // üye, görebilir
        Assert.False(await _context.CanEditAsync(_projectId, billingId));  // ama yazamaz
        Assert.False(await _context.CanManageMembersAsync(_projectId, billingId));
    }

    [RequiresDockerFact]
    public async Task Legacy_project_without_org_falls_back_to_creator_ownership()
    {
        // Migration öncesinden kalmış satır: OrganizationId boş. Erişimi sessizce
        // kesmek yerine oluşturana Owner yetkisi verilir (veri kaybı riskine karşı).
        var legacyId = Guid.NewGuid().ToString();
        await _context.CloudProjects.AddAsync(new CloudProject
        {
            Id = legacyId, Name = "Legacy", DbType = "PostgreSQL",
            SchemaJson = "{}", NodePositionsJson = "{}",
            UserId = _ownerId, OrganizationId = null
        });
        await _context.SaveChangesAsync();

        Assert.Equal(OrgRole.Owner, await _context.GetRoleAsync(legacyId, _ownerId));
        Assert.Null(await _context.GetRoleAsync(legacyId, _editorId));
    }

    [RequiresDockerFact]
    public async Task Personal_org_creation_is_idempotent()
    {
        var a = await _context.GetOrCreatePersonalOrgAsync(_outsiderId, "outsider");
        var b = await _context.GetOrCreatePersonalOrgAsync(_outsiderId, "outsider");

        Assert.Equal(a.Id, b.Id);
        Assert.Equal(1, await _context.Organizations.CountAsync(o => o.IsPersonal && o.CreatedByUserId == _outsiderId));
    }

    [RequiresDockerFact]
    public async Task Removing_a_member_revokes_access_immediately()
    {
        Assert.True(await _context.CanEditAsync(_projectId, _editorId));

        var m = await _context.OrganizationMembers
            .FirstAsync(x => x.OrganizationId == _orgId && x.UserId == _editorId);
        _context.OrganizationMembers.Remove(m);
        await _context.SaveChangesAsync();

        Assert.False(await _context.CanViewAsync(_projectId, _editorId));
    }
}
