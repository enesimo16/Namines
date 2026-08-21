using Microsoft.EntityFrameworkCore;
using Namines.Core.Analysis;
using Namines.Core.Models;
using Namines.Core.Models.Auth;
using Namines.Infrastructure.Data;
using Testcontainers.PostgreSql;

namespace Namines.Tests.Integration;

/// <summary>
/// Gateway API anahtarları ve tablo izinleri (new-phase/08-GATEWAY-API.md §4.3).
///
/// Buradaki testlerin çoğu tek bir cümleyi koruyor: <b>"hiçbir tablo varsayılan
/// olarak public değil"</b> (§1). Varsayılanı açık yapan bir regresyon, projeye
/// sonradan eklenen bir tabloyu — <c>password_resets</c> gibi — kimse istemeden
/// internete açar ve bu sessizce olur.
/// </summary>
[Collection("Docker")]
public class GatewayApiKeyTests : IAsyncLifetime
{
    // OrgAccessTests ile aynı desen: gerçek PostgreSQL. Bellek içi sağlayıcı unique
    // index ve FK davranışını uygulamaz, yani "proje başına tablo adı tekil" gibi
    // kurallar test edilmiş GÖRÜNÜP gerçekte doğrulanmamış olurdu.
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17-alpine").Build();
    private AuthDbContext _shared = null!;

    public async Task InitializeAsync()
    {
        if (!DockerAvailable.Value) return;
        await _container.StartAsync();

        _shared = new AuthDbContext(new DbContextOptionsBuilder<AuthDbContext>()
            .UseNpgsql(_container.GetConnectionString()).Options);
        await _shared.Database.MigrateAsync();

        await _shared.Users.AddAsync(new ApplicationUser { Id = "u1", UserName = "u1" });
        await _shared.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        if (_shared is not null) await _shared.DisposeAsync();
        await _container.DisposeAsync();
    }

    /// <summary>Her test kendi projesiyle çalışır — testler birbirinin verisini görmesin.</summary>
    private async Task<(AuthDbContext Context, string ProjectId)> SeededAsync()
    {
        var projectId = "p-" + Guid.NewGuid().ToString("N")[..8];
        _shared.CloudProjects.Add(new CloudProject
        {
            Id = projectId, Name = "test", UserId = "u1",
            DbType = "PostgreSQL", SchemaJson = "{}", NodePositionsJson = "{}",
        });
        await _shared.SaveChangesAsync();
        return (_shared, projectId);
    }

    private static async Task<(GatewayApiKey Key, string Raw)> AddKeyAsync(
        AuthDbContext context, string projectId, bool canWrite = false, DateTime? expiresAt = null)
    {
        var (entity, raw) = GatewayAccess.CreateKey(projectId, "test key", "u1", canWrite, expiresAt);
        context.GatewayApiKeys.Add(entity);
        await context.SaveChangesAsync();
        return (entity, raw);
    }

    // ── Anahtar üretimi ve saklanması ────────────────────────────────────────

    [RequiresDockerFact]
    public async Task The_raw_key_is_never_stored()
    {
        // Kontrol veritabanının bir yedeği sızsa bile müşterinin veritabanına
        // erişim vermemeli. Ham anahtarı saklamak tam olarak bunu verirdi.
        var (context, projectId) = await SeededAsync();
        var (entity, raw) = await AddKeyAsync(context, projectId);

        Assert.DoesNotContain(raw, entity.KeyHash);
        Assert.NotEqual(raw, entity.KeyHash);
        Assert.Equal(GatewayAccess.Hash(raw), entity.KeyHash);
    }

    [Fact]
    public void Generated_keys_are_unique_and_prefixed()
    {
        var seen = new HashSet<string>();
        for (var i = 0; i < 200; i++)
        {
            var (_, raw) = GatewayAccess.CreateKey("p1", "k", "u1", false, null);
            Assert.StartsWith(GatewayAccess.KeyPrefix, raw);
            Assert.True(seen.Add(raw));
        }
    }

