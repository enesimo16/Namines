using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Namines.API.Controllers;
using Namines.Core.Models.Auth;
using Namines.Infrastructure.Data;
using Xunit;

namespace Namines.Tests.Controllers;

/// <summary>
/// <see cref="ProjectMemberController"/> — Owner katmanı.
///
/// <b>Bulunan açık:</b> üye yönetimi kapısı <c>CanManageMembersAsync</c> (Admin VEYA Owner)
/// idi ve hedef rolde hiçbir kısıt yoktu: bir Admin kendi rolünü Owner'a yükseltebiliyordu.
/// Desk raw SQL, Ground provision/delete ve Vault restore Owner'a özel olduğu için bu,
/// "Admin bile yetmez" diye korunan her şeye erişim demekti. Aynı kapı bir Owner'ı ekipten
/// çıkarmaya ve rolünü düşürmeye de izin veriyordu.
/// </summary>
public sealed class ProjectMemberOwnerEscalationTests : IAsyncLifetime
{
    private const string ProjectId = "proj-1";
    private const string OrgId = "org-1";

    private SqliteConnection _connection = null!;
    private DbContextOptions<AuthDbContext> _options = null!;

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        await _connection.OpenAsync();
        _options = new DbContextOptionsBuilder<AuthDbContext>().UseSqlite(_connection).Options;

        await using var db = new AuthDbContext(_options);
        await db.Database.EnsureCreatedAsync();

        foreach (var id in new[] { "owner", "owner2", "admin", "editor" })
            db.Users.Add(new ApplicationUser { Id = id, UserName = id });
        db.Organizations.Add(new Organization { Id = OrgId, Name = "org", CreatedByUserId = "owner" });
        db.CloudProjects.Add(new CloudProject
        {
            Id = ProjectId, Name = "p", DbType = "PostgreSQL", SchemaJson = "{}", NodePositionsJson = "{}",
            UserId = "owner", OrganizationId = OrgId,
        });
        db.OrganizationMembers.AddRange(
            new OrganizationMember { OrganizationId = OrgId, UserId = "owner", Role = OrgRole.Owner },
            new OrganizationMember { OrganizationId = OrgId, UserId = "owner2", Role = OrgRole.Owner },
            new OrganizationMember { OrganizationId = OrgId, UserId = "admin", Role = OrgRole.Admin },
            new OrganizationMember { OrganizationId = OrgId, UserId = "editor", Role = OrgRole.Editor });
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync() => await _connection.DisposeAsync();

    // ProjectId doluyken ResolveOrgIdAsync UserManager'a dokunmuyor.
    private ProjectMemberController NewController(AuthDbContext db, string userId) =>
        new(db, userManager: null!)
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

    private static int Status(IActionResult result) =>
        (result as ObjectResult)?.StatusCode ?? (result as StatusCodeResult)?.StatusCode ?? 200;

    private async Task<OrgRole> RoleOfAsync(string userId)
    {
        await using var db = new AuthDbContext(_options);
        return (await db.OrganizationMembers.SingleAsync(m => m.OrganizationId == OrgId && m.UserId == userId)).Role;
    }

    [Fact]
    public async Task An_admin_cannot_promote_themselves_to_owner()
    {
        await using var db = new AuthDbContext(_options);

        var result = await NewController(db, "admin").ChangeRole(ProjectId, "admin", new ChangeMemberRoleRequest(OrgRole.Owner), default);

        Assert.Equal(403, Status(result));
        Assert.Equal(OrgRole.Admin, await RoleOfAsync("admin"));
    }

    [Fact]
    public async Task An_admin_cannot_promote_someone_else_to_owner()
    {
        await using var db = new AuthDbContext(_options);

        var result = await NewController(db, "admin").ChangeRole(ProjectId, "editor", new ChangeMemberRoleRequest(OrgRole.Owner), default);

        Assert.Equal(403, Status(result));
        Assert.Equal(OrgRole.Editor, await RoleOfAsync("editor"));
    }

    [Fact]
    public async Task An_admin_cannot_demote_an_owner()
    {
        await using var db = new AuthDbContext(_options);

        var result = await NewController(db, "admin").ChangeRole(ProjectId, "owner2", new ChangeMemberRoleRequest(OrgRole.Viewer), default);

        Assert.Equal(403, Status(result));
        Assert.Equal(OrgRole.Owner, await RoleOfAsync("owner2"));
    }

    [Fact]
    public async Task An_admin_cannot_remove_an_owner_even_when_another_owner_exists()
    {
        await using var db = new AuthDbContext(_options);

        var result = await NewController(db, "admin").Remove(ProjectId, "owner2", default);

        Assert.Equal(403, Status(result));
        Assert.Equal(OrgRole.Owner, await RoleOfAsync("owner2"));
    }

    [Fact]
    public async Task An_admin_can_still_manage_non_owner_roles()
    {
        await using var db = new AuthDbContext(_options);

        var result = await NewController(db, "admin").ChangeRole(ProjectId, "editor", new ChangeMemberRoleRequest(OrgRole.Viewer), default);

        Assert.Equal(200, Status(result));
        Assert.Equal(OrgRole.Viewer, await RoleOfAsync("editor"));
    }

    [Fact]
    public async Task An_owner_can_promote_to_owner()
    {
        await using var db = new AuthDbContext(_options);

        var result = await NewController(db, "owner").ChangeRole(ProjectId, "admin", new ChangeMemberRoleRequest(OrgRole.Owner), default);

        Assert.Equal(200, Status(result));
        Assert.Equal(OrgRole.Owner, await RoleOfAsync("admin"));
    }

    [Fact]
    public async Task An_owner_can_remove_another_owner()
    {
        await using var db = new AuthDbContext(_options);

        var result = await NewController(db, "owner").Remove(ProjectId, "owner2", default);

        Assert.Equal(200, Status(result));
    }

    [Fact]
    public async Task An_undefined_role_value_is_rejected()
    {
        await using var db = new AuthDbContext(_options);

        var result = await NewController(db, "owner").ChangeRole(ProjectId, "editor", new ChangeMemberRoleRequest((OrgRole)99), default);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task An_admin_cannot_add_a_new_member_as_owner()
    {
        await using var db = new AuthDbContext(_options);

        var result = await NewController(db, "admin").Add(ProjectId, new AddMemberRequest("someone@example.com", OrgRole.Owner), default);

        Assert.Equal(403, Status(result));
    }
}
