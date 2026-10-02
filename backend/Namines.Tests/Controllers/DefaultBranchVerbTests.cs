using System.Security.Claims;
using System.Threading.Tasks;
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
/// Varsayılan branch ucunun HTTP fiili — docs/last-review L-1.
///
/// <b>Bulunan açık:</b> uç bir <c>GET</c> olduğu hâlde satır OLUŞTURUYORDU, ve
/// <c>GET</c>'ler CSRF korumasından muaf — muaf olmaları doğru, çünkü <c>GET</c>
/// "güvenli" (yazmayan) olduğu VARSAYILAN bir fiil. Oluşturma idempotent ve
/// kullanıcının zaten sahip olduğu bir kaynak için olduğundan saldırganın
/// kazanacağı bir şey yoktu, ama "GET yazmaz" kuralının istisnası olmak, bir
/// sonraki değişikliğin o istisnayı büyütmesine zemin hazırlar.
///
/// Okuma <c>GET</c>'te kaldı (yoksa 404), oluşturma <c>POST</c>'a taşındı.
/// </summary>
public sealed class DefaultBranchVerbTests : IAsyncLifetime
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

        db.Users.Add(new ApplicationUser { Id = "owner-1", UserName = "owner-1" });
        db.CloudProjects.Add(new CloudProject
        {
            Id = "proj-1", Name = "p", DbType = "PostgreSQL",
            SchemaJson = "{}", NodePositionsJson = "{}", UserId = "owner-1",
        });
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync() => await _connection.DisposeAsync();

    private AuthDbContext NewContext() => new(_options);

    /// <summary>Bu iki uç yalnızca DbContext'e dokunuyor; diğer bağımlılıklar kullanılmıyor.</summary>
    private static BranchController NewController(AuthDbContext db, string userId) =>
        new(db, databases: null!)
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

    private async Task<int> BranchCountAsync()
    {
        await using var db = NewContext();
        return await db.Branches.CountAsync();
    }

    [Fact]
    public async Task GET_hicbir_sey_OLUSTURMUYOR()
    {
        // Asıl iddia: GET yazmıyor.
        await using var db = NewContext();
        var result = await NewController(db, "owner-1").GetDefaultBranch("proj-1");

        Assert.IsType<NotFoundObjectResult>(result);
        Assert.Equal(0, await BranchCountAsync());
    }

    [Fact]
    public async Task POST_yoksa_OLUSTURUYOR_ve_201_donuyor()
    {
        await using var db = NewContext();
        var result = await NewController(db, "owner-1").EnsureDefaultBranch("proj-1");

        var created = Assert.IsType<ObjectResult>(result);
        Assert.Equal(201, created.StatusCode);
        Assert.Equal(1, await BranchCountAsync());
    }

    [Fact]
    public async Task POST_varsa_200_donuyor_ve_IKINCI_branch_acmiyor()
    {
        await using (var db = NewContext())
            await NewController(db, "owner-1").EnsureDefaultBranch("proj-1");

        await using var second = NewContext();
        var result = await NewController(second, "owner-1").EnsureDefaultBranch("proj-1");

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal(1, await BranchCountAsync());
    }

    [Fact]
    public async Task POST_sonrasi_GET_branchi_BULUYOR()
    {
        await using (var db = NewContext())
            await NewController(db, "owner-1").EnsureDefaultBranch("proj-1");

        await using var second = NewContext();
        var result = await NewController(second, "owner-1").GetDefaultBranch("proj-1");

        Assert.IsType<OkObjectResult>(result);
    }

    [Theory]
    [InlineData("intruder")]
    public async Task Yabanci_kullanici_ikisinde_de_404_aliyor(string userId)
    {
        await using var db = NewContext();

        Assert.IsType<NotFoundObjectResult>(
            await NewController(db, userId).GetDefaultBranch("proj-1"));
        Assert.IsType<NotFoundObjectResult>(
            await NewController(db, userId).EnsureDefaultBranch("proj-1"));

        // Yetkisiz bir POST da hiçbir şey oluşturmamalı.
        Assert.Equal(0, await BranchCountAsync());
    }
}
