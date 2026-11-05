using Microsoft.EntityFrameworkCore;
using Namines.BuildingBlocks.Persistence;

namespace Namines.Infrastructure.Data;

/// <summary>
/// AuthDbContext zincirinin orkestratör kaydı. Her zaman ilk sırada: tarihsel migration'lar tabloları
/// <c>public</c>'te kurar, modül zincirleri onları oradan alır. Geçmiş tablosu tarihsel yerinde kalır.
/// </summary>
public static class AuthMigrations
{
    public const int Order = 0;

    public static ModuleMigrationDescriptor Descriptor { get; } = new(
        "Auth",
        Order,
        "public",
        ModuleMigrationOwnership.OwnsChain,
        connectionString => new AuthDbContext(
            new DbContextOptionsBuilder<AuthDbContext>().UseNpgsql(connectionString).Options));
}
