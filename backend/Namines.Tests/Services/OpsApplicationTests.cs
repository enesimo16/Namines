using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Namines.BuildingBlocks.Security;
using Xunit;

namespace Namines.Tests.Services;

/// <summary>Ops Application servisleri: başarı, geçersiz girdi, yetkisiz aktör, tekrar, eşzamanlılık, saklama, maskeleme.</summary>
public sealed class OpsApplicationTests : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly ServiceProvider _sp;

    public OpsApplicationTests()
    {
        _connection.Open();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<OpsDbContext>(o => o.UseSqlite(_connection));
        services.AddScoped<Namines.Ops.Application.IOpsData>(sp => sp.GetRequiredService<OpsDbContext>());
        _sp = services.BuildServiceProvider();
        using var scope = _sp.CreateScope();
        scope.ServiceProvider.GetRequiredService<OpsDbContext>().Database.EnsureCreated();
    }

    public void Dispose()
    {
        _sp.Dispose();
        _connection.Dispose();
    }

    private OpsDbContext Db() => _sp.CreateScope().ServiceProvider.GetRequiredService<OpsDbContext>();

    private sealed class FakeAdmins : IPlatformAdminAuthority
    {
        private readonly string _adminId;
        public FakeAdmins(string adminId = "admin") => _adminId = adminId;
        public Task<bool> IsPlatformAdminAsync(string? userId, CancellationToken ct = default) => Task.FromResult(userId == _adminId);
    }

    private OpsAdminService Admin(OpsDbContext db)
    {
        var scopes = _sp.GetRequiredService<IServiceScopeFactory>();
        return new OpsAdminService(db, new FakeAdmins(),
            new FeatureFlagService(scopes, NullLogger<FeatureFlagService>.Instance),
            new AiRouteCatalog(scopes, NullLogger<AiRouteCatalog>.Instance));
    }

    // ── Geri bildirim ────────────────────────────────────────────────────────

    [Fact]
    public async Task Submit_normalizes_and_stores()
    {
        await using var db = Db();
        var ok = await new FeedbackService(db).SubmitAsync(new FeedbackSubmission("u1", "  a@b.c ", "weird", "  merhaba  "));
        Assert.True(ok);
        var row = await db.Feedbacks.SingleAsync();
        Assert.Equal(("u1", "a@b.c", "general", "merhaba"), (row.UserId, row.Email, row.Category, row.Message));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  x ")]
    public async Task Submit_rejects_short_or_missing_message(string? message)
    {
        await using var db = Db();
        Assert.False(await new FeedbackService(db).SubmitAsync(new FeedbackSubmission(null, null, "bug", message)));
        Assert.Equal(0, await db.Feedbacks.CountAsync());
    }

    [Fact]
    public async Task Submit_truncates_long_message_and_blank_email_becomes_null()
    {
        await using var db = Db();
        await new FeedbackService(db).SubmitAsync(new FeedbackSubmission(null, "   ", "bug", new string('m', 5000)));
        var row = await db.Feedbacks.SingleAsync();
        Assert.Equal(FeedbackService.MaxMessageLength, row.Message.Length);
        Assert.Null(row.Email);
    }

    [Fact]
    public async Task Submit_null_is_rejected()
    {
        await using var db = Db();
        await Assert.ThrowsAsync<ArgumentNullException>(() => new FeedbackService(db).SubmitAsync(null!));
    }

    [Fact]
    public async Task Same_text_twice_is_two_rows_not_an_error()
    {
        await using var db = Db();
        var service = new FeedbackService(db);
        var submission = new FeedbackSubmission("u1", null, "bug", "aynı metin");
        Assert.True(await service.SubmitAsync(submission));
        Assert.True(await service.SubmitAsync(submission));
        Assert.Equal(2, await db.Feedbacks.CountAsync());
    }

    [Fact]
    public async Task Anonymize_removes_identity_keeps_text_and_is_idempotent()
    {
        await using var db = Db();
        var service = new FeedbackService(db);
        await service.SubmitAsync(new FeedbackSubmission("u1", "a@b.c", "bug", "metin kalsın"));
        await service.SubmitAsync(new FeedbackSubmission("u2", "x@y.z", "bug", "başkasına dokunma"));

        Assert.Equal(1, await service.AnonymizeByUserAsync("u1"));
        Assert.Equal(0, await service.AnonymizeByUserAsync("u1"));

        var rows = await db.Feedbacks.AsNoTracking().OrderBy(f => f.Id).ToListAsync();
        Assert.Null(rows[0].UserId);
        Assert.Null(rows[0].Email);
        Assert.Equal("metin kalsın", rows[0].Message);
        Assert.Equal("u2", rows[1].UserId);
    }

    [Fact]
    public async Task Anonymize_requires_a_user()
    {
        await using var db = Db();
        await Assert.ThrowsAnyAsync<ArgumentException>(() => new FeedbackService(db).AnonymizeByUserAsync(" "));
    }

    // ── Yönetim: yetki Application katmanında ───────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-admin")]
    public async Task Admin_surface_rejects_non_admin(string? actor)
    {
        await using var db = Db();
        db.Feedbacks.Add(new Feedback { Message = "gizli", Email = "pii@example.com" });
        await db.SaveChangesAsync();
        var admin = Admin(db);

        await Assert.ThrowsAsync<OpsAdminRequiredException>(() => admin.CountFeedbacksAsync(actor));
        await Assert.ThrowsAsync<OpsAdminRequiredException>(() => admin.ListFeedbacksAsync(actor, null, 1, 10));
        await Assert.ThrowsAsync<OpsAdminRequiredException>(() => admin.ListFeedbacksForUserAsync(actor, "u", null, 5));
        await Assert.ThrowsAsync<OpsAdminRequiredException>(() => admin.ExportFeedbacksAsync(actor, 5));
        await Assert.ThrowsAsync<OpsAdminRequiredException>(() => admin.DeleteFeedbackAsync(actor, 1));
        await Assert.ThrowsAsync<OpsAdminRequiredException>(() => admin.ListFlagsAsync(actor));
        await Assert.ThrowsAsync<OpsAdminRequiredException>(() => admin.UpdateFlagAsync(actor, "canvas", false, "Hidden", null));
        await Assert.ThrowsAsync<OpsAdminRequiredException>(() => admin.ListRoutesAsync(actor));
        await Assert.ThrowsAsync<OpsAdminRequiredException>(() => admin.UpdateRouteAsync(actor, "ai_dba", "groq", "m", 0.1, null));

        Assert.Equal(1, await db.Feedbacks.CountAsync());
    }

    [Fact]
    public async Task Admin_lists_filters_pages_and_deletes()
    {
        await using var db = Db();
        for (var i = 0; i < 7; i++)
            db.Feedbacks.Add(new Feedback { Category = i % 2 == 0 ? "bug" : "idea", Message = "m" + i, CreatedAt = DateTime.UtcNow.AddMinutes(i) });
        await db.SaveChangesAsync();
        var admin = Admin(db);

        var page = await admin.ListFeedbacksAsync("admin", " BUG ", 1, 2);
        Assert.Equal(4, page.TotalCount);
        Assert.Equal(5, page.PageSize); // alt sınır
        Assert.All(page.Items, f => Assert.Equal("bug", f.Category));
        Assert.Equal("m6", page.Items[0].Message);

        var all = await admin.ListFeedbacksAsync("admin", "all", 0, 50);
        Assert.Equal(1, all.Page);
        Assert.Equal(7, all.TotalCount);

        var id = all.Items[0].Id;
        Assert.True(await admin.DeleteFeedbackAsync("admin", id));
        Assert.False(await admin.DeleteFeedbackAsync("admin", id));
    }

    [Fact]
    public async Task Admin_user_lookup_with_null_email_does_not_match_blank_rows()
    {
        await using var db = Db();
        db.Feedbacks.Add(new Feedback { UserId = null, Email = null, Message = "anonim" });
        db.Feedbacks.Add(new Feedback { UserId = "u1", Message = "kendi" });
        await db.SaveChangesAsync();

        var rows = await Admin(db).ListFeedbacksForUserAsync("admin", "u1", null, 10);
        Assert.Equal("kendi", Assert.Single(rows).Message);
    }

    // ── Bayraklar ve rotalar ─────────────────────────────────────────────────

    [Fact]
    public async Task Flag_update_is_persisted_and_visible_to_a_new_instance()
    {
        await using var db = Db();
        var updated = await Admin(db).UpdateFlagAsync("admin", "canvas", false, "Hidden", "bakım");
        Assert.NotNull(updated);
        Assert.False(updated!.IsEnabled);
        Assert.Equal("Hidden", updated.Mode);

        var fresh = new FeatureFlagService(_sp.GetRequiredService<IServiceScopeFactory>(), NullLogger<FeatureFlagService>.Instance);
        var flag = await fresh.GetFlagAsync("canvas");
        Assert.False(flag!.IsEnabled);
        Assert.False(fresh.IsFeatureEnabled("canvas", out var mode, out var notice));
        Assert.Equal("Hidden", mode);
        Assert.Equal("bakım", notice);
    }

    [Fact]
    public async Task Flag_update_unknown_key_returns_null_and_writes_nothing()
    {
        await using var db = Db();
        Assert.Null(await Admin(db).UpdateFlagAsync("admin", "typo_flag", false, "Hidden", null));
        Assert.False(await db.FeatureFlags.AnyAsync(f => f.FeatureKey == "typo_flag"));
    }

    [Fact]
    public async Task Flag_update_invalid_mode_is_rejected()
    {
        await using var db = Db();
        await Assert.ThrowsAsync<ArgumentException>(() => Admin(db).UpdateFlagAsync("admin", "canvas", false, "Sometimes", null));
    }

    [Fact]
    public async Task Unknown_flag_evaluates_as_enabled()
    {
        var service = new FeatureFlagService(_sp.GetRequiredService<IServiceScopeFactory>(), NullLogger<FeatureFlagService>.Instance);
        Assert.True(await Task.FromResult(service.IsFeatureEnabled("does_not_exist", out _, out var notice)));
        Assert.Equal(string.Empty, notice);
    }

    [Fact]
    public async Task Concurrent_flag_updates_leave_one_consistent_row()
    {
        var scopes = _sp.GetRequiredService<IServiceScopeFactory>();
        var service = new FeatureFlagService(scopes, NullLogger<FeatureFlagService>.Instance);
        await service.GetAllFlagsAsync();

        // SQLite tek bağlantı paylaşıyor: yarış gerçek PG testinde; burada son yazanın tek satır bıraktığını doğrula.
        await service.UpdateFlagAsync("canvas", false, "Hidden");
        await service.UpdateFlagAsync("canvas", true, "DisabledNotice");

        await using var db = Db();
        var row = await db.FeatureFlags.AsNoTracking().SingleAsync(f => f.FeatureKey == "canvas");
        Assert.True(row.IsEnabled);
        Assert.Equal("DisabledNotice", row.Mode);
    }

    [Fact]
    public async Task Route_update_clamps_and_lowercases_and_unknown_tool_falls_back()
    {
        var catalog = new AiRouteCatalog(_sp.GetRequiredService<IServiceScopeFactory>(), NullLogger<AiRouteCatalog>.Instance);
        await catalog.UpdateRouteAsync("ai_dba", " DeepSeek ", " model-x ", 5.0, " GROQ ");

        var route = await catalog.GetRouteAsync("ai_dba");
        Assert.Equal(("deepseek", "model-x", 1.0, "groq"), (route.Provider, route.ModelName, route.Temperature, route.FallbackProvider));

        var unknown = await catalog.GetRouteAsync("nope");
        Assert.Equal("groq", unknown.Provider);

        await using var db = Db();
        Assert.False(await db.AiToolRoutes.AnyAsync(r => r.ToolKey == "nope"));
    }

    // ── SQL denetimi: maskeleme ─────────────────────────────────────────────

    private static SqlExecutionRecord Record(string script, string userId = "u1", string? project = null) =>
        new(userId, project, "db.example", "app", "PostgreSQL", script, true, 3, false, null);

    [Fact]
    public async Task Audit_stores_hash_and_preview_never_full_script()
    {
        await using var db = Db();
        var script = "DROP TABLE x;" + new string('y', 2000);
        await new SqlExecutionAuditService(db).RecordAsync(Record(script));

        var row = await db.SqlExecutionAudits.SingleAsync();
        Assert.Equal(SqlExecutionAuditService.ScriptPreviewLength, row.ScriptPreview.Length);
        Assert.Equal(script.Length, row.ScriptLength);
        Assert.Equal(64, row.ScriptHash.Length);
        Assert.True(row.ContainsDestructiveKeyword);
    }

    [Theory]
    [InlineData("CREATE USER a PASSWORD 'hunter2';")]
    [InlineData("ALTER ROLE b WITH PASSWORD 'hun''ter2';")]
    [InlineData("create user c identified by \"hunter2\";")]
    [InlineData("SET api_key = 'hunter2';")]
    [InlineData("INSERT INTO s(token) VALUES (1); select token = 'hunter2'")]
    public void Preview_masks_secret_literals(string script)
    {
        var preview = SqlExecutionAuditService.BuildPreview(script);
        Assert.DoesNotContain("hunter2", preview);
        Assert.Contains("***", preview);
    }

    // Denetim m-7: anahtar kelimeden hemen sonra tırnak gelmeyen sırlar ve hata alanı.
    [Theory]
    [InlineData("SELECT dblink_connect('host=h password=hunter2 user=x');")]
    [InlineData("SELECT dblink_connect('hostaddr=10.0.0.1 PWD=hunter2');")]
    [InlineData("SELECT * FROM dblink('dbname=x sslpassword=hunter2;', 'select 1') AS t(a int);")]
    [InlineData("SET app.key = 'hunter2';")]
    [InlineData("set session my.secret_value to 'hunter2'")]
    [InlineData("select 1;\nSET LOCAL app.token = hunter2;")]
    [InlineData("SET \"app.key\" TO 'hun''ter2';")]
    public void Preview_masks_secrets_inside_literals_and_session_settings(string script)
    {
        var preview = SqlExecutionAuditService.BuildPreview(script);
        Assert.DoesNotContain("hunter2", preview);
        Assert.DoesNotContain("hun''ter2", preview);
        Assert.Contains("***", preview);
    }

    [Fact]
    public void Update_assignments_are_not_session_settings()
    {
        // UPDATE ... SET kolon atamasıdır; ayar maskesi veriyi ezmesin.
        const string script = "UPDATE t SET name = 'Ada' WHERE id = 1;";
        Assert.Equal(script, SqlExecutionAuditService.BuildPreview(script));
    }

    [Fact]
    public async Task Error_text_is_masked_like_the_preview()
    {
        await using var db = Db();
        var record = Record("SELECT dblink_connect('host=h password=hunter2')") with
        {
            Success = false,
            Error = "could not establish connection: password=hunter2 rejected; near \"SET app.key = 'hunter3'\"",
        };
        await new SqlExecutionAuditService(db).RecordAsync(record);

        var row = await db.SqlExecutionAudits.SingleAsync();
        Assert.DoesNotContain("hunter2", row.Error);
        Assert.DoesNotContain("hunter2", row.ScriptPreview);
        Assert.Contains("rejected", row.Error);
    }

    [Fact]
    public async Task Null_error_stays_null()
    {
        await using var db = Db();
        await new SqlExecutionAuditService(db).RecordAsync(Record("select 1"));
        Assert.Null((await db.SqlExecutionAudits.SingleAsync()).Error);
    }

    [Fact]
    public void Preview_keeps_ordinary_sql()
    {
        const string script = "ALTER TABLE t ADD COLUMN password_hash text;";
        Assert.Equal(script, SqlExecutionAuditService.BuildPreview(script));
    }

    [Theory]
    [InlineData(" ")]
    [InlineData("")]
    public async Task Audit_requires_a_user(string user)
    {
        await using var db = Db();
        await Assert.ThrowsAnyAsync<ArgumentException>(() => new SqlExecutionAuditService(db).RecordAsync(Record("select 1", user)));
    }

    [Fact]
    public async Task Audit_same_script_twice_shares_hash()
    {
        await using var db = Db();
        var service = new SqlExecutionAuditService(db);
        await service.RecordAsync(Record("select 1"));
        await service.RecordAsync(Record("select 1", project: "  "));
        var rows = await db.SqlExecutionAudits.AsNoTracking().ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.Equal(rows[0].ScriptHash, rows[1].ScriptHash);
        Assert.All(rows, r => Assert.Null(r.ProjectId));
    }

    // ── Saklama ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Retention_purges_only_old_rows_and_zero_days_disables()
    {
        await using var db = Db();
        var now = new DateTime(2026, 11, 5, 0, 0, 0, DateTimeKind.Utc);
        db.Feedbacks.Add(new Feedback { Message = "eski", CreatedAt = now.AddDays(-40) });
        db.Feedbacks.Add(new Feedback { Message = "yeni", CreatedAt = now.AddDays(-5) });
        db.SqlExecutionAudits.Add(NewAudit(now.AddDays(-40)));
        db.SqlExecutionAudits.Add(NewAudit(now.AddDays(-5)));
        await db.SaveChangesAsync();
        var retention = new OpsRetentionService(db);

        Assert.Equal(0, await retention.PurgeFeedbacksAsync(0, now, TimeSpan.Zero));
        Assert.Equal(0, await retention.PurgeSqlExecutionAuditsAsync(-1, now, TimeSpan.Zero));
        Assert.Equal(2, await db.Feedbacks.CountAsync());

        Assert.Equal(1, await retention.PurgeFeedbacksAsync(30, now, TimeSpan.Zero));
        Assert.Equal(1, await retention.PurgeSqlExecutionAuditsAsync(30, now, TimeSpan.Zero));
        Assert.Equal(0, await retention.PurgeFeedbacksAsync(30, now, TimeSpan.Zero));
    }

    private static SqlExecutionAudit NewAudit(DateTime createdAt) => new()
    {
        UserId = "u1", DbType = "PostgreSQL", ScriptHash = "h", ScriptPreview = "p", CreatedAt = createdAt,
    };
}
