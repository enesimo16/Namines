using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Namines.BuildingBlocks.Persistence;

/// <summary>Bir modül bağlamının DDL'e göre durumu. "Sahip değil" açıkça söylenir, unutulmuş sayılmaz.</summary>
public enum ModuleMigrationOwnership
{
    /// <summary>Kendi zinciri ve <c>&lt;şema&gt;.__EFMigrationsHistory</c> tablosu var; orkestratör çalıştırır.</summary>
    OwnsChain,

    /// <summary>Tablolarının DDL'i AuthDbContext zincirinde. Bağlam yalnızca okur/yazar, migration üretmez.</summary>
    DdlInAuthChain,
}

/// <summary>
/// Bir bağlamın migration kaydı. Modül <c>DbContext</c>'i kendi derlemesinde internal olduğu için
/// orkestratör onu bu fabrika üzerinden görür: başka modüle tip sızdırmaz.
/// </summary>
public sealed record ModuleMigrationDescriptor(
    string Module,
    int Order,
    string Schema,
    ModuleMigrationOwnership Ownership,
    Func<string, DbContext> CreateContext,
    string? NonOwnerReason = null);

public sealed record ModuleMigrationResult(string Module, IReadOnlyList<string> Applied);

/// <summary>
/// Üretimde şemayı kuran TEK yol (<c>--migrate</c> ve açılış). Önce AuthDbContext, sonra modüller
/// sırayla: Auth zinciri tarihsel tabloları <c>public</c>'te bırakır, modül zincirleri onları kendi
/// şemasına yerinde taşır. Boş veritabanı ile canlı veritabanı böylece aynı adımlardan geçer; dal yok.
///
/// Danışma kilidi: migration bu süreçte çalışırken ikinci bir örnek aynı anda başlarsa EF'in kendi
/// kilidi yok (EF8), ikisi aynı <c>SET SCHEMA</c>'yı denerdi.
/// Yarıda kalırsa yeniden çalıştırmak güvenli: her bağlam yalnızca kendi eksiklerini uygular.
/// </summary>
public static class ModuleMigrationOrchestrator
{
    /// <summary>Rastgele değil, sabit: tüm örnekler aynı anahtarda buluşmalı.</summary>
    public const long AdvisoryLockKey = 7_346_501_142L;

    public static IReadOnlyList<ModuleMigrationDescriptor> Chain(IEnumerable<ModuleMigrationDescriptor> descriptors)
    {
        var owners = descriptors.Where(d => d.Ownership == ModuleMigrationOwnership.OwnsChain).OrderBy(d => d.Order).ToList();
        var duplicate = owners.GroupBy(d => d.Order).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
            throw new InvalidOperationException($"Aynı sırada iki migration zinciri: {string.Join(", ", duplicate.Select(d => d.Module))}");
        return owners;
    }

    public static async Task<IReadOnlyList<ModuleMigrationResult>> MigrateAllAsync(
        IEnumerable<ModuleMigrationDescriptor> descriptors,
        string connectionString,
        TimeSpan commandTimeout,
        ILogger? logger = null,
        CancellationToken ct = default)
    {
        var chain = Chain(descriptors);
        await using var lockConnection = new NpgsqlConnection(connectionString);
        await lockConnection.OpenAsync(ct);
        await AcquireLockAsync(lockConnection, commandTimeout, ct);

        var results = new List<ModuleMigrationResult>(chain.Count);
        try
        {
            foreach (var module in chain)
            {
                await using var db = module.CreateContext(connectionString);
                db.Database.SetCommandTimeout(commandTimeout);
                var pending = (await db.Database.GetPendingMigrationsAsync(ct)).ToList();
                if (pending.Count > 0)
                {
                    logger?.LogInformation("Migration: {Module} için {Count} migration uygulanıyor.", module.Module, pending.Count);
                    await db.Database.MigrateAsync(ct);
                }
                results.Add(new ModuleMigrationResult(module.Module, pending));
            }
        }
        finally
        {
            await using var unlock = new NpgsqlCommand("SELECT pg_advisory_unlock(@key)", lockConnection);
            unlock.Parameters.AddWithValue("key", AdvisoryLockKey);
            await unlock.ExecuteNonQueryAsync(CancellationToken.None);
        }

        return results;
    }

    /// <summary>Uygulanmamış migration'lar, "modül: migration" biçiminde. Boşsa şema güncel.</summary>
    public static async Task<IReadOnlyList<string>> GetPendingAsync(
        IEnumerable<ModuleMigrationDescriptor> descriptors,
        string connectionString,
        CancellationToken ct = default)
    {
        var pending = new List<string>();
        foreach (var module in Chain(descriptors))
        {
            await using var db = module.CreateContext(connectionString);
            pending.AddRange((await db.Database.GetPendingMigrationsAsync(ct)).Select(m => $"{module.Module}: {m}"));
        }
        return pending;
    }

    /// <summary>
    /// Bekleyen <c>pg_advisory_lock</c> kullanılmaz: açık bir sorgunun anlık görüntüsü, kilidi tutan
    /// örneğin <c>CREATE INDEX CONCURRENTLY</c> adımını bekletir, o da kilidi bırakmaz. İki örnek aynı
    /// anda açılınca bu kilitlenme gerçek PostgreSQL testinde görüldü; kısa denemeler arasında açık sorgu yok.
    /// </summary>
    private static async Task AcquireLockAsync(NpgsqlConnection connection, TimeSpan timeout, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (true)
        {
            await using (var command = new NpgsqlCommand("SELECT pg_try_advisory_lock(@key)", connection))
            {
                command.Parameters.AddWithValue("key", AdvisoryLockKey);
                if (await command.ExecuteScalarAsync(ct) is true) return;
            }

            if (DateTime.UtcNow >= deadline)
                throw new TimeoutException("Başka bir örnek migration çalıştırıyor; kilit süresi içinde alınamadı.");
            await Task.Delay(TimeSpan.FromSeconds(1), ct);
        }
    }
}