    [RequiresDockerFact]
    public async Task A_valid_key_authenticates()
    {
        var (context, projectId) = await SeededAsync();
        var (entity, raw) = await AddKeyAsync(context, projectId);

        var resolved = await context.AuthenticateAsync(raw);
        Assert.NotNull(resolved);
        Assert.Equal(entity.Id, resolved!.Id);
    }

    [RequiresDockerTheory]
    [InlineData("")]
    [InlineData("not-a-key")]
    [InlineData("nmn_totally-wrong-value-here")]
    public async Task Invalid_keys_are_rejected(string candidate)
    {
        var (context, projectId) = await SeededAsync();
        await AddKeyAsync(context, projectId);

        Assert.Null(await context.AuthenticateAsync(candidate));
    }

    [RequiresDockerFact]
    public async Task A_revoked_key_stops_working_but_is_not_deleted()
    {
        // Silinseydi "bu anahtar ne zaman, kim tarafından iptal edildi" sorusu
        // cevapsız kalırdı.
        var (context, projectId) = await SeededAsync();
        var (entity, raw) = await AddKeyAsync(context, projectId);

        entity.RevokedAt = DateTime.UtcNow;
        await context.SaveChangesAsync();

        Assert.Null(await context.AuthenticateAsync(raw));
        // Kayıt duruyor: iptal silmek değil, işaretlemektir.
        Assert.NotNull(await context.GatewayApiKeys.FindAsync(entity.Id));
    }

    [RequiresDockerFact]
    public async Task An_expired_key_stops_working()
    {
        var (context, projectId) = await SeededAsync();
        var (_, raw) = await AddKeyAsync(context, projectId, expiresAt: DateTime.UtcNow.AddSeconds(-1));

        Assert.Null(await context.AuthenticateAsync(raw));
    }

    // ── ASIL KURAL: varsayılan kapalı ────────────────────────────────────────

    [RequiresDockerFact]
    public async Task A_table_with_no_permission_row_is_not_accessible()
    {
        // 08 §1. Bu testin kırılması, şemanın tamamının sessizce açılması demek.
        var (context, projectId) = await SeededAsync();
        var (key, _) = await AddKeyAsync(context, projectId, canWrite: true);

        Assert.False(await context.IsTableAllowedAsync(key, "users", forWrite: false));
        Assert.False(await context.IsTableAllowedAsync(key, "users", forWrite: true));
    }

    [RequiresDockerFact]
    public async Task Read_permission_does_not_grant_write()
    {
        var (context, projectId) = await SeededAsync();
        var (key, _) = await AddKeyAsync(context, projectId, canWrite: true);

        context.GatewayTablePermissions.Add(new GatewayTablePermission
        {
            ProjectId = projectId, TableName = "users", CanRead = true, CanWrite = false,
        });
        await context.SaveChangesAsync();

        Assert.True(await context.IsTableAllowedAsync(key, "users", forWrite: false));
        Assert.False(await context.IsTableAllowedAsync(key, "users", forWrite: true));
    }

    [RequiresDockerFact]
    public async Task A_read_only_key_cannot_write_even_to_a_writable_table()
    {
        // İki ayrı kapı: anahtarın yetkisi ve tablonun izni. Tablo yazmaya açık
        // olsa bile salt-okunur bir anahtar geçememeli.
        var (context, projectId) = await SeededAsync();
        var (key, _) = await AddKeyAsync(context, projectId, canWrite: false);

        context.GatewayTablePermissions.Add(new GatewayTablePermission
        {
            ProjectId = projectId, TableName = "users", CanRead = true, CanWrite = true,
        });
        await context.SaveChangesAsync();

        Assert.True(await context.IsTableAllowedAsync(key, "users", forWrite: false));
        Assert.False(await context.IsTableAllowedAsync(key, "users", forWrite: true));
    }

    [RequiresDockerFact]
    public async Task Permissions_do_not_leak_across_projects()
    {
        var (context, projectId) = await SeededAsync();
        var (_, otherProjectId) = await SeededAsync();

        var (key, _) = await AddKeyAsync(context, projectId);

        context.GatewayTablePermissions.Add(new GatewayTablePermission
        {
            ProjectId = otherProjectId, TableName = "users", CanRead = true,
        });
        await context.SaveChangesAsync();

        Assert.False(await context.IsTableAllowedAsync(key, "users", forWrite: false));
    }

