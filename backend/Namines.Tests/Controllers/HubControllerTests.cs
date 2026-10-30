using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Namines.API.Controllers;
using Namines.Core.Enums;
using Namines.Core.Models;
using Namines.Core.Models.Auth;
using Namines.Core.Models.Hub;
using Namines.Infrastructure.Data;
using Namines.Infrastructure.Services;
using Xunit;

namespace Namines.Tests.Controllers;

public sealed class HubControllerTests : IAsyncLifetime
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

    private static UserManager<ApplicationUser> NewUserManager(AuthDbContext db)
    {
        var store = new UserStore<ApplicationUser>(db);
        return new UserManager<ApplicationUser>(
            store,
            optionsAccessor: null!,
            passwordHasher: new PasswordHasher<ApplicationUser>(),
            userValidators: new List<IUserValidator<ApplicationUser>>(),
            passwordValidators: new List<IPasswordValidator<ApplicationUser>>(),
            keyNormalizer: new UpperInvariantLookupNormalizer(),
            errors: new IdentityErrorDescriber(),
            services: null!,
            logger: NullLogger<UserManager<ApplicationUser>>.Instance);
    }

    private static HubController CreateController(AuthDbContext db, string? userId = null, string? username = null)
    {
        var userManager = NewUserManager(db);
        var controller = new HubController(db, userManager, Namines.Tests.Services.ProjectDeletionTestKit.Create(db));

        var httpContext = new DefaultHttpContext();
        if (!string.IsNullOrEmpty(userId))
        {
            httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, userId),
                new Claim(ClaimTypes.Name, username ?? userId)
            }, "TestAuth"));
        }

        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
        return controller;
    }

    private static string BuildValidQualitySchemaJson()
    {
        var tUsers = new SchemaTable
        {
            Id = "t1",
            Name = "users",
            Columns = new List<SchemaColumn> { new() { Id = "c1", Name = "id", Type = "uuid", IsPK = true } }
        };
        var tProducts = new SchemaTable
        {
            Id = "t2",
            Name = "products",
            Columns = new List<SchemaColumn> { new() { Id = "c2", Name = "id", Type = "uuid", IsPK = true } }
        };
        var tOrders = new SchemaTable
        {
            Id = "t3",
            Name = "orders",
            Columns = new List<SchemaColumn>
            {
                new() { Id = "c3", Name = "id", Type = "uuid", IsPK = true },
                new() { Id = "c4", Name = "user_id", Type = "uuid", IsFK = true }
            }
        };
        var tOrderItems = new SchemaTable
        {
            Id = "t4",
            Name = "order_items",
            Columns = new List<SchemaColumn>
            {
                new() { Id = "c5", Name = "id", Type = "uuid", IsPK = true },
                new() { Id = "c6", Name = "order_id", Type = "uuid", IsFK = true }
            }
        };

        var schema = new DatabaseSchema
        {
            Name = "CommerceDb",
            Tables = new List<SchemaTable> { tUsers, tProducts, tOrders, tOrderItems },
            Relations = new List<SchemaRelation>
            {
                new() { Id = "r1", SourceTableId = "t3", SourceColumnId = "c4", TargetTableId = "t1", TargetColumnId = "c1" },
                new() { Id = "r2", SourceTableId = "t4", SourceColumnId = "c6", TargetTableId = "t3", TargetColumnId = "c3" }
            }
        };

        return JsonSerializer.Serialize(schema, SchemaJsonOptions.Default);
    }

    [Fact]
    public async Task Explore_ReturnsOnlyQualityGatedPublicRepos()
    {
        await using var db = new AuthDbContext(_options);
        var owner = new ApplicationUser { Id = "u1", UserName = "alice", Email = "alice@example.com" };
        db.Users.Add(owner);

        // Quality Gate passing public repo (>=4 tables, >=2 relations, non-empty description)
        var qualityRepo = new CloudProject
        {
            Id = "p-quality",
            Name = "Quality E-Commerce",
            Slug = "quality-ecommerce",
            DbType = "PostgreSQL",
            Category = "E-Commerce",
            Description = "Production-grade e-commerce schema in 3NF.",
            Visibility = ProjectVisibility.Public,
            SchemaJson = BuildValidQualitySchemaJson(),
            NodePositionsJson = "{}",
            UserId = owner.Id,
            CreatedAt = DateTime.UtcNow
        };

        // Failed Gate: only 1 table
        var simpleRepo = new CloudProject
        {
            Id = "p-simple",
            Name = "Simple Notes",
            Slug = "simple-notes",
            DbType = "PostgreSQL",
            Category = "General",
            Description = "Simple notes database.",
            Visibility = ProjectVisibility.Public,
            SchemaJson = JsonSerializer.Serialize(new DatabaseSchema
            {
                Name = "Simple",
                Tables = new List<SchemaTable> { new() { Id = "t-notes", Name = "notes", Columns = new List<SchemaColumn> { new() { Id = "c-id", Name = "id", Type = "int", IsPK = true } } } }
            }, SchemaJsonOptions.Default),
            NodePositionsJson = "{}",
            UserId = owner.Id,
            CreatedAt = DateTime.UtcNow
        };

        // Private repo
        var privateRepo = new CloudProject
        {
            Id = "p-private",
            Name = "Secret Enterprise",
            Slug = "secret-enterprise",
            DbType = "PostgreSQL",
            Category = "Enterprise",
            Description = "Secret internal database.",
            Visibility = ProjectVisibility.Private,
            SchemaJson = BuildValidQualitySchemaJson(),
            NodePositionsJson = "{}",
            UserId = owner.Id,
            CreatedAt = DateTime.UtcNow
        };

        db.CloudProjects.AddRange(qualityRepo, simpleRepo, privateRepo);
        await db.SaveChangesAsync();

        var controller = CreateController(db);
        var actionResult = await controller.Explore(q: null, category: null, dbType: null);

        var okResult = Assert.IsType<OkObjectResult>(actionResult);
        dynamic result = okResult.Value!;

        Assert.Equal(1, (int)result.Total);
        var items = (IEnumerable<dynamic>)result.Items;
        var first = Assert.Single(items);
        Assert.Equal("p-quality", (string)first.Id);
    }

    [Fact]
    public async Task GetRepo_AnonymousCanAccessPublic_RejectsPrivate()
    {
        await using var db = new AuthDbContext(_options);
        var owner = new ApplicationUser { Id = "u2", UserName = "bob", NormalizedUserName = "BOB" };
        db.Users.Add(owner);

        var pubRepo = new CloudProject
        {
            Id = "p-pub",
            Name = "Public Repo",
            Slug = "public-repo",
            DbType = "PostgreSQL",
            Visibility = ProjectVisibility.Public,
            SchemaJson = BuildValidQualitySchemaJson(),
            NodePositionsJson = "{}",
            UserId = owner.Id,
            CreatedAt = DateTime.UtcNow
        };

        var privRepo = new CloudProject
        {
            Id = "p-priv",
            Name = "Private Repo",
            Slug = "private-repo",
            DbType = "PostgreSQL",
            Visibility = ProjectVisibility.Private,
            SchemaJson = BuildValidQualitySchemaJson(),
            NodePositionsJson = "{}",
            UserId = owner.Id,
            CreatedAt = DateTime.UtcNow
        };

        db.CloudProjects.AddRange(pubRepo, privRepo);
        await db.SaveChangesAsync();

        var anonController = CreateController(db);

        // Public repo -> 200 OK
        var pubResult = await anonController.GetRepo("bob", "public-repo", CancellationToken.None);
        var ok = Assert.IsType<OkObjectResult>(pubResult);
        dynamic val = ok.Value!;
        Assert.Equal("p-pub", (string)val.Id);

        // Private repo -> 404 (401 ile 404 arasındaki fark "bu private repo var" bilgisini sızdırıyordu)
        var privResult = await anonController.GetRepo("bob", "private-repo", CancellationToken.None);
        Assert.IsType<NotFoundObjectResult>(privResult);
    }

    [Fact]
    public async Task ToggleStar_TogglesStarAndUpdatesCount()
    {
        await using var db = new AuthDbContext(_options);
        var owner = new ApplicationUser { Id = "u3", UserName = "carol" };
        var starrer = new ApplicationUser { Id = "u4", UserName = "dave" };
        db.Users.AddRange(owner, starrer);

        var repo = new CloudProject
        {
            Id = "p-star",
            Name = "Star Repo",
            Slug = "star-repo",
            DbType = "PostgreSQL",
            Visibility = ProjectVisibility.Public,
            SchemaJson = BuildValidQualitySchemaJson(),
            NodePositionsJson = "{}",
            UserId = owner.Id,
            StarCount = 0,
            CreatedAt = DateTime.UtcNow
        };
        db.CloudProjects.Add(repo);
        await db.SaveChangesAsync();

        var controller = CreateController(db, userId: starrer.Id, username: starrer.UserName);

        // 1st call: Star added
        var res1 = await controller.ToggleStar("p-star", CancellationToken.None);
        var ok1 = Assert.IsType<OkObjectResult>(res1);
        dynamic val1 = ok1.Value!;
        Assert.True((bool)val1.isStarred);
        Assert.Equal(1, (int)val1.starCount);

        // 2nd call: Star removed
        var res2 = await controller.ToggleStar("p-star", CancellationToken.None);
        var ok2 = Assert.IsType<OkObjectResult>(res2);
        dynamic val2 = ok2.Value!;
        Assert.False((bool)val2.isStarred);
        Assert.Equal(0, (int)val2.starCount);
    }

    [Fact]
    public async Task Fork_CreatesClonedProjectWithMainBranch_AndIncrementsParentForkCount()
    {
        await using var db = new AuthDbContext(_options);
        var owner = new ApplicationUser { Id = "u5", UserName = "eve" };
        var forker = new ApplicationUser { Id = "u6", UserName = "frank" };
        db.Users.AddRange(owner, forker);

        var repo = new CloudProject
        {
            Id = "p-original",
            Name = "Original Architecture",
            Slug = "original-arch",
            DbType = "PostgreSQL",
            Category = "FinTech",
            Tags = "finance,ledger",
            Visibility = ProjectVisibility.Public,
            SchemaJson = BuildValidQualitySchemaJson(),
            NodePositionsJson = "{}",
            UserId = owner.Id,
            ForkCount = 0,
            CreatedAt = DateTime.UtcNow
        };
        db.CloudProjects.Add(repo);
        await db.SaveChangesAsync();

        var controller = CreateController(db, userId: forker.Id, username: forker.UserName);

        var forkResult = await controller.Fork("p-original", new ForkRepoRequest("My Ledger Fork"), CancellationToken.None);
        var created = Assert.IsType<CreatedAtActionResult>(forkResult);
        dynamic val = created.Value!;

        Assert.Equal("My Ledger Fork", (string)val.Name);
        Assert.Equal("p-original", (string)val.ForkedFromProjectId);
        Assert.Equal(ProjectVisibility.Private, (ProjectVisibility)val.Visibility);

        // Sayaç SQL'de artırılıyor (ExecuteUpdate) — izlenen varlık bayat kalır, taze bağlamla oku.
        await using var verifyDb = new AuthDbContext(_options);
        var updatedParent = await verifyDb.CloudProjects.FindAsync("p-original");
        Assert.NotNull(updatedParent);
        Assert.Equal(1, updatedParent.ForkCount);

        // Verify default main branch was created for the fork
        var forkedId = (string)val.Id;
        var defaultBranch = await db.Branches.FirstOrDefaultAsync(b => b.ProjectId == forkedId && b.IsDefault);
        Assert.NotNull(defaultBranch);
        Assert.Equal("main", defaultBranch.Name);
        Assert.Equal(forker.Id, defaultBranch.CreatedByUserId);
    }

    [Fact]
    public async Task Issues_Crud_AutoIncrement_AndStateToggle()
    {
        await using var db = new AuthDbContext(_options);
        var user = new ApplicationUser { Id = "u7", UserName = "grace" };
        db.Users.Add(user);

        var repo = new CloudProject
        {
            Id = "p-issue",
            Name = "Issue Repo",
            Slug = "issue-repo",
            DbType = "PostgreSQL",
            Visibility = ProjectVisibility.Public,
            SchemaJson = BuildValidQualitySchemaJson(),
            NodePositionsJson = "{}",
            UserId = user.Id,
            CreatedAt = DateTime.UtcNow
        };
        db.CloudProjects.Add(repo);
        await db.SaveChangesAsync();

        var controller = CreateController(db, userId: user.Id, username: user.UserName);

        // Create Issue #1
        var res1 = await controller.CreateIssue("p-issue", new CreateIssueRequest("Missing index on orders", "orders.user_id should be indexed", "orders", "NSL005", "performance"), CancellationToken.None);
        var created1 = Assert.IsType<CreatedAtActionResult>(res1);
        dynamic val1 = created1.Value!;
        Assert.Equal(1, (int)val1.IssueNumber);
        var issueId1 = (string)val1.Id;

        // Create Issue #2
        var res2 = await controller.CreateIssue("p-issue", new CreateIssueRequest("Table naming convention", "Use snake_case", "order_items", null, "style"), CancellationToken.None);
        var created2 = Assert.IsType<CreatedAtActionResult>(res2);
        dynamic val2 = created2.Value!;
        Assert.Equal(2, (int)val2.IssueNumber);

        // List issues (should return 2 open)
        var listRes = await controller.ListIssues("p-issue", status: "open");
        var okList = Assert.IsType<OkObjectResult>(listRes);
        dynamic listVal = okList.Value!;
        Assert.Equal(2, (int)listVal.OpenCount);
        Assert.Equal(0, (int)listVal.ClosedCount);

        // Close Issue #1
        var patchRes = await controller.UpdateIssueState(issueId1, new UpdateIssueStateRequest(true), CancellationToken.None);
        var okPatch = Assert.IsType<OkObjectResult>(patchRes);
        dynamic patchVal = okPatch.Value!;
        Assert.True((bool)patchVal.IsClosed);
        Assert.NotNull(patchVal.ClosedAt);

        // Verify counts after closing
        var listRes2 = await controller.ListIssues("p-issue", status: "open");
        dynamic listVal2 = ((OkObjectResult)listRes2).Value!;
        Assert.Equal(1, (int)listVal2.OpenCount);
        Assert.Equal(1, (int)listVal2.ClosedCount);
    }

    [Fact]
    public async Task Comments_AddAndList_ReturnsInOrder()
    {
        await using var db = new AuthDbContext(_options);
        var user = new ApplicationUser { Id = "u8", UserName = "heidi" };
        db.Users.Add(user);

        var repo = new CloudProject
        {
            Id = "p-comm",
            Name = "Comment Repo",
            Slug = "comment-repo",
            DbType = "PostgreSQL",
            Visibility = ProjectVisibility.Public,
            SchemaJson = BuildValidQualitySchemaJson(),
            NodePositionsJson = "{}",
            UserId = user.Id,
            CreatedAt = DateTime.UtcNow
        };
        db.CloudProjects.Add(repo);

        var issue = new RepoIssue
        {
            Id = "issue-1",
            ProjectId = repo.Id,
            IssueNumber = 1,
            Title = "Bug in foreign key",
            CreatedByUserId = user.Id,
            CreatedAt = DateTime.UtcNow
        };
        db.RepoIssues.Add(issue);
        await db.SaveChangesAsync();

        var controller = CreateController(db, userId: user.Id, username: user.UserName);

        // Add Comment
        var addRes = await controller.CreateComment("issue-1", new CreateCommentRequest("I will fix this in branch hotfix-fk."), CancellationToken.None);
        var okAdd = Assert.IsType<OkObjectResult>(addRes);
        dynamic addVal = okAdd.Value!;
        Assert.Equal("I will fix this in branch hotfix-fk.", (string)addVal.Body);

        // List Comments
        var listRes = await controller.ListComments("issue-1", CancellationToken.None);
        var okList = Assert.IsType<OkObjectResult>(listRes);
        var comments = (IEnumerable<dynamic>)okList.Value!;
        var first = Assert.Single(comments);
        Assert.Equal("I will fix this in branch hotfix-fk.", (string)first.Body);
        Assert.Equal("heidi", (string)first.Author.Username);
    }

    [Fact]
    public async Task Badge_ReturnsValidSvgWithStatus()
    {
        await using var db = new AuthDbContext(_options);
        var owner = new ApplicationUser { Id = "u9", UserName = "ivan", NormalizedUserName = "IVAN" };
        db.Users.Add(owner);

        var repo = new CloudProject
        {
            Id = "p-badge",
            Name = "Verified Repo",
            Slug = "verified-repo",
            DbType = "PostgreSQL",
            Visibility = ProjectVisibility.Public,
            SchemaJson = BuildValidQualitySchemaJson(),
            NodePositionsJson = "{}",
            UserId = owner.Id,
            CreatedAt = DateTime.UtcNow
        };
        db.CloudProjects.Add(repo);
        await db.SaveChangesAsync();

        var controller = CreateController(db);
        var badgeResult = await controller.Badge("ivan", "verified-repo", CancellationToken.None);

        var content = Assert.IsType<ContentResult>(badgeResult);
        Assert.Equal("image/svg+xml; charset=utf-8", content.ContentType);
        Assert.Contains("<svg", content.Content);
        Assert.Contains("namines hub", content.Content);
        // "3NF verified" bir sezgiyi kanıt gibi sunuyordu.
        Assert.DoesNotContain("3NF verified", content.Content);
        Assert.Contains("no normalization warnings", content.Content);
    }
}
