using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Namines.API.Controllers;
using Namines.Core.Models.Auth;
using Namines.Infrastructure.Data;

namespace Namines.Tests.Security;

/// <summary>
/// DB-14/DB-34: anonim paylaşım uçları token'ı düz metinle değil SHA-256 özetiyle arıyor.
/// Postgres'teki geriye dönük doldurma (<c>encode(sha256(convert_to(...)),'hex')</c>) ile C#
/// özetinin AYNI olması şart; aksi halde migration'dan önce yayınlanmış linkler kırılırdı.
/// </summary>
public sealed class ShareTokenHashTests : IAsyncLifetime
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
        db.Users.Add(new ApplicationUser { Id = "u1", UserName = "u1" });
        db.CloudProjects.Add(new CloudProject
        {
            Id = "p1", Name = "Shared", DbType = "PostgreSQL", SchemaJson = "{\"tables\":[]}",
            NodePositionsJson = "{}", UserId = "u1", ShareToken = "tok-123",
        });
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync() => await _connection.DisposeAsync();

    [Fact]
    public void Ozet_bilinen_SHA256_vektoruyle_ayni()
    {
        // sha256("abc") — Postgres: encode(sha256('abc'::bytea),'hex')
        Assert.Equal(
            "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad",
            ShareTokenHasher.Hash("abc"));
    }

    [Fact]
    public async Task ShareToken_yazilinca_ozet_da_kaydedilir_ve_silinince_temizlenir()
    {
        await using (var db = new AuthDbContext(_options))
        {
            var p = await db.CloudProjects.SingleAsync();
            Assert.Equal(ShareTokenHasher.Hash("tok-123"), p.ShareTokenHash);

            p.ShareToken = null;
            await db.SaveChangesAsync();
        }

        await using var verify = new AuthDbContext(_options);
        var after = await verify.CloudProjects.SingleAsync();
        Assert.Null(after.ShareToken);
        Assert.Null(after.ShareTokenHash);
    }

    [Fact]
    public async Task Anonim_uclar_token_ile_ozet_uzerinden_buluyor()
    {
        await using var db = new AuthDbContext(_options);
        var controller = new ShareController(db, linter: null!, configuration: null!)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };

        Assert.IsType<OkObjectResult>(await controller.GetSharedSchema("tok-123"));
        Assert.IsType<NotFoundResult>(await controller.GetSharedSchema("tok-124"));
        // Özetin kendisi bir token değildir: özet ile arayan bulamamalı.
        Assert.IsType<NotFoundResult>(await controller.GetSharedSchema(ShareTokenHasher.Hash("tok-123")));
    }
}
