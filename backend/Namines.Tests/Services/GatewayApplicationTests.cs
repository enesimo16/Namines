using System.Net;
using Microsoft.EntityFrameworkCore;
using Namines.Core.Models.Auth;
using Xunit;
using static Namines.Tests.TestSupport.ModuleTestDb;

namespace Namines.Tests.Services;

/// <summary>Gateway Application: anahtar yaşam döngüsü, yetki Application katmanında, izin, maske, denetim, kiracı sınırı.</summary>
public sealed class GatewayApplicationTests : IDisposable
{
    private readonly ModuleTestDb _t = new();
    public void Dispose() => _t.Dispose();

    private ModuleKit Kit => _t.Kit;
    private IGatewayKeyService Keys => Kit.GatewayKeys;
    private IGatewayAccess Access => Kit.GatewayAccess;

    private static CreateKeyCommand Key(string? name = "k", bool write = false, bool sql = false, bool export = false, DateTime? expires = null, string? origins = null, string? ips = null, int? rate = null) =>
        new(name, write, sql, export, expires, origins, ips, rate);

    // ── Oluşturma ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Create_returns_raw_key_once_and_stores_hash_only()
    {
        var created = (await Keys.CreateAsync(Admin, ProjectId, Key(write: true))).Value!;
        Assert.StartsWith(GatewayKeys.KeyPrefix, created.Key);

        var row = await Kit.Gateway.GatewayApiKeys.AsNoTracking().SingleAsync();
        Assert.NotEqual(created.Key, row.KeyHash);
        Assert.Equal(GatewayKeys.Hash(created.Key), row.KeyHash);
        Assert.Equal(created.Key[..GatewayKeys.PrefixLength], row.Prefix);

        var listed = Assert.Single((await Keys.ListAsync(Admin, ProjectId)).Value!);
        Assert.DoesNotContain(created.Key, listed.ToString());
        Assert.DoesNotContain(row.KeyHash, listed.ToString());
    }

    [Theory]
    [InlineData(Editor)]
    [InlineData(Viewer)]
    [InlineData(Outsider)]
    [InlineData("")]
    public async Task Key_management_is_enforced_in_the_service_not_only_the_controller(string actor)
    {
        Assert.Equal(ModuleOutcomeKind.NotFound, (await Keys.CreateAsync(actor, ProjectId, Key())).Kind);
        Assert.Equal(ModuleOutcomeKind.NotFound, (await Keys.ListAsync(actor, ProjectId)).Kind);
        Assert.Equal(ModuleOutcomeKind.NotFound, (await Keys.RevokeAsync(actor, ProjectId, "x")).Kind);
        Assert.Equal(ModuleOutcomeKind.NotFound, (await Keys.ListPermissionsAsync(actor, ProjectId)).Kind);
        Assert.Equal(ModuleOutcomeKind.NotFound, (await Keys.SetPermissionAsync(actor, ProjectId, new PermissionCommand("t", true, false, false, null))).Kind);
        Assert.Equal(0, await Kit.Gateway.GatewayApiKeys.CountAsync());
        Assert.Equal(0, await Kit.Gateway.GatewayTablePermissions.CountAsync());
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Raw_sql_and_export_need_owner(bool sql, bool export)
    {
        var outcome = await Keys.CreateAsync(Admin, ProjectId, Key(sql: sql, export: export));
        Assert.Equal(ModuleOutcomeKind.Forbidden, outcome.Kind);
        Assert.True((await Keys.CreateAsync(Owner, ProjectId, Key(sql: sql, export: export))).IsOk);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(" ")]
    public async Task Name_is_required(string? name) =>
        Assert.Equal(ModuleOutcomeKind.Invalid, (await Keys.CreateAsync(Admin, ProjectId, Key(name))).Kind);

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(100_000_000)]
    public async Task Rate_limit_must_be_positive_and_within_plan(int rate) =>
        Assert.Equal(ModuleOutcomeKind.Invalid, (await Keys.CreateAsync(Admin, ProjectId, Key(rate: rate))).Kind);

