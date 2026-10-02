using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Namines.API.Controllers;
using Namines.Core.Enums;
using Namines.Core.Models.Auth;
using Namines.Infrastructure.Data;
using Xunit;

namespace Namines.Tests.Controllers;

/// <summary>
/// Onay sayısının CR açıldığı ana sabitlenmesi — docs/last-review M-1.
///
/// <b>Bulunan açık:</b> gereken onay sayısı her oyda CANLI ekip büyüklüğünden
/// yeniden hesaplanıyordu (<c>CountVotingMembersAsync</c>). Üye çıkarma yetkisi olan
/// bir Admin, kendi Destructive/Breaking CR'ını açtıktan sonra ekibi 1 kişiye
/// küçültüp — <c>EffectiveRequiredApprovals</c> o durumda 1 onaya ve
/// <c>RequiresDistinctFromAuthor</c> <c>false</c>'a düştüğü için — kendi yıkıcı
/// değişikliğini TEK BAŞINA onaylayabiliyordu. "İki farklı kişi onaylamalı" kuralı,
/// onu uygulayacak kişiler silinerek atlatılabiliyordu.
/// </summary>
public sealed class ApprovalCountPinningTests : IAsyncLifetime
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

    private AuthDbContext NewContext() => new(_options);

    /// <summary>
    /// Üç oy verebilen üyeli bir org (yazar Admin + iki Editor) ve yazarın açtığı
    /// bir Destructive CR. <paramref name="pinnedTeamSize"/> 0 verilirse alan
    /// eklenmeden önce açılmış ESKİ bir satır taklit edilir.
    /// </summary>
    private async Task<string> SeedAsync(int pinnedTeamSize)
    {
        await using var db = NewContext();

        foreach (var id in new[] { "author", "rev-1", "rev-2" })
            db.Users.Add(new ApplicationUser { Id = id, UserName = id });

        db.Organizations.Add(new Organization
        { Id = "org-1", Name = "org", IsPersonal = false, CreatedByUserId = "author" });

        db.OrganizationMembers.Add(new OrganizationMember
        { OrganizationId = "org-1", UserId = "author", Role = OrgRole.Admin });
        db.OrganizationMembers.Add(new OrganizationMember
        { OrganizationId = "org-1", UserId = "rev-1", Role = OrgRole.Editor });
        db.OrganizationMembers.Add(new OrganizationMember
        { OrganizationId = "org-1", UserId = "rev-2", Role = OrgRole.Editor });

        db.CloudProjects.Add(new CloudProject
        {
            Id = "proj-1", Name = "p", DbType = "PostgreSQL", OrganizationId = "org-1",
            SchemaJson = "{}", NodePositionsJson = "{}", UserId = "author",
        });
        await db.SaveChangesAsync();

        var branch = new Branch
        { ProjectId = "proj-1", Name = "main", IsDefault = true, CreatedByUserId = "author" };
        db.Branches.Add(branch);
        await db.SaveChangesAsync();

        var version = new SchemaVersion
        {
            ProjectId = "proj-1", BranchId = branch.Id, Version = 1,
            Checksum = "abc", SchemaJson = "{}", AuthorUserId = "author",
        };
        db.SchemaVersions.Add(version);
        await db.SaveChangesAsync();

        var cr = new ChangeRequest
        {
            ProjectId = "proj-1", BranchId = branch.Id, HeadVersionId = version.Id,
            Title = "drop everything",
            RiskLevel = RiskLevel.Destructive,
            ImpactReportJson = "{}",
            CreatedByUserId = "author",
            VotingMembersAtCreation = pinnedTeamSize,
        };
        db.ChangeRequests.Add(cr);
        await db.SaveChangesAsync();

        return cr.Id;
    }

    /// <summary>Ekibi yazar tek başına kalacak şekilde küçültür — saldırının kendisi.</summary>
    private async Task ShrinkTeamToAuthorAsync()
    {
        await using var db = NewContext();
        db.OrganizationMembers.RemoveRange(
            await db.OrganizationMembers.Where(m => m.UserId != "author").ToListAsync());
        await db.SaveChangesAsync();
    }

    private static ChangeRequestController NewController(AuthDbContext db, string userId) =>
        new(db, migrationService: null!, testRunner: null!, aiService: null!, quota: null!)
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

    private async Task<IActionResult> VoteAsync(string crId, string userId)
    {
        await using var db = NewContext();
        return await NewController(db, userId)
            .Decide(crId, new DecideChangeRequestRequest
            {
                Decision = ApprovalDecision.Approved,
                Comment = "lgtm",
            });
    }

    [Fact]
    public async Task Ekibi_kucultup_kendi_yikici_CRini_onaylamak_ARTIK_MUMKUN_DEGIL()
    {
        var crId = await SeedAsync(pinnedTeamSize: 3);
        await ShrinkTeamToAuthorAsync();

        var result = await VoteAsync(crId, "author");

        Assert.IsType<ForbidResult>(result);

        await using var verify = NewContext();
        var cr = await verify.ChangeRequests.SingleAsync();
        Assert.Equal(ChangeRequestStatus.PendingReview, cr.Status);
        Assert.Empty(await verify.ChangeRequestApprovals.ToListAsync());
    }

    [Fact]
    public async Task Baska_bir_uyenin_onayi_CALISMAYA_DEVAM_EDIYOR()
    {
        // Düzeltme meşru onayı kırmamalı.
        var crId = await SeedAsync(pinnedTeamSize: 3);

        var result = await VoteAsync(crId, "rev-1");

        Assert.IsNotType<ForbidResult>(result);

        await using var verify = NewContext();
        Assert.Single(await verify.ChangeRequestApprovals.ToListAsync());
    }

    [Fact]
    public async Task Ekip_BUYUDUYSE_canli_sayi_kazaniyor()
    {
        // Sabitlenen 2, canlı 3 → ideal kural (2 onay) uygulanabilir hâle geldi;
        // görmezden gelmek kuralı gereksiz yere gevşetirdi. Bir onay CR'ı
        // Approved'a ÇEVİRMEMELİ.
        var crId = await SeedAsync(pinnedTeamSize: 2);

        await VoteAsync(crId, "rev-1");

        await using var verify = NewContext();
        var cr = await verify.ChangeRequests.SingleAsync();
        Assert.Equal(ChangeRequestStatus.PendingReview, cr.Status);
    }

    [Fact]
    public async Task Sabitlenmemis_ESKI_CR_canli_sayiya_dusuyor()
    {
        // 0 = alan eklenmeden önce açılmış satır. Geriye dönük bir sayı UYDURMAK,
        // olmayan bir bilgiyi varmış gibi göstermek olurdu — eski davranış korunuyor.
        var crId = await SeedAsync(pinnedTeamSize: 0);
        await ShrinkTeamToAuthorAsync();

        var result = await VoteAsync(crId, "author");

        // Eski davranış: tek kişilik ekipte yazar kendi onayını verebiliyor.
        Assert.IsNotType<ForbidResult>(result);
    }
}
