using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Namines.Core.Models.Auth;
using Namines.Infrastructure.Data;
using Testcontainers.PostgreSql;

namespace Namines.Tests.Integration;

/// <summary>
/// Optimistic concurrency: <c>CloudProject.RowVersion</c> (REL-003a / B-32).
///
/// <b>GERÇEK PostgreSQL'e karşı ve bu zorunlu:</b> belirteç ayrı bir kolon
/// değil, PostgreSQL'in <c>xmin</c> sistem kolonuna eşleniyor. SQLite'ta
/// <c>xmin</c> YOKTUR, yani bellek içi bir sağlayıcıda bu testler ya derlenmez
/// ya da hiçbir şey kanıtlamaz — motorun kendi davranışını test ediyoruz.
///
/// <b>Neden ayrı bir kolon değil:</b> <c>xmin</c> her satırda zaten var ve her
/// güncellemede motor tarafından değişiyor. Kendi <c>byte[] RowVersion</c>
/// kolonumuzu eklemek, mevcut satırlar için backfill ve her güncellemede elle
/// artırma gerektirirdi — ikisi de atlanabilir adımlar, ve atlandığında koruma
/// SESSİZCE yok olurdu.
/// </summary>
[Collection("Docker")]
public class ProjectConcurrencyTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17-alpine").Build();
    private DbContextOptions<AuthDbContext> _options = null!;

    public async Task InitializeAsync()
    {
        if (!DockerAvailable.Value) return;
        await _container.StartAsync();

        _options = new DbContextOptionsBuilder<AuthDbContext>()
            .UseNpgsql(_container.GetConnectionString()).Options;

        await using var db = new AuthDbContext(_options);
        await db.Database.MigrateAsync();

        db.Users.Add(new ApplicationUser { Id = "u1", UserName = "u1" });
        db.CloudProjects.Add(NewProject("p1"));
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        if (DockerAvailable.Value) await _container.DisposeAsync();
    }

    private static CloudProject NewProject(string id) => new()
    {
        Id = id,
        Name = "Proje",
        DbType = "PostgreSQL",
        SchemaJson = "{}",
        NodePositionsJson = "{}",
        UserId = "u1",
    };

    private static CancellationToken Ct => CancellationToken.None;

    [RequiresDockerFact]
    public async Task Satir_okundugunda_RowVersion_dolu_geliyor()
    {
        await using var db = new AuthDbContext(_options);

        var project = await db.CloudProjects.SingleAsync(p => p.Id == "p1", Ct);

        Assert.NotEqual(0u, project.RowVersion);
    }

    /// <summary>
    /// Belirteç HER güncellemede değişmeli. Değişmezse çakışma tespiti
    /// tamamen anlamsız olur — istemci hep aynı değeri geri gönderir ve
    /// kontrol hiçbir zaman tetiklenmez.
    /// </summary>
    [RequiresDockerFact]
    public async Task Guncelleme_RowVersion_u_DEGISTIRIYOR()
    {
        uint before;
        await using (var db = new AuthDbContext(_options))
        {
            var p = await db.CloudProjects.SingleAsync(x => x.Id == "p1", Ct);
            before = p.RowVersion;
            p.Name = "Yeni ad";
            await db.SaveChangesAsync(Ct);
        }

        await using var check = new AuthDbContext(_options);
        var after = await check.CloudProjects.SingleAsync(x => x.Id == "p1", Ct);

        Assert.NotEqual(before, after.RowVersion);
    }

    /// <summary>
    /// Bulgunun tarif ettiği senaryo: iki kullanıcı aynı projeyi aynı anda
    /// düzenliyor. İkinci yazma, ELİNDEKİ ESKİ sürümle geldiğinde
    /// yakalanabilmeli — ki denetleyici 409 dönebilsin ve ilk kullanıcının
    /// çalışması sessizce kaybolmasın.
    /// </summary>
    [RequiresDockerFact]
    public async Task Eski_surumle_gelen_yazma_TESPIT_EDILIYOR()
    {
        // Kullanıcı A projeyi okuyor (elinde bu sürüm var).
        uint versionSeenByA;
        await using (var read = new AuthDbContext(_options))
            versionSeenByA = (await read.CloudProjects.SingleAsync(p => p.Id == "p1", Ct)).RowVersion;

        // Kullanıcı B araya girip kaydediyor.
        await using (var other = new AuthDbContext(_options))
        {
            var p = await other.CloudProjects.SingleAsync(x => x.Id == "p1", Ct);
            p.SchemaJson = "{\"tables\":[]}";
            await other.SaveChangesAsync(Ct);
        }

        // Şimdi A kaydetmeye çalışıyor. Denetleyicinin yaptığı karşılaştırma:
        // istemcinin gönderdiği sürüm != güncel sürüm  ->  409.
        await using var db = new AuthDbContext(_options);
        var current = await db.CloudProjects.SingleAsync(x => x.Id == "p1", Ct);

        Assert.NotEqual(versionSeenByA, current.RowVersion);
    }

    /// <summary>
    /// Araya kimse girmediyse aynı sürüm dönmeli — aksi hâlde kontrol her
    /// kaydetmede yanlış alarm verir ve kullanıcı hiç kaydedemez. Yanlış
    /// pozitif veren bir koruma, kapatılan bir korumadır.
    /// </summary>
    [RequiresDockerFact]
    public async Task Arada_degisiklik_yoksa_surum_AYNI_kaliyor()
    {
        await using var db = new AuthDbContext(_options);

        var first = (await db.CloudProjects.AsNoTracking().SingleAsync(p => p.Id == "p1", Ct)).RowVersion;
        var second = (await db.CloudProjects.AsNoTracking().SingleAsync(p => p.Id == "p1", Ct)).RowVersion;

        Assert.Equal(first, second);
    }

    /// <summary>
    /// <c>xmin</c> bir SİSTEM kolonu: migration bir kolon EKLEMEMELİ.
    /// Eklemiş olsaydı mevcut satırlar için bir backfill gerekirdi ve
    /// eşleme yanlış kurulmuş demek olurdu.
    /// </summary>
    [RequiresDockerFact]
    public async Task RowVersion_ayri_bir_kolon_OLARAK_eklenmemis()
    {
        await using var db = new AuthDbContext(_options);

        var columns = await db.Database
            .SqlQuery<string>($@"
                SELECT column_name FROM information_schema.columns
                WHERE table_name = 'CloudProjects'")
            .ToListAsync(Ct);

        Assert.DoesNotContain("RowVersion", columns);
        Assert.DoesNotContain("xmin", columns);   // sistem kolonu; katalogda listelenmez
    }
}