    [Fact]
    public async Task Missing_rate_limit_takes_the_plan_ceiling()
    {
        var created = (await Keys.CreateAsync(Admin, ProjectId, Key())).Value!;
        Assert.True(created.RateLimitPerMinute > 0);
    }

    [Theory]
    [InlineData("*", null)]
    [InlineData("https://a.com/x", null)]
    [InlineData(",,", null)]
    [InlineData(null, "nope")]
    [InlineData(null, "10.0.0.0/99")]
    public async Task Invalid_allowlist_creates_no_key(string? origins, string? ips)
    {
        Assert.Equal(ModuleOutcomeKind.Invalid, (await Keys.CreateAsync(Admin, ProjectId, Key(origins: origins, ips: ips))).Kind);
        Assert.Equal(0, await Kit.Gateway.GatewayApiKeys.CountAsync());
    }

    [Fact]
    public async Task Allowlists_normalize_and_echo()
    {
        var created = (await Keys.CreateAsync(Admin, ProjectId, Key(origins: " HTTPS://App.Example.com ,https://app.example.com", ips: "10.1.2.3/8"))).Value!;
        Assert.Equal(("https://app.example.com", "10.0.0.0/8"), (created.AllowedOrigins, created.AllowedIps));
    }

    // ── Doğrulama ────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("short")]
    [InlineData("xyz_00000000000000000000000000")]
    [InlineData("nmn_doesnotexist0000000000000000000")]
    public async Task Bad_raw_keys_do_not_authenticate(string? raw) =>
        Assert.Null(await Access.AuthenticateAsync(raw));

    [Fact]
    public async Task Prefix_collision_needs_full_hash()
    {
        var created = (await Keys.CreateAsync(Admin, ProjectId, Key())).Value!;
        var forged = created.Key[..GatewayKeys.PrefixLength] + "ffffffffffffffffffffffffffffff";
        Assert.Null(await Access.AuthenticateAsync(forged));
        Assert.NotNull(await Access.AuthenticateAsync(created.Key));
    }

    [Fact]
    public async Task Revoke_is_idempotent_keeps_first_time_and_blocks_use()
    {
        var created = (await Keys.CreateAsync(Admin, ProjectId, Key())).Value!;
        Assert.True((await Keys.RevokeAsync(Admin, ProjectId, created.Id)).IsOk);
        var first = (await Kit.Gateway.GatewayApiKeys.AsNoTracking().SingleAsync()).RevokedAt;
        await Task.Delay(5);
        Assert.True((await Keys.RevokeAsync(Admin, ProjectId, created.Id)).IsOk);
        Assert.Equal(first, (await Kit.Gateway.GatewayApiKeys.AsNoTracking().SingleAsync()).RevokedAt);
        Assert.Null(await Access.AuthenticateAsync(created.Key));
    }

    [Fact]
    public async Task Revoke_cannot_reach_another_projects_key()
    {
        var created = (await Keys.CreateAsync(Admin, ProjectId, Key())).Value!;
        Assert.Equal(ModuleOutcomeKind.NotFound, (await Keys.RevokeAsync(Admin, OtherProjectId, created.Id)).Kind);
        Assert.NotNull(await Access.AuthenticateAsync(created.Key));
    }

    [Fact]
    public async Task Expired_key_does_not_authenticate()
    {
        var created = (await Keys.CreateAsync(Admin, ProjectId, Key(expires: DateTime.UtcNow.AddMinutes(-1)))).Value!;
        Assert.Null(await Access.AuthenticateAsync(created.Key));
    }

    [Fact]
    public async Task Key_dies_with_creators_membership_and_owner_only_rights_die_on_demotion()
    {
        var plain = (await Keys.CreateAsync(Admin, ProjectId, Key())).Value!;
        var sql = (await Keys.CreateAsync(Owner, ProjectId, Key(sql: true))).Value!;

        var owner = await _t.Db.OrganizationMembers.SingleAsync(m => m.UserId == Owner);
        owner.Role = OrgRole.Admin;
        await _t.Db.SaveChangesAsync();
        Assert.Null(await Access.AuthenticateAsync(sql.Key));
        Assert.NotNull(await Access.AuthenticateAsync(plain.Key));

        _t.Db.OrganizationMembers.Remove(await _t.Db.OrganizationMembers.SingleAsync(m => m.UserId == Admin));
        await _t.Db.SaveChangesAsync();
        Assert.Null(await Access.AuthenticateAsync(plain.Key));
    }

