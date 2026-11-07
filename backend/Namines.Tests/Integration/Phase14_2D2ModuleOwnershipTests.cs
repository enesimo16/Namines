using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Internal;
using Namines.Tests.TestSupport;
using Npgsql;

namespace Namines.Tests.Integration;

/// <summary>
/// Faz 14.2D-2: her modül kendi tablolarının sahibi. Vault, Ground, Flow ve Desk de artık zincir sahibi;
/// Auth yalnızca kimlik tablolarını bilir. Burada: on bir bağlam birbirinden ayrık tablo eşler, hepsi gerçek
/// PostgreSQL'de üretim yolundan (orkestratör) kurulur ve her bağlamın modeli kendi anlık görüntüsüyle uyumludur.
/// </summary>
[Collection("Docker")]
public sealed class Phase14_2D2ModuleOwnershipTests : IClassFixture<Phase14_2BPostgresFixture>
{
    private static readonly string[] NewOwners = { "Vault", "Ground", "Flow", "Desk" };

    private readonly Phase14_2BPostgresFixture _pg;

    public Phase14_2D2ModuleOwnershipTests(Phase14_2BPostgresFixture pg) => _pg = pg;

    public static IEnumerable<object[]> OwningModules() =>
        TestModules.Descriptors.Select(d => new object[] { d.Module });

    private static string Unused => "Host=localhost;Database=unused;Username=unused;Password=unused";

    private static IEnumerable<(string Schema, string Table, IEntityType Entity)> MappedTables(DbContext ctx) =>
        ctx.GetService<IDesignTimeModel>().Model.GetEntityTypes()
            .Where(e => e.GetTableName() is not null)
            .Select(e => (e.GetSchema() ?? "public", e.GetTableName()!, e));

    [Fact]
    public void Eleven_chains_exist_and_the_four_new_owners_map_tables()
    {
        Assert.Equal(11, TestModules.Descriptors.Count);
        Assert.Equal(
            new[] { "Auth", "Studio", "Billing", "Governance", "Hub", "Gateway", "Ops", "Vault", "Ground", "Flow", "Desk" },
            TestModules.Descriptors.OrderBy(d => d.Order).Select(d => d.Module).ToArray());

        foreach (var owner in NewOwners)
        {
            using var ctx = TestModules.Descriptors.Single(d => d.Module == owner).CreateContext(Unused);
            Assert.NotEmpty(MappedTables(ctx));
        }
    }

    [Fact]
    public void No_table_is_mapped_by_two_contexts()
    {
        // İki bağlam aynı tabloyu eşlerse iki zincir de onu "sahiplenir" ve ikinci migration "tablo var" ile düşer.
        var owners = new Dictionary<(string, string), string>();
        var duplicates = new List<string>();
        foreach (var descriptor in TestModules.Descriptors)
        {
            using var ctx = descriptor.CreateContext(Unused);
            foreach (var (schema, table, _) in MappedTables(ctx))
            {
                if (owners.TryGetValue((schema, table), out var other))
                    duplicates.Add($"{schema}.{table}: {other} ve {descriptor.Module}");
                else
                    owners[(schema, table)] = descriptor.Module;
            }
        }
        Assert.True(duplicates.Count == 0, string.Join("\n", duplicates));
    }

    [Theory]
    [MemberData(nameof(OwningModules))]
    public void Owning_context_model_matches_its_migration_snapshot(string module)
    {
        // `dotnet ef migrations has-pending-model-changes` karşılığı: veritabanı gerekmez.
        using var context = TestModules.Descriptors.Single(d => d.Module == module).CreateContext(Unused);

        var differ = context.GetService<IMigrationsModelDiffer>();
        var migrationsAssembly = context.GetService<IMigrationsAssembly>();
        var initializer = context.GetService<IModelRuntimeInitializer>();

        var snapshotModel = migrationsAssembly.ModelSnapshot?.Model;
        Assert.NotNull(snapshotModel);
        if (snapshotModel is IMutableModel mutable)
            snapshotModel = mutable.FinalizeModel();
        snapshotModel = initializer.Initialize(snapshotModel);

        var current = context.GetService<IDesignTimeModel>().Model.GetRelationalModel();
        var differences = differ.GetDifferences(snapshotModel.GetRelationalModel(), current);

        Assert.True(differences.Count == 0,
            $"{module}: model ile son migration anlik goruntusu arasinda fark var; yeni bir migration ekleyin. " +
            "Ilk farklar: " + string.Join("; ", differences.Take(5).Select(d => d.GetType().Name)));
    }

    [RequiresDockerFact]
    public async Task Fresh_database_creates_every_mapped_table_and_column_of_all_eleven_contexts()
    {
        var cs = await _pg.CreateDatabaseAsync("d2_ownership_fresh");
        await TestModules.MigrateAllAsync(cs);

        var tables = new HashSet<string>(StringComparer.Ordinal);
        var columns = new HashSet<string>(StringComparer.Ordinal);
        await using (var conn = new NpgsqlConnection(cs))
        {
            await conn.OpenAsync();
            await using (var cmd = new NpgsqlCommand("SELECT table_schema || '.' || table_name FROM information_schema.tables WHERE table_type = 'BASE TABLE'", conn))
            await using (var reader = await cmd.ExecuteReaderAsync())
                while (await reader.ReadAsync()) tables.Add(reader.GetString(0));
            await using (var cmd = new NpgsqlCommand("SELECT table_schema || '.' || table_name || '.' || column_name FROM information_schema.columns", conn))
            await using (var reader = await cmd.ExecuteReaderAsync())
                while (await reader.ReadAsync()) columns.Add(reader.GetString(0));
        }

        var missing = new List<string>();
        var perContext = new Dictionary<string, int>();
        foreach (var descriptor in TestModules.Descriptors)
        {
            await using var ctx = descriptor.CreateContext(cs);
            var count = 0;
            foreach (var (schema, table, entity) in MappedTables(ctx))
            {
                count++;
                if (!tables.Contains($"{schema}.{table}")) { missing.Add($"{descriptor.Module}: tablo {schema}.{table}"); continue; }
                var store = StoreObjectIdentifier.Table(table, entity.GetSchema());
                foreach (var property in entity.GetProperties())
                {
                    var column = property.GetColumnName(store);
                    // xmin sistem kolonu, information_schema'da görünmez.
                    if (column is null || column == "xmin") continue;
                    if (!columns.Contains($"{schema}.{table}.{column}")) missing.Add($"{descriptor.Module}: kolon {schema}.{table}.{column}");
                }
            }
            perContext[descriptor.Module] = count;
        }

        Assert.True(missing.Count == 0, string.Join("\n", missing));
        // Boş bir bağlam her şeyi "var" gösterirdi: dört yeni sahip dahil hepsi gerçekten tablo eşliyor.
        Assert.All(perContext, kv => Assert.True(kv.Value > 0, kv.Key + " hic tablo eslemiyor"));
    }
}
