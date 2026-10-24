using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Namines.API.Controllers;
using Namines.Core.Models;
using Namines.Core.Models.Auth;
using Namines.Infrastructure.Data;
using Xunit;

namespace Namines.Tests.Controllers;

/// <summary>
/// C-2 Güvenlik Testleri:
/// - TeamController.GetActivity çağrısının kullanıcının kişisel/bağlantısız projelerini
///   sessizce ekip organizasyonuna çalmadığının (exfiltration prevention) doğrulanması.
/// - Proje paylaşımının (ShareProject) ve geri çekilmesinin (UnshareProject)
///   yalnızca kullanıcının açık isteğiyle (opt-in) gerçekleştiğinin doğrulanması.
/// </summary>
public sealed class TeamControllerProjectSharingTests : IAsyncLifetime
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

    private static TeamController CreateController(AuthDbContext db, string userId)
    {
        var controller = new TeamController(db);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(new[]
                {
                    new Claim(ClaimTypes.NameIdentifier, userId),
                    new Claim(ClaimTypes.Name, "testuser")
                }, "TestAuth"))
            }
        };
        return controller;
    }

    [Fact]
    public async Task GetActivity_DoesNotStealPersonalProjects()
    {
        var userId = "user-team-owner";
        await using (var db = new AuthDbContext(_options))
        {
            var user = new ApplicationUser
            {
                Id = userId,
                UserName = "owner",
                Email = "owner@example.com",
                PlanCode = "team"
            };
            db.Users.Add(user);

            var personalOrg = new Organization
            {
                Id = "org-personal",
                Name = "Personal",
                IsPersonal = true,
                CreatedByUserId = userId,
                CreatedAt = DateTime.UtcNow
            };
            db.Organizations.Add(personalOrg);

            var teamOrg = new Organization
            {
                Id = "org-team-shared",
                Name = "Dev Team",
                IsPersonal = false,
                CreatedByUserId = userId,
                CreatedAt = DateTime.UtcNow.AddMinutes(5)
            };
            db.Organizations.Add(teamOrg);

            db.OrganizationMembers.Add(new OrganizationMember
            {
                OrganizationId = teamOrg.Id,
                UserId = userId,
                Role = OrgRole.Owner,
                JoinedAt = DateTime.UtcNow
            });

            // Kişisel proje (ekipte paylaşılmamış)
            db.CloudProjects.Add(new CloudProject
            {
                Id = "proj-private-1",
                Name = "Secret Personal Project",
                UserId = userId,
                OrganizationId = personalOrg.Id,
                DbType = "PostgreSQL",
                SchemaJson = "{}",
                NodePositionsJson = "[]",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });

            // Ekiple paylaşılan proje
            db.CloudProjects.Add(new CloudProject
            {
                Id = "proj-team-shared-1",
                Name = "Shared Team Project",
                UserId = userId,
                OrganizationId = teamOrg.Id,
                DbType = "PostgreSQL",
                SchemaJson = "{}",
                NodePositionsJson = "[]",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });

            await db.SaveChangesAsync();
        }

        // GetActivity çağrısı yap
        await using (var db = new AuthDbContext(_options))
        {
            var controller = CreateController(db, userId);
            var result = await controller.GetActivity(CancellationToken.None);

            var ok = Assert.IsType<OkObjectResult>(result);
            Assert.NotNull(ok.Value);
        }

        // Doğrulama: Kişisel projenin OrganizationId'si değişmemiş olmalı
        await using (var db = new AuthDbContext(_options))
        {
            var privateProj = await db.CloudProjects.FindAsync("proj-private-1");
            Assert.NotNull(privateProj);
            Assert.Equal("org-personal", privateProj.OrganizationId);
            Assert.NotEqual("org-team-shared", privateProj.OrganizationId);
        }
    }

    [Fact]
    public async Task ShareProject_And_UnshareProject_WorkExplicitly()
    {
        var userId = "user-team-owner-2";
        await using (var db = new AuthDbContext(_options))
        {
            var user = new ApplicationUser
            {
                Id = userId,
                UserName = "owner2",
                Email = "owner2@example.com",
                PlanCode = "team"
            };
            db.Users.Add(user);

            var personalOrg = new Organization
            {
                Id = "org-personal-2",
                Name = "Personal",
                IsPersonal = true,
                CreatedByUserId = userId,
                CreatedAt = DateTime.UtcNow
            };
            db.Organizations.Add(personalOrg);

            var teamOrg = new Organization
            {
                Id = "org-team-2",
                Name = "Dev Team 2",
                IsPersonal = false,
                CreatedByUserId = userId,
                CreatedAt = DateTime.UtcNow.AddMinutes(5)
            };
            db.Organizations.Add(teamOrg);

            db.OrganizationMembers.Add(new OrganizationMember
            {
                OrganizationId = teamOrg.Id,
                UserId = userId,
                Role = OrgRole.Owner,
                JoinedAt = DateTime.UtcNow
            });

            db.CloudProjects.Add(new CloudProject
            {
                Id = "proj-toggle-1",
                Name = "Toggle Project",
                UserId = userId,
                OrganizationId = personalOrg.Id,
                DbType = "PostgreSQL",
                SchemaJson = "{}",
                NodePositionsJson = "[]",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });

            await db.SaveChangesAsync();
        }

        // 1. ShareProject çağrısı
        await using (var db = new AuthDbContext(_options))
        {
            var controller = CreateController(db, userId);
            var result = await controller.ShareProject("proj-toggle-1", CancellationToken.None);
            Assert.IsType<OkObjectResult>(result);
        }

        await using (var db = new AuthDbContext(_options))
        {
            var proj = await db.CloudProjects.FindAsync("proj-toggle-1");
            Assert.NotNull(proj);
            Assert.Equal("org-team-2", proj.OrganizationId);
        }

        // 2. UnshareProject çağrısı
        await using (var db = new AuthDbContext(_options))
        {
            var controller = CreateController(db, userId);
            var result = await controller.UnshareProject("proj-toggle-1", CancellationToken.None);
            Assert.IsType<OkObjectResult>(result);
        }

        await using (var db = new AuthDbContext(_options))
        {
            var proj = await db.CloudProjects.FindAsync("proj-toggle-1");
            Assert.NotNull(proj);
            // Ekip organizasyonundan çıkarılmış olmalı
            Assert.NotEqual("org-team-2", proj.OrganizationId);
        }
    }
}
