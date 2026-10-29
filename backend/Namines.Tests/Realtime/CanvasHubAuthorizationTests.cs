using System.Security.Claims;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Namines.API.Hubs;
using Namines.Core.Models;
using Namines.Core.Models.Auth;
using Namines.Infrastructure.Data;
using Namines.Infrastructure.Realtime;
using Xunit;

namespace Namines.Tests.Realtime;

/// <summary>
/// <see cref="CanvasHub"/> — oda yetkilendirmesi.
///
/// <b>Bulunan açık:</b> hub yalnızca <c>roomId</c>'nin BİÇİMİNİ doğruluyordu. G17'den beri
/// <c>roomId</c> gerçek bir branch kimliği; bu artık her üyeye (Viewer dahil) verilen KALICI bir
/// kiracı kaynağı. Projeden çıkarılan biri elindeki kimlikle bağlanıp yayınları dinleyebiliyor
/// ve <c>UpdateSchema</c> ile kalan üyelerin kanvasına şema yazabiliyordu. Ayrıca
/// <c>MoveCursor</c>/<c>UpdateSchema</c> çağıranın o odaya KATILMIŞ olmasını bile aramıyordu.
/// </summary>
public sealed class CanvasHubAuthorizationTests : IAsyncLifetime
{
    private const string BranchId = "branch-1";
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

        foreach (var id in new[] { "owner", "editor", "viewer", "stranger" })
            db.Users.Add(new ApplicationUser { Id = id, UserName = id });
        db.Organizations.Add(new Organization { Id = OrgId, Name = "org", CreatedByUserId = "owner" });
        db.CloudProjects.Add(new CloudProject
        {
            Id = ProjectId, Name = "p", DbType = "PostgreSQL", SchemaJson = "{}", NodePositionsJson = "{}",
            UserId = "owner", OrganizationId = OrgId,
        });
        db.OrganizationMembers.AddRange(
            new OrganizationMember { OrganizationId = OrgId, UserId = "owner", Role = OrgRole.Owner },
            new OrganizationMember { OrganizationId = OrgId, UserId = "editor", Role = OrgRole.Editor },
            new OrganizationMember { OrganizationId = OrgId, UserId = "viewer", Role = OrgRole.Viewer });
        db.Branches.Add(new Branch { Id = BranchId, ProjectId = ProjectId, Name = "main", IsDefault = true, CreatedByUserId = "owner" });
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync() => await _connection.DisposeAsync();

    // ── sahte SignalR altyapısı (bu projede mock kütüphanesi yok) ─────────────