    [Fact]
    public async Task Orphan_key_without_creator_is_refused()
    {
        var created = (await Keys.CreateAsync(Admin, ProjectId, Key())).Value!;
        await Kit.Gateway.GatewayApiKeys.ExecuteUpdateAsync(s => s.SetProperty(k => k.CreatedByUserId, (string?)null));
        Assert.Null(await Access.AuthenticateAsync(created.Key));
    }

    [Fact]
    public async Task Last_used_updates_at_most_once_a_minute()
    {
        var created = (await Keys.CreateAsync(Admin, ProjectId, Key())).Value!;
        var key = (await Access.AuthenticateAsync(created.Key))!;
        await Access.TouchLastUsedAsync(key);
        var first = (await Kit.Gateway.GatewayApiKeys.AsNoTracking().SingleAsync()).LastUsedAt;
        Assert.NotNull(first);
        await Task.Delay(5);
        await Access.TouchLastUsedAsync(key);
        Assert.Equal(first, (await Kit.Gateway.GatewayApiKeys.AsNoTracking().SingleAsync()).LastUsedAt);
    }

    // ── Rotasyon ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Download_key_rotation_revokes_previous_and_only_owner_may_do_it()
    {
        Assert.Equal(ModuleOutcomeKind.Forbidden, (await Keys.RotateDownloadKeyAsync(Admin, ProjectId)).Kind);

        var first = (await Keys.RotateDownloadKeyAsync(Owner, ProjectId)).Value!;
        Assert.NotNull(await Access.AuthenticateAsync(first.RawKey));
        var second = (await Keys.RotateDownloadKeyAsync(Owner, ProjectId)).Value!;

        Assert.Null(await Access.AuthenticateAsync(first.RawKey));
        Assert.NotNull(await Access.AuthenticateAsync(second.RawKey));
        Assert.Equal(1, await Kit.Gateway.GatewayApiKeys.CountAsync(k => k.RevokedAt == null));
    }

    // ── İzin ve maske ────────────────────────────────────────────────────────

    [Fact]
    public async Task Table_is_closed_by_default_and_write_implies_read()
    {
        var key = (await Access.AuthenticateAsync((await Keys.CreateAsync(Admin, ProjectId, Key(write: true))).Value!.Key))!;
        Assert.False(await Access.IsTableAllowedAsync(key, "t", forWrite: false));

        var set = (await Keys.SetPermissionAsync(Admin, ProjectId, new PermissionCommand("t", false, true, false, null))).Value!;
        Assert.True(set.CanRead);
        Assert.True(await Access.IsTableAllowedAsync(key, "t", forWrite: true));
    }

    [Fact]
    public async Task Write_key_needs_can_write_and_export_needs_read()
    {
        var readOnly = (await Access.AuthenticateAsync((await Keys.CreateAsync(Admin, ProjectId, Key())).Value!.Key))!;
        await Keys.SetPermissionAsync(Admin, ProjectId, new PermissionCommand("t", true, true, true, null));
        Assert.False(await Access.IsTableAllowedAsync(readOnly, "t", forWrite: true));
        Assert.False(await Access.IsTableExportAllowedAsync(readOnly, "t"));

        var exporter = (await Access.AuthenticateAsync((await Keys.CreateAsync(Owner, ProjectId, Key(export: true))).Value!.Key))!;
        Assert.True(await Access.IsTableExportAllowedAsync(exporter, "t"));
        Assert.False(await Access.IsTableExportAllowedAsync(exporter, "other"));

        // Okuma yoksa dışa aktarım da yok.
        await Keys.SetPermissionAsync(Admin, ProjectId, new PermissionCommand("x", false, false, true, null));
        Assert.Equal(1, await Kit.Gateway.GatewayTablePermissions.CountAsync());
    }

