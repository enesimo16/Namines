using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Namines.Core.Models.Auth;
using Namines.Core.Models.Hub;
using Namines.Infrastructure.Catalogs;
using Namines.Infrastructure.Data;
using Namines.Infrastructure.Services;

namespace Namines.API.Services;

/// <summary>
/// Namines Hub (G57 — Faz 3): Başlangıç vitrini için 10 adet kurumsal, 3NF-onaylı
/// ve doğrulanmış mimari şablonunu veritabanına tohumlar.
/// Şablon tanımları <see cref="HubBlueprintCatalog"/> içindedir.
/// </summary>
public static class HubBlueprintSeeder
{
    private const string ShowcaseUserId = "namines-architect";
    private const string ShowcaseUsername = "namines";
    private const string ShowcaseEmail = "architect@namines.app";

    public static async Task SeedAsync(
        AuthDbContext context,
        UserManager<ApplicationUser> userManager,
        ILogger logger,
        CancellationToken ct = default)
    {
        // 1. Showcase kullanıcısını oluştur veya getir
        var architect = await userManager.FindByNameAsync(ShowcaseUsername)
                        ?? await userManager.FindByIdAsync(ShowcaseUserId);

        if (architect is null)
        {
            architect = new ApplicationUser
            {
                Id = ShowcaseUserId,
                UserName = ShowcaseUsername,
                NormalizedUserName = ShowcaseUsername.ToUpperInvariant(),
                Email = ShowcaseEmail,
                NormalizedEmail = ShowcaseEmail.ToUpperInvariant(),
                EmailConfirmed = true,
                CreatedAt = DateTime.UtcNow,
                IsDev = true
            };
            var createRes = await userManager.CreateAsync(architect);
            if (!createRes.Succeeded)
            {
                logger.LogWarning("Showcase mimarı oluşturulamadı: {Errors}",
                    string.Join(", ", createRes.Errors.Select(e => e.Description)));
                return;
            }
        }

        var personalOrg = await context.GetOrCreatePersonalOrgAsync(architect.Id, ShowcaseUsername, ct);

        // 2. Blueprints tanımları
        var blueprints = HubBlueprintCatalog.All;

        var existingSlugs = await context.CloudProjects
            .Where(p => p.UserId == architect.Id)
            .Select(p => p.Slug)
            .ToListAsync(ct);

        var existingSlugSet = new HashSet<string>(existingSlugs.Where(s => s != null)!, StringComparer.OrdinalIgnoreCase);

        var addedCount = 0;
        foreach (var bp in blueprints)
        {
            if (existingSlugSet.Contains(bp.Slug))
                continue;

            var project = new CloudProject
            {
                Id = Guid.NewGuid().ToString(),
                Name = bp.Name,
                Slug = bp.Slug,
                Description = bp.Description,
                Category = bp.Category,
                Tags = bp.Tags,
                DbType = "PostgreSQL",
                Visibility = ProjectVisibility.Public,
                StarCount = bp.StarCount,
                ForkCount = bp.ForkCount,
                UserId = architect.Id,
                OrganizationId = personalOrg.Id,
                SchemaJson = JsonSerializer.Serialize(bp.Schema, SchemaJsonOptions.Default),
                NodePositionsJson = "{}",
                CreatedAt = DateTime.UtcNow.AddDays(-bp.DaysAgo),
                UpdatedAt = DateTime.UtcNow.AddDays(-bp.DaysAgo)
            };

            await context.CloudProjects.AddAsync(project, ct);

            // Default main branch
            var mainBranch = new Branch
            {
                Id = Guid.NewGuid().ToString(),
                ProjectId = project.Id,
                Name = "main",
                IsDefault = true,
                CreatedByUserId = architect.Id,
                CreatedAt = project.CreatedAt
            };
            await context.Branches.AddAsync(mainBranch, ct);

            // Starter issue
            var starterIssue = new RepoIssue
            {
                Id = Guid.NewGuid().ToString(),
                ProjectId = project.Id,
                IssueNumber = 1,
                Title = bp.StarterIssueTitle,
                Body = bp.StarterIssueBody,
                RelatedTableName = bp.StarterIssueTable,
                NslRuleCode = bp.StarterIssueNslCode,
                Labels = "architecture,review",
                CreatedByUserId = architect.Id,
                CreatedAt = project.CreatedAt.AddHours(2),
                IsClosed = false
            };
            await context.RepoIssues.AddAsync(starterIssue, ct);

            addedCount++;
        }

        if (addedCount > 0)
        {
            await context.SaveChangesAsync(ct);
            logger.LogInformation("Namines Hub için {Count} kurumsal mimari şablonu başarıyla tohumlandı.", addedCount);
        }
    }
}
