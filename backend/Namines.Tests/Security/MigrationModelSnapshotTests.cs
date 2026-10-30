using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Internal;
using Namines.Infrastructure.Data;

namespace Namines.Tests.Security;

/// <summary>
/// <c>dotnet ef migrations has-pending-model-changes</c>'in test karşılığı (EF 8'de
/// <c>Database.HasPendingModelChanges</c> yok). Bir entity/enum/CHECK değişip migration
/// eklenmezse (örn. enum'a üye eklemek, bkz. CheckConstraintSql) burada kırmızı yanar —
/// Docker ya da veritabanı bağlantısı gerekmez, yalnızca modelle anlık görüntü karşılaştırılır.
/// </summary>
public sealed class MigrationModelSnapshotTests
{
    [Fact]
    public void Model_migration_anlik_goruntusuyle_senkron()
    {
        var options = new DbContextOptionsBuilder<AuthDbContext>()
            .UseNpgsql("Host=localhost;Database=unused;Username=unused;Password=unused")
            .Options;
        using var context = new AuthDbContext(options);

        var differ = context.GetService<IMigrationsModelDiffer>();
        var migrationsAssembly = context.GetService<IMigrationsAssembly>();
        var initializer = context.GetService<IModelRuntimeInitializer>();

        var snapshotModel = migrationsAssembly.ModelSnapshot?.Model;
        if (snapshotModel is IMutableModel mutable)
            snapshotModel = mutable.FinalizeModel();
        if (snapshotModel is not null)
            snapshotModel = initializer.Initialize(snapshotModel);

        var current = context.GetService<IDesignTimeModel>().Model.GetRelationalModel();
        var differences = differ.GetDifferences(snapshotModel?.GetRelationalModel(), current);

        Assert.True(differences.Count == 0,
            "Model ile son migration anlik goruntusu arasinda fark var; yeni bir migration ekleyin " +
            "(dotnet ef migrations add ...). Ilk farklar: " +
            string.Join("; ", differences.Take(5).Select(d => d.GetType().Name)));
    }
}