    [Fact]
    public async Task Permissions_are_project_scoped()
    {
        var key = (await Access.AuthenticateAsync((await Keys.CreateAsync(Admin, OtherProjectId, Key())).Value!.Key))!;
        await Keys.SetPermissionAsync(Admin, ProjectId, new PermissionCommand("t", true, false, false, "email"));
        Assert.False(await Access.IsTableAllowedAsync(key, "t", forWrite: false));
        Assert.Empty(await Access.MaskedColumnsAsync(OtherProjectId, "t"));
    }

    [Fact]
    public async Task Masks_dedupe_keep_order_and_update_in_place()
    {
        await Keys.SetPermissionAsync(Admin, ProjectId, new PermissionCommand("t", true, false, false, " email , phone,email "));
        Assert.Equal(new[] { "email", "phone" }, await Access.MaskedColumnsAsync(ProjectId, "t"));

        await Keys.SetPermissionAsync(Admin, ProjectId, new PermissionCommand("t", true, false, false, "phone, ssn"));
        Assert.Equal(new[] { "phone", "ssn" }, await Access.MaskedColumnsAsync(ProjectId, "t"));
        Assert.Equal(2, await Kit.Gateway.MaskedColumns.CountAsync());

        var view = Assert.Single((await Keys.ListPermissionsAsync(Admin, ProjectId)).Value!);
        Assert.Equal("phone,ssn", view.MaskedColumns);
    }

    [Fact]
    public async Task Removing_all_rights_deletes_row_and_masks_and_is_idempotent()
    {
        await Keys.SetPermissionAsync(Admin, ProjectId, new PermissionCommand("t", true, false, false, "email"));
        Assert.Null((await Keys.SetPermissionAsync(Admin, ProjectId, new PermissionCommand("t", false, false, false, null))).Value);
        Assert.Null((await Keys.SetPermissionAsync(Admin, ProjectId, new PermissionCommand("t", false, false, false, null))).Value);
        Assert.Equal(0, await Kit.Gateway.GatewayTablePermissions.CountAsync());
        Assert.Equal(0, await Kit.Gateway.MaskedColumns.CountAsync());
    }

    [Theory]
    [InlineData(null)]
    [InlineData(" ")]
    public async Task Permission_requires_table_name(string? table) =>
        Assert.Equal(ModuleOutcomeKind.Invalid, (await Keys.SetPermissionAsync(Admin, ProjectId, new PermissionCommand(table, true, false, false, null))).Kind);

    [Fact]
    public async Task Oversized_mask_column_is_rejected()
    {
        var name = new string('c', GatewayAllowlistRules.MaxMaskedColumnLength + 1);
        Assert.Equal(ModuleOutcomeKind.Invalid, (await Keys.SetPermissionAsync(Admin, ProjectId, new PermissionCommand("t", true, false, false, name))).Kind);
    }

    // ── Kaynak kısıtı ────────────────────────────────────────────────────────

    [Fact]
    public async Task Origin_and_ip_rules_are_loaded_with_the_key_and_enforced()
    {
        var created = (await Keys.CreateAsync(Admin, ProjectId, Key(origins: "https://a.com", ips: "10.0.0.0/8,2001:db8::/32"))).Value!;
        var key = (await Access.AuthenticateAsync(created.Key))!;

        Assert.True(GatewayKeyRestrictions.IsOriginAllowed(key, "https://A.com"));
        Assert.False(GatewayKeyRestrictions.IsOriginAllowed(key, "https://evil.com"));
        Assert.False(GatewayKeyRestrictions.IsOriginAllowed(key, null));
        Assert.True(GatewayKeyRestrictions.IsIpAllowed(key, IPAddress.Parse("10.2.3.4"), true, out _));
        Assert.True(GatewayKeyRestrictions.IsIpAllowed(key, IPAddress.Parse("2001:db8::1"), true, out _));
        Assert.False(GatewayKeyRestrictions.IsIpAllowed(key, IPAddress.Parse("192.168.0.1"), true, out var reason));
        Assert.DoesNotContain("192.168", reason);
        // Proxy güvenilir değilse kısıt uygulanamaz: reddedilir, geçirilmez.
        Assert.False(GatewayKeyRestrictions.IsIpAllowed(key, IPAddress.Parse("10.2.3.4"), false, out _));
    }

