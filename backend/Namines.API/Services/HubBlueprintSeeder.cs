using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Namines.BuildingBlocks.Application;
using Namines.BuildingBlocks.Persistence;
using Namines.Core.Models.Auth;
using Namines.Hub.Domain;
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
        Namines.BuildingBlocks.Contracts.IIdentityDirectory identity,
        IStudioProjects studioProjects,
        IStudioBranches studioBranches,
        UserManager<ApplicationUser> userManager,
        IHubApplicationService hub,
        ICrossModuleTransaction transaction,
        ILogger logger,
        CancellationToken ct = default)
    {
        // 1. Showcase kullanıcısını oluştur veya getir
        // Sabit Id ile aranıyor: eskiden "namines" kullanıcı adıyla bulunan HERHANGİ bir
        // kullanıcı showcase sahibi sayılıyordu — o adı önceden kapmış gerçek bir hesap,
        // vitrin reposlarının sahibi olurdu. Ad başka bir Id'ye aitse tohumlama atlanır.
        var architect = await userManager.FindByIdAsync(ShowcaseUserId);

        if (architect is null)
        {
            var nameOwner = await userManager.FindByNameAsync(ShowcaseUsername);
            if (nameOwner is not null)
            {
                logger.LogWarning(
                    "Hub vitrin tohumlaması atlandı: '{Username}' kullanıcı adı başka bir hesaba ait.",
                    ShowcaseUsername);
                return;
            }

            architect = new ApplicationUser
            {
                Id = ShowcaseUserId,
                UserName = ShowcaseUsername,
                NormalizedUserName = ShowcaseUsername.ToUpperInvariant(),
                Email = ShowcaseEmail,
                NormalizedEmail = ShowcaseEmail.ToUpperInvariant(),
                EmailConfirmed = true,
                CreatedAt = DateTime.UtcNow,
                // Vitrin hesabı yönetici DEĞİL: IsDev=true admin yetkisi veriyordu.
                IsDev = false
            };
            var createRes = await userManager.CreateAsync(architect);
            if (!createRes.Succeeded)
            {
                logger.LogWarning("Showcase mimarı oluşturulamadı: {Errors}",
                    string.Join(", ", createRes.Errors.Select(e => e.Description)));
                return;
            }
        }

        // Önceki sürümler vitrin hesabını IsDev=true (admin) ile oluşturmuştu; mevcut kayıt da düşürülüyor.
        if (architect.IsDev)
        {
            architect.IsDev = false;
            await userManager.UpdateAsync(architect);
        }

        var personalOrgId = await identity.GetOrCreatePersonalOrganizationAsync(architect.Id, ShowcaseUsername, ct);

        // 2. Blueprints tanımları
        var blueprints = HubBlueprintCatalog.All;

        var existingSlugs = await hub.GetSlugsByOwnerAsync(architect.Id, ct);

        var existingSlugSet = new HashSet<string>(existingSlugs, StringComparer.OrdinalIgnoreCase);
        var newRepositories = new List<(ProjectRepository Repository, string? Tags)>();
        var newProjects = new List<ProjectWrite>();
        var starterProjects = new List<(string ProjectId, DateTime SeededAt)>();
        var starterIssues = new List<RepoIssue>();

        var addedCount = 0;
        foreach (var bp in blueprints)
        {
            if (existingSlugSet.Contains(bp.Slug))
                continue;

            var seededAt = DateTime.UtcNow.AddDays(-bp.DaysAgo);
            var project = new ProjectWrite(
                Guid.NewGuid().ToString(), bp.Name, "PostgreSQL",
                JsonSerializer.Serialize(bp.Schema, SchemaJsonOptions.Default), "{}",
                architect.Id, personalOrgId, null, seededAt, seededAt);
            newProjects.Add(project);

            // Katalogdaki StarCount/ForkCount uydurma rakamlardı; Hub gerçek etkileşim
            // göstermeli, tohumlanan repolar 0'dan başlar.
            newRepositories.Add((new ProjectRepository
            {
                ProjectId = project.Id,
                OwnerUserId = architect.Id,
                ProjectName = project.Name,
                ProjectDbType = project.DbType,
                Slug = bp.Slug,
                Description = bp.Description,
                Category = bp.Category,
                Visibility = ProjectVisibility.Public,
                CreatedAt = seededAt,
                UpdatedAt = seededAt
            }, bp.Tags));

            starterProjects.Add((project.Id, seededAt));

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
                CreatedByUserId = architect.Id,
                CreatedAt = seededAt.AddHours(2),
                IsClosed = false
            };
            starterIssues.Add(starterIssue);

            addedCount++;
        }

        if (addedCount > 0)
        {
            // Proje ve vitrin satırı tek işlemde: yarım kalan tohum, bir sonraki açılışta slug'ı
            // "var" sayıp projeyi vitrinsiz bırakırdı.
            await transaction.ExecuteAsync(async c =>
            {
                await studioProjects.SaveAsync(newProjects, c);
                foreach (var (projectId, _) in starterProjects)
                    await studioBranches.GetOrCreateDefaultAsync(projectId, architect.Id, c);
                foreach (var (repository, tags) in newRepositories)
                    await hub.CreateRepositoryAsync(repository, tags, c);
                foreach (var issue in starterIssues)
                    await hub.AddSeedIssueAsync(issue, "architecture,review", c);
            }, ct);
            logger.LogInformation("Namines Hub için {Count} kurumsal mimari şablonu başarıyla tohumlandı.", addedCount);
        }
    }
}
