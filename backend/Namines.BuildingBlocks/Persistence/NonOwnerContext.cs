using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Namines.BuildingBlocks.Persistence;

/// <summary>
/// DDL'i kendisine ait olmayan bağlamlar için: tablolar migration üretmez, <c>has-pending-model-changes</c>
/// temiz kalır. Yanlışlıkla ikinci bir CREATE TABLE zinciri doğmasın diye bu durum modelde açık.
/// </summary>
public static class NonOwnerContext
{
    /// <summary>
    /// Sahip olmayan bağlamın orkestratör kaydı. Sıraya girmez, çalıştırılmaz; ama listede durduğu için
    /// test her bağlamın ya zinciri ya da gerekçesi olduğunu doğrulayabilir.
    /// </summary>
    public static ModuleMigrationDescriptor Descriptor<TContext>(string module, string schema, string reason)
        where TContext : DbContext =>
        new(module, int.MaxValue, schema, ModuleMigrationOwnership.DdlInAuthChain,
            connectionString => (TContext)System.Activator.CreateInstance(
                typeof(TContext), ModuleDbContextRegistration.MigrationOptions<TContext>(connectionString, schema))!,
            reason);

    public static void ExcludeAllFromMigrations(ModelBuilder modelBuilder)
    {
        foreach (var entity in modelBuilder.Model.GetEntityTypes())
            ((IMutableEntityType)entity).SetIsTableExcludedFromMigrations(true);
    }

    /// <summary>
    /// D-2 modülleri henüz taşınmadı: tablolar gerçekte <c>public</c>'te. Var olmayan bir şemaya eşlemek
    /// ilk sorguda patlardı, bağlam gerçek konuma eşlenir ve migration üretmez.
    /// </summary>
    public static void MapToActualSchema(ModelBuilder modelBuilder, string? actualSchema)
    {
        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            if (actualSchema is not null) ((IMutableEntityType)entity).SetSchema(actualSchema);
            ((IMutableEntityType)entity).SetIsTableExcludedFromMigrations(true);
        }
    }
}