    private sealed class FakeCallerContext : HubCallerContext
    {
        private readonly ClaimsPrincipal _user;
        public FakeCallerContext(string connectionId, string? userId)
        {
            ConnectionId = connectionId;
            _user = userId is null
                ? new ClaimsPrincipal(new ClaimsIdentity())
                : new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, userId) }, "TestAuth"));
        }
        public override string ConnectionId { get; }
        public override string? UserIdentifier => null;
        public override ClaimsPrincipal? User => _user;
        public override IDictionary<object, object?> Items { get; } = new Dictionary<object, object?>();
        public override IFeatureCollection Features { get; } = new FeatureCollection();
        public override CancellationToken ConnectionAborted => CancellationToken.None;
        public override void Abort() { }
    }

    private sealed class RecordingGroups : IGroupManager
    {
        public HashSet<(string Connection, string Group)> Members { get; } = new();
        public Task AddToGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default)
        { Members.Add((connectionId, groupName)); return Task.CompletedTask; }
        public Task RemoveFromGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default)
        { Members.Remove((connectionId, groupName)); return Task.CompletedTask; }
    }

    private sealed class RecordingProxy : IClientProxy
    {
        public List<string> Methods { get; } = new();
        public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default)
        { Methods.Add(method); return Task.CompletedTask; }
    }

    private sealed class FakeClients : IHubCallerClients
    {
        public RecordingProxy Group { get; } = new();
        public IClientProxy OthersInGroup(string groupName) => Group;
        public IClientProxy Caller => Group;
        public IClientProxy Others => Group;
        public IClientProxy All => Group;
        public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => Group;
        public IClientProxy Client(string connectionId) => Group;
        public IClientProxy Clients(IReadOnlyList<string> connectionIds) => Group;
        public IClientProxy Group_(string groupName) => Group;
        IClientProxy IHubClients<IClientProxy>.Group(string groupName) => Group;
        public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => Group;
        public IClientProxy Groups(IReadOnlyList<string> groupNames) => Group;
        public IClientProxy User(string userId) => Group;
        public IClientProxy Users(IReadOnlyList<string> userIds) => Group;
    }

    private sealed record Harness(CanvasHub Hub, FakeCallerContext Context, RecordingGroups Groups, FakeClients Clients, AuthDbContext Db);

    private Harness NewHub(string? userId, string connectionId = "conn-1")
    {
        var db = new AuthDbContext(_options);
        var context = new FakeCallerContext(connectionId, userId);
        var groups = new RecordingGroups();
        var clients = new FakeClients();
        var hub = new CanvasHub(new InMemoryPresenceStore(), db, new LiveConnectionRegistry())
        {
            Context = context,
            Groups = groups,
            Clients = clients,
        };
        return new Harness(hub, context, groups, clients, db);
    }

    [Fact]
    public async Task A_non_member_cannot_join_a_branch_room()
    {
        var h = NewHub("stranger");

        await Assert.ThrowsAsync<HubException>(() => h.Hub.JoinRoom(BranchId, "x"));

        Assert.Empty(h.Groups.Members);
    }

    [Fact]
    public async Task An_anonymous_guest_cannot_join_a_branch_room()
    {
        var h = NewHub(userId: null);

        await Assert.ThrowsAsync<HubException>(() => h.Hub.JoinRoom(BranchId, "x"));

        Assert.Empty(h.Groups.Members);
    }

    [Fact]
    public async Task A_viewer_can_join_and_send_cursors_but_not_write_the_schema()
    {
        var h = NewHub("viewer");
        await h.Hub.JoinRoom(BranchId, "v");
        h.Clients.Group.Methods.Clear();

        await h.Hub.MoveCursor(BranchId, "v", 1, 2);
        Assert.Equal(new[] { "ReceiveCursor" }, h.Clients.Group.Methods);

        h.Clients.Group.Methods.Clear();
        await h.Hub.UpdateSchema(BranchId, new DatabaseSchema());
        Assert.Empty(h.Clients.Group.Methods);
    }

    [Fact]
    public async Task An_editor_can_write_the_schema()
    {
        var h = NewHub("editor");
        await h.Hub.JoinRoom(BranchId, "e");
        h.Clients.Group.Methods.Clear();

        await h.Hub.UpdateSchema(BranchId, new DatabaseSchema());

        Assert.Equal(new[] { "ReceiveSchema" }, h.Clients.Group.Methods);
    }

    [Fact]
    public async Task A_member_removed_after_joining_stops_receiving_on_the_open_connection()
    {
        var h = NewHub("editor");
        await h.Hub.JoinRoom(BranchId, "e");

        // Üyelik kaldırılıyor ve yetki önbelleği süresi doluyor (gerçekte 15 sn sonra).
        await using (var db = new AuthDbContext(_options))
        {
            db.OrganizationMembers.Remove(await db.OrganizationMembers.SingleAsync(m => m.UserId == "editor"));
            await db.SaveChangesAsync();
        }
        h.Context.Items["canvas.viewChecked"] = (DateTime.UtcNow.AddMinutes(-5), true);
        h.Context.Items["canvas.editChecked"] = (DateTime.UtcNow.AddMinutes(-5), true);
        h.Clients.Group.Methods.Clear();

        await h.Hub.UpdateSchema(BranchId, new DatabaseSchema());

        Assert.Empty(h.Clients.Group.Methods);
        Assert.DoesNotContain((h.Context.ConnectionId, BranchId), h.Groups.Members);
    }

    [Fact]
    public async Task Broadcasting_to_a_room_you_never_joined_is_ignored()
    {
        // Oda adını bilen herkes katılmadan yayın yapabiliyordu.
        var h = NewHub("editor");

        await h.Hub.UpdateSchema(BranchId, new DatabaseSchema());
        await h.Hub.MoveCursor(BranchId, "e", 1, 2);

        Assert.Empty(h.Clients.Group.Methods);
    }

    [Fact]
    public async Task Guest_capability_rooms_keep_working_for_anonymous_users()
    {
        // Guest erişimi TASARIM GEREĞİ: branch olmayan rastgele bir roomId'ye girişsiz katılım.
        var h = NewHub(userId: null);

        await h.Hub.JoinRoom("a-random-guest-room-id", "guest");
        h.Clients.Group.Methods.Clear();
        await h.Hub.UpdateSchema("a-random-guest-room-id", new DatabaseSchema());

        Assert.Equal(new[] { "ReceiveSchema" }, h.Clients.Group.Methods);
    }

    [Fact]
    public async Task Joining_a_guest_room_after_a_branch_room_drops_the_branch_authorization_state()
    {
        var h = NewHub("editor");
        await h.Hub.JoinRoom(BranchId, "e");

        await h.Hub.JoinRoom("a-random-guest-room-id", "e");

        Assert.False(h.Context.Items.ContainsKey("canvas.branchProjectId"));
    }

    // ── A-5: başarısız JoinRoom yetkiyi sıfırlamamalı ────────────────────────

    private async Task AddBranchAsync(string branchId, string projectId = ProjectId)
    {
        await using var db = new AuthDbContext(_options);
        db.Branches.Add(new Branch { Id = branchId, ProjectId = projectId, Name = branchId, CreatedByUserId = "owner" });
        await db.SaveChangesAsync();
    }

    private async Task AddPrivateProjectBranchAsync(string branchId)
    {
        await using var db = new AuthDbContext(_options);
        db.CloudProjects.Add(new CloudProject
        {
            Id = "proj-private", Name = "private", DbType = "PostgreSQL", SchemaJson = "{}", NodePositionsJson = "{}",
            UserId = "owner", OrganizationId = null,
        });
        db.Branches.Add(new Branch { Id = branchId, ProjectId = "proj-private", Name = "main", IsDefault = true, CreatedByUserId = "owner" });
        await db.SaveChangesAsync();
    }

    private static DatabaseSchema SchemaWithTable() => new() { Tables = { new SchemaTable { Name = "t" } } };

    [Fact]
    public async Task A_failed_join_keeps_the_previous_authorization_so_a_viewer_still_cannot_write()
    {
        await AddPrivateProjectBranchAsync("branch-private");
        var h = NewHub("viewer");
        await h.Hub.JoinRoom(BranchId, "v");

        await Assert.ThrowsAsync<HubException>(() => h.Hub.JoinRoom("branch-private", "v"));

        // Durum değişmedi: hâlâ eski odada, hâlâ Viewer.
        Assert.True(h.Context.Items.ContainsKey("canvas.branchProjectId"));
        Assert.Contains((h.Context.ConnectionId, BranchId), h.Groups.Members);
        h.Clients.Group.Methods.Clear();
        await h.Hub.UpdateSchema(BranchId, new DatabaseSchema());
        Assert.Empty(h.Clients.Group.Methods);
    }

    [Fact]
    public async Task Switching_rooms_leaves_the_previous_group()
    {
        await AddBranchAsync("branch-2");
        var h = NewHub("editor");
        await h.Hub.JoinRoom(BranchId, "e");

        await h.Hub.JoinRoom("branch-2", "e");

        Assert.DoesNotContain((h.Context.ConnectionId, BranchId), h.Groups.Members);
        Assert.Contains((h.Context.ConnectionId, "branch-2"), h.Groups.Members);
        // Eski odaya artık yayın yapılamaz.
        h.Clients.Group.Methods.Clear();
        await h.Hub.UpdateSchema(BranchId, new DatabaseSchema());
        Assert.Empty(h.Clients.Group.Methods);
    }

    [Fact]
    public async Task Authorization_fails_closed_when_the_room_state_is_missing()
    {
        var h = NewHub("viewer");
        await h.Hub.JoinRoom(BranchId, "v");
        // Branch anahtarı da guest işareti de yok → eskiden "guest sayılıp" izin veriliyordu.
        h.Context.Items.Remove("canvas.branchProjectId");
        h.Clients.Group.Methods.Clear();

        await h.Hub.MoveCursor(BranchId, "v", 1, 2);
        await h.Hub.UpdateSchema(BranchId, new DatabaseSchema());

        Assert.Empty(h.Clients.Group.Methods);
    }

    // ── B-16: oda şema önbelleği ─────────────────────────────────────────────

    [Fact]
    public async Task Branch_room_schema_is_replayed_to_a_late_joiner_and_evicted_when_the_last_member_leaves()
    {
        await AddBranchAsync("branch-cache");
        var editor = NewHub("editor", "conn-a");
        await editor.Hub.JoinRoom("branch-cache", "e");
        await editor.Hub.UpdateSchema("branch-cache", SchemaWithTable());

        var viewer = NewHub("viewer", "conn-b");
        await viewer.Hub.JoinRoom("branch-cache", "v");
        Assert.Contains("ReceiveSchema", viewer.Clients.Group.Methods);

        // Herkes ayrılınca önbellek düşer; sonradan gelen bayat şema almamalı.
        await editor.Hub.OnDisconnectedAsync(null);
        await viewer.Hub.OnDisconnectedAsync(null);
        var late = NewHub("owner", "conn-c");
        await late.Hub.JoinRoom("branch-cache", "o");
        Assert.DoesNotContain("ReceiveSchema", late.Clients.Group.Methods);
    }

    [Fact]
    public async Task Guest_room_schema_is_never_cached()
    {
        var first = NewHub(userId: null, "conn-g1");
        await first.Hub.JoinRoom("guest-cache-room", "g");
        await first.Hub.UpdateSchema("guest-cache-room", SchemaWithTable());

        var second = NewHub(userId: null, "conn-g2");
        await second.Hub.JoinRoom("guest-cache-room", "g2");

        Assert.DoesNotContain("ReceiveSchema", second.Clients.Group.Methods);
    }

    [Fact]
    public async Task A_room_prefixed_branch_id_is_still_authorized_as_a_branch_room()
    {
        // Frontend URL'de "room-<branchId>" kullanıyor; önek yüzünden yetki atlanmamalı.
        var stranger = NewHub("stranger");
        await Assert.ThrowsAsync<HubException>(() => stranger.Hub.JoinRoom("room-" + BranchId, "x"));
        Assert.Empty(stranger.Groups.Members);

        var anon = NewHub(userId: null);
        await Assert.ThrowsAsync<HubException>(() => anon.Hub.JoinRoom("room-" + BranchId, "x"));

        var viewer = NewHub("viewer");
        await viewer.Hub.JoinRoom("room-" + BranchId, "v");
        viewer.Clients.Group.Methods.Clear();
        await viewer.Hub.UpdateSchema("room-" + BranchId, new DatabaseSchema());
        Assert.Empty(viewer.Clients.Group.Methods);
    }
}
