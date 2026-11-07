using Namines.Identity.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Namines.Core.Models.Auth;
using Namines.Infrastructure.Data;

namespace Namines.Tests.TestSupport;

/// <summary>
/// Modül Application testleri için: gerçek yetki kuralı, SQLite üzerinde tüm modül şemaları, dört rolden
/// üye ve bir yabancı. Yetki testleri sahte değil, organizasyon üyeliğinden gelir.
/// </summary>
internal sealed class ModuleTestDb : IDisposable
{
    public const string Owner = "owner";
    public const string Admin = "admin";
    public const string Editor = "editor";
    public const string Viewer = "viewer";
    public const string Outsider = "outsider";
    public const string OrgId = "org-1";
    public const string ProjectId = "p1";
    public const string OtherProjectId = "p2";

    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    public AuthDbContext Db { get; }
    public ModuleKit Kit { get; }

    public ModuleTestDb()
    {
        _connection.Open();
        Db = new AuthDbContext(new DbContextOptionsBuilder<AuthDbContext>().UseSqlite(_connection).Options);
        Db.EnsureModulesCreated();
        Kit = TestModules.For(Db);

        foreach (var user in new[] { Owner, Admin, Editor, Viewer, Outsider })
            Db.Users.Add(new ApplicationUser { Id = user, UserName = user, NormalizedUserName = user.ToUpperInvariant(), Email = user + "@example.com" });

        Db.Organizations.Add(new Organization { Id = OrgId, Name = "Team" });
        Db.OrganizationMembers.AddRange(
            new OrganizationMember { OrganizationId = OrgId, UserId = Owner, Role = OrgRole.Owner },
            new OrganizationMember { OrganizationId = OrgId, UserId = Admin, Role = OrgRole.Admin },
            new OrganizationMember { OrganizationId = OrgId, UserId = Editor, Role = OrgRole.Editor },
            new OrganizationMember { OrganizationId = OrgId, UserId = Viewer, Role = OrgRole.Viewer });
        Db.SaveChanges();

        Kit.Studio.Projects.Add(Project(ProjectId, OrgId));
        Kit.Studio.Projects.Add(Project(OtherProjectId, OrgId));
        Kit.Studio.SaveChanges();
        Kit.Studio.ChangeTracker.Clear();
    }

    public static CloudProject Project(string id, string? org, string user = Owner) => new()
    {
        Id = id,
        Name = "Project " + id,
        DbType = "PostgreSQL",
        SchemaJson = """{"tables":[],"relations":[]}""",
        NodePositionsJson = "{}",
        UserId = user,
        OrganizationId = org,
    };

    public void Dispose()
    {
        Db.Dispose();
        _connection.Dispose();
    }
}