    // ── OpenAPI ──────────────────────────────────────────────────────────────

    private static DatabaseSchema TwoTableSchema() => new()
    {
        Name = "app",
        Tables =
        {
            new SchemaTable
            {
                Id = "t1", Name = "users",
                Columns =
                {
                    new SchemaColumn { Id = "c1", Name = "id", Type = "INT", IsPK = true },
                    new SchemaColumn { Id = "c2", Name = "email", Type = "VARCHAR", Length = 255 },
                    new SchemaColumn { Id = "c3", Name = "note", Type = "TEXT", IsNullable = true },
                },
            },
            new SchemaTable
            {
                Id = "t2", Name = "password_resets",
                Columns = { new SchemaColumn { Id = "c4", Name = "token", Type = "VARCHAR", Length = 64 } },
            },
        },
    };

    [Fact]
    public void Openapi_documents_only_the_tables_the_key_can_reach()
    {
        // Belgede göstermek, izin kuralını belge üzerinden delerdi: şemanın
        // tamamı okunabilir hâle gelirdi.
        var allowed = new Dictionary<string, (bool, bool)> { ["users"] = (true, false) };
        var doc = GatewayOpenApiGenerator.Generate(TwoTableSchema(), allowed);

        var schemas = (Dictionary<string, object?>)((Dictionary<string, object?>)doc["components"]!)["schemas"]!;

        Assert.True(schemas.ContainsKey("users"));
        Assert.False(schemas.ContainsKey("password_resets"));
    }

    [Fact]
    public void Openapi_separates_integers_from_decimals()
    {
        // İkisini de "number" saymak, üretilen istemcilerde para alanlarını
        // kayan noktaya düşürürdü.
        var allowed = new Dictionary<string, (bool, bool)> { ["users"] = (true, false) };
        var doc = GatewayOpenApiGenerator.Generate(TwoTableSchema(), allowed);

        var users = (Dictionary<string, object?>)((Dictionary<string, object?>)
            ((Dictionary<string, object?>)doc["components"]!)["schemas"]!)["users"]!;
        var properties = (Dictionary<string, object?>)users["properties"]!;
        var id = (Dictionary<string, object?>)properties["id"]!;

        Assert.Equal("integer", id["type"]);
    }

    [Fact]
    public void Openapi_marks_nullable_columns_as_nullable()
    {
        var allowed = new Dictionary<string, (bool, bool)> { ["users"] = (true, false) };
        var doc = GatewayOpenApiGenerator.Generate(TwoTableSchema(), allowed);

        var users = (Dictionary<string, object?>)((Dictionary<string, object?>)
            ((Dictionary<string, object?>)doc["components"]!)["schemas"]!)["users"]!;
        var note = (Dictionary<string, object?>)((Dictionary<string, object?>)users["properties"]!)["note"]!;

        // Nullable ifade edilmezse üretilen istemci null geldiğinde doğrulama
        // hatası verir.
        var types = Assert.IsType<string[]>(note["type"]);
        Assert.Contains("null", types);
    }

    [Fact]
    public void Openapi_omits_write_paths_for_a_read_only_grant()
    {
        var allowed = new Dictionary<string, (bool, bool)> { ["users"] = (true, false) };
        var doc = GatewayOpenApiGenerator.Generate(TwoTableSchema(), allowed);
        var paths = (Dictionary<string, object?>)doc["paths"]!;

        Assert.True(paths.ContainsKey("/api/gateway/list"));
        Assert.False(paths.ContainsKey("/api/gateway/delete"));
    }

    [Fact]
    public void Openapi_declares_the_api_key_security_scheme()
    {
        var doc = GatewayOpenApiGenerator.Generate(
            TwoTableSchema(), new Dictionary<string, (bool, bool)> { ["users"] = (true, true) });

        var schemes = (Dictionary<string, object?>)((Dictionary<string, object?>)doc["components"]!)["securitySchemes"]!;
        var apiKey = (Dictionary<string, object?>)schemes["ApiKey"]!;

        Assert.Equal("X-Namines-Key", apiKey["name"]);
        Assert.Equal("header", apiKey["in"]);
    }
}
