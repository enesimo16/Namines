using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Namines.Core.Models;
using Namines.Core.Models.Auth;
using Namines.Infrastructure.Data;

namespace Namines.Tests.Security;

/// <summary>
/// Toplu dışa aktarım AYRI bir izin (F-08 / B-44).
///
/// <b>Kapatılan açık:</b> <c>/export</c> ucu yalnızca OKUMA izniyle
/// korunuyordu, yani okuma yetkisi olan her API anahtarı tablonun TAMAMINI
/// tek istekte CSV/JSON olarak indirebiliyordu. Nicelik burada bir nitelik
/// farkı: "ekranda sayfa sayfa göster" ile "hepsini dosya olarak al" aynı
/// yetki değil. F-08 bunu "veri sızıntısının en sessiz yolu" diye
/// tanımlıyor — sessiz, çünkü hiçbir kural ihlal edilmiyor, hiçbir alarm
/// çalmıyor.
///
/// Bu testler İKİ kapının da gerektiğini kilitliyor: anahtarın kendi yetkisi
/// VE o tablonun izni. Biri düşerse test kırılır.
/// </summary>
public sealed class ExportPermissionTests : IAsyncLifetime
{
    private const string Project = "proj-1";
    private const string Table = "customers";

    private SqliteConnection _connection = null!;
    private DbContextOptions<AuthDbContext> _options = null!;

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        await _connection.OpenAsync();
        _options = new DbContextOptionsBuilder<AuthDbContext>().UseSqlite(_connection).Options;

        await using var db = new AuthDbContext(_options);
        await db.Database.EnsureCreatedAsync();

        // `GatewayTablePermission` -> `CloudProject` yabanci anahtari ZORUNLU;
        // SQLite bunu uyguluyor. Projeyi tohumlamadan izin satiri eklenemez.
        db.Users.Add(new ApplicationUser { Id = "u1", UserName = "u1" });
        foreach (var id in new[] { Project, "baska-proje" })
        {
            db.CloudProjects.Add(new CloudProject
            {
                Id = id,
                Name = id,
                DbType = "PostgreSQL",
                SchemaJson = "{}",
                NodePositionsJson = "{}",
                UserId = "u1",
            });
        }

        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync() => await _connection.DisposeAsync();

    private static CancellationToken Ct => CancellationToken.None;

    private async Task<AuthDbContext> SeedAsync(
        bool keyCanExport, bool tableCanRead, bool tableCanExport)
    {
        var db = new AuthDbContext(_options);

        db.GatewayTablePermissions.Add(new GatewayTablePermission
        {
            ProjectId = Project,
            TableName = Table,
            CanRead = tableCanRead,
            CanExport = tableCanExport,
        });

        await db.SaveChangesAsync(Ct);
        return db;
    }

    private static GatewayApiKey Key(bool canExport) => new()
    {
        Id = "key-1",
        ProjectId = Project,
        Name = "test",
        Prefix = "nk_test",
        KeyHash = "hash",
        CanExport = canExport,
    };

    // ── İki kapı ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Her_iki_izin_de_varsa_disa_aktarilabilir()
    {
        await using var db = await SeedAsync(keyCanExport: true, tableCanRead: true, tableCanExport: true);

        Assert.True(await db.IsTableExportAllowedAsync(Key(canExport: true), Table, Ct));
    }

    /// <summary>
    /// Asıl kapatılan açık: OKUMA izni tek başına YETMEZ.
    /// Bu test kırılırsa bulgu geri gelmiş demektir.
    /// </summary>
    [Fact]
    public async Task Yalnizca_OKUMA_izni_disa_aktarim_icin_YETMEZ()
    {
        await using var db = await SeedAsync(keyCanExport: true, tableCanRead: true, tableCanExport: false);

        Assert.False(await db.IsTableExportAllowedAsync(Key(canExport: true), Table, Ct));
    }

    [Fact]
    public async Task Anahtar_seviyesi_izin_yoksa_tablo_izni_YETMEZ()
    {
        await using var db = await SeedAsync(keyCanExport: false, tableCanRead: true, tableCanExport: true);

        Assert.False(await db.IsTableExportAllowedAsync(Key(canExport: false), Table, Ct));
    }

    /// <summary>
    /// Okunamayan bir tabloyu indirmek, okuma iznini anlamsız kılardı.
    /// <c>CanExport</c> okumayı ima ETMEZ, GEREKTİRİR.
    /// </summary>
    [Fact]
    public async Task Okuma_izni_olmadan_disa_aktarim_izni_calismaz()
    {
        await using var db = await SeedAsync(keyCanExport: true, tableCanRead: false, tableCanExport: true);

        Assert.False(await db.IsTableExportAllowedAsync(Key(canExport: true), Table, Ct));
    }

    /// <summary>
    /// İzin kaydının YOKLUĞU hayır demektir — deponun kendi ilkesi
    /// (08 §1: "hiçbir tablo varsayılan olarak public değil").
    /// </summary>
    [Fact]
    public async Task Izin_kaydi_hic_yoksa_disa_aktarilamaz()
    {
        await using var db = new AuthDbContext(_options);

        Assert.False(await db.IsTableExportAllowedAsync(Key(canExport: true), Table, Ct));
    }

    [Fact]
    public async Task Baska_tablonun_izni_bu_tabloyu_acmaz()
    {
        await using var db = await SeedAsync(keyCanExport: true, tableCanRead: true, tableCanExport: true);

        Assert.False(await db.IsTableExportAllowedAsync(Key(canExport: true), "orders", Ct));
    }

    /// <summary>
    /// Başka bir PROJENİN izin satırı sızmamalı. `ProjectId` koşulu düşerse
    /// bir anahtar, adı aynı olan başka bir projenin tablosunu indirebilirdi.
    /// </summary>
    [Fact]
    public async Task Baska_projenin_izni_bu_anahtari_acmaz()
    {
        await using var db = new AuthDbContext(_options);
        db.GatewayTablePermissions.Add(new GatewayTablePermission
        {
            ProjectId = "baska-proje",
            TableName = Table,
            CanRead = true,
            CanExport = true,
        });
        await db.SaveChangesAsync(Ct);

        Assert.False(await db.IsTableExportAllowedAsync(Key(canExport: true), Table, Ct));
    }

    /// <summary>
    /// Yeni bayrakların varsayılanı KAPALI olmalı. Varsayılanı açık yapmak
    /// izni dekoratif hâle getirirdi: bugün okuma izni olan her anahtar
    /// indirmeye devam eder ve bulgu hiç kapanmazdı.
    /// </summary>
    [Fact]
    public void Yeni_bayraklarin_varsayilani_KAPALI()
    {
        Assert.False(new GatewayApiKey().CanExport);
        Assert.False(new GatewayTablePermission().CanExport);
    }

    /// <summary>
    /// Okuma/yazma yolu bu değişiklikten ETKİLENMEMELİ. Yeni bir kapı
    /// eklerken var olanı bozmak, en kolay gözden kaçan gerileme.
    /// </summary>
    [Fact]
    public async Task Okuma_izni_disa_aktarim_bayragindan_ETKILENMEZ()
    {
        await using var db = await SeedAsync(keyCanExport: false, tableCanRead: true, tableCanExport: false);

        Assert.True(await db.IsTableAllowedAsync(Key(canExport: false), Table, forWrite: false, Ct));
    }
}