    // ── Hız sınırı ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Rate_limit_blocks_after_quota()
    {
        var key = new GatewayApiKey { Id = Guid.NewGuid().ToString(), RateLimitPerMinute = 2 };
        Assert.True(await GatewayRateLimiter.TryAcquireAsync(key));
        Assert.True(await GatewayRateLimiter.TryAcquireAsync(key));
        Assert.False(await GatewayRateLimiter.TryAcquireAsync(key));
    }

    // ── Bakım ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Member_revocation_scopes_project_and_privilege()
    {
        var plain = (await Keys.CreateAsync(Owner, ProjectId, Key())).Value!;
        var sql = (await Keys.CreateAsync(Owner, ProjectId, Key(sql: true))).Value!;
        var other = (await Keys.CreateAsync(Owner, OtherProjectId, Key(sql: true))).Value!;

        Assert.Equal(1, await Kit.GatewayMaintenance.RevokeMemberKeysAsync(ProjectId, Owner, privilegedOnly: true));
        Assert.Null(await Access.AuthenticateAsync(sql.Key));
        Assert.NotNull(await Access.AuthenticateAsync(plain.Key));
        Assert.NotNull(await Access.AuthenticateAsync(other.Key));

        Assert.Equal(1, await Kit.GatewayMaintenance.RevokeMemberKeysAsync(ProjectId, Owner, privilegedOnly: false));
        Assert.Null(await Access.AuthenticateAsync(plain.Key));
        Assert.Equal(1, await Kit.GatewayMaintenance.RevokeKeysByCreatorAsync(Owner));
        Assert.Equal(0, await Kit.GatewayMaintenance.RevokeKeysByCreatorAsync(Owner));
    }

    [Fact]
    public async Task Purge_removes_keys_permissions_children_but_keeps_audit_and_other_projects()
    {
        var doomed = (await Keys.CreateAsync(Admin, ProjectId, Key(origins: "https://a.com", ips: "10.0.0.0/8"))).Value!;
        var kept = (await Keys.CreateAsync(Admin, OtherProjectId, Key(origins: "https://b.com"))).Value!;
        await Keys.SetPermissionAsync(Admin, ProjectId, new PermissionCommand("t", true, false, false, "email"));
        await Keys.SetPermissionAsync(Admin, OtherProjectId, new PermissionCommand("t", true, false, false, "email"));
        await Kit.GatewayAudit.RecordAsync(null, Admin, GatewayWriteKind.Read, "t", null, new[] { "email" }, 1, true, default, ProjectId);

        await Kit.GatewayMaintenance.PurgeProjectAsync(ProjectId);

        Assert.Equal(new[] { kept.Id }, await Kit.Gateway.GatewayApiKeys.Select(k => k.Id).ToListAsync());
        Assert.Equal(1, await Kit.Gateway.AllowedOrigins.CountAsync());
        Assert.Equal(0, await Kit.Gateway.AllowedIps.CountAsync());
        Assert.Equal(1, await Kit.Gateway.GatewayTablePermissions.CountAsync());
        Assert.Equal(1, await Kit.Gateway.MaskedColumns.CountAsync());
        Assert.Equal(1, await Kit.Gateway.GatewayAuditEntries.CountAsync());
        Assert.NotEqual(doomed.Id, kept.Id);
        await Kit.GatewayMaintenance.PurgeProjectAsync(ProjectId);
    }

    [Fact]
    public async Task Seed_permission_does_not_overwrite_existing_row()
    {
        await Keys.SetPermissionAsync(Admin, ProjectId, new PermissionCommand("t", true, false, false, null));
        await Kit.GatewayMaintenance.SeedTablePermissionAsync(ProjectId, "t", true, true, new[] { "x" });
        Assert.False((await Kit.Gateway.GatewayTablePermissions.AsNoTracking().SingleAsync()).CanWrite);
    }

    // ── Denetim ──────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(Outsider, ModuleOutcomeKind.NotFound)]
    [InlineData(Viewer, ModuleOutcomeKind.Forbidden)]
    [InlineData(Editor, ModuleOutcomeKind.Forbidden)]
    public async Task Audit_read_requires_admin(string actor, ModuleOutcomeKind expected)
    {
        var query = new AuditQuery(null, null, null, null, null, 1, 10);
        Assert.Equal(expected, (await Kit.GatewayAudit.TrailAsync(actor, ProjectId, query)).Kind);
        Assert.Equal(expected, (await Kit.GatewayAudit.AnalyticsAsync(actor, ProjectId, DateTime.UtcNow.AddDays(-1), DateTime.UtcNow, "day")).Kind);
    }

    [Fact]
    public async Task Audit_keeps_column_names_only_filters_and_pages()
    {
        var audit = Kit.GatewayAudit;
        await audit.RecordAsync(null, Admin, GatewayWriteKind.Create, "t", "1", new[] { "a", "b" }, 1, true, default, ProjectId);
        await audit.RecordAsync(null, Admin, GatewayWriteKind.Delete, "u", "2", null, 0, false, default, ProjectId);
        await audit.RecordAsync(null, Admin, GatewayWriteKind.Create, "t", "3", null, 1, true, default, OtherProjectId);

        var all = (await audit.TrailAsync(Admin, ProjectId, new AuditQuery(null, null, null, null, null, 1, 10))).Value!;
        Assert.Equal(2, all.TotalCount);
        Assert.Contains(all.Entries, e => e.Columns == "a,b");

        var failed = (await audit.TrailAsync(Admin, ProjectId, new AuditQuery(null, null, new[] { GatewayWriteKind.Delete }, "u", false, 1, 10))).Value!;
        Assert.Equal("delete", Assert.Single(failed.Entries).Kind);

        var paged = (await audit.TrailAsync(Admin, ProjectId, new AuditQuery(null, null, null, null, null, 0, 99999))).Value!;
        Assert.Equal((0, 99999), (paged.Page, paged.PageSize));
        Assert.Equal(2, paged.Entries.Count);
    }

    [Fact]
    public async Task Audit_write_failure_never_reaches_caller_and_does_not_poison_context()
    {
        await Kit.Gateway.Database.ExecuteSqlRawAsync("ALTER TABLE \"GatewayAuditEntries\" RENAME TO \"Moved\"");
        await Kit.GatewayAudit.RecordAsync(null, Admin, GatewayWriteKind.Read, "t", null, null, 0, true, default, ProjectId);
        await Kit.Gateway.Database.ExecuteSqlRawAsync("ALTER TABLE \"Moved\" RENAME TO \"GatewayAuditEntries\"");

        await Kit.GatewayAudit.RecordAsync(null, Admin, GatewayWriteKind.Read, "t", null, null, 0, true, default, ProjectId);
        Assert.Equal(1, await Kit.Gateway.GatewayAuditEntries.CountAsync());
    }

    [Fact]
    public async Task Retention_purges_only_old_audit_rows()
    {
        var now = new DateTime(2026, 11, 5, 0, 0, 0, DateTimeKind.Utc);
        Kit.Gateway.GatewayAuditEntries.Add(new GatewayAuditEntry { ProjectId = ProjectId, Kind = GatewayWriteKind.Read, CreatedAt = now.AddDays(-40) });
        Kit.Gateway.GatewayAuditEntries.Add(new GatewayAuditEntry { ProjectId = ProjectId, Kind = GatewayWriteKind.Read, CreatedAt = now.AddDays(-5) });
        await Kit.Gateway.SaveChangesAsync();
        Assert.Equal(0, await Kit.GatewayMaintenance.PurgeAuditAsync(0, now, TimeSpan.Zero));
        Assert.Equal(1, await Kit.GatewayMaintenance.PurgeAuditAsync(30, now, TimeSpan.Zero));
    }
}
