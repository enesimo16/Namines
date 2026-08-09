using Namines.Core.Enums;
using Namines.Core.Models;
using Namines.Infrastructure.Generators.DdlGenerator;
using Namines.Tests.Fixtures;

namespace Namines.Tests.Ddl;

/// <summary>
/// FK silme/güncelleme davranışının doğru üretildiğini doğrular.
///
/// ÖNEMLİ: Bu testlerin varlık sebebi, CASCADE'in KALDIRILMADIĞINI kanıtlamaktır.
/// G3'te yapılan değişiklik "CASCADE'i sil" değil, "CASCADE'i varsayılan olmaktan çıkar"dır.
/// Kullanıcı açıkça istediğinde CASCADE hâlâ üretilmelidir.
/// </summary>
public class ReferentialActionTests
{
    private static string GenerateWith(ReferentialAction onDelete, ReferentialAction onUpdate, DatabaseType engine)
    {
        var schema = SchemaFixtures.ECommerce();
        foreach (var rel in schema.Relations)
        {
            rel.OnDelete = onDelete;
            rel.OnUpdate = onUpdate;
        }
        return new DdlGeneratorFactory().GetGenerator(engine).Generate(schema);
    }

    // ── Varsayılan davranış ──────────────────────────────────────────────────

    [Theory]
    [InlineData(DatabaseType.MSSQL)]
    [InlineData(DatabaseType.PostgreSQL)]
    [InlineData(DatabaseType.MySQL)]
    [InlineData(DatabaseType.MariaDB)]
    [InlineData(DatabaseType.SQLite)]
    [InlineData(DatabaseType.Oracle)]
    public void Default_emits_no_referential_action(DatabaseType engine)
    {
        var ddl = GenerateWith(ReferentialAction.NoAction, ReferentialAction.NoAction, engine);

        Assert.DoesNotContain("ON DELETE", ddl, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ON UPDATE", ddl, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Relation_default_is_no_action()
    {
        var relation = new SchemaRelation();

        Assert.Equal(ReferentialAction.NoAction, relation.OnDelete);
        Assert.Equal(ReferentialAction.NoAction, relation.OnUpdate);
    }

    // ── CASCADE hâlâ üretilebiliyor (özellik kaldırılmadı) ───────────────────

    [Theory]
    [InlineData(DatabaseType.MSSQL)]
    [InlineData(DatabaseType.PostgreSQL)]
    [InlineData(DatabaseType.MySQL)]
    [InlineData(DatabaseType.MariaDB)]
    [InlineData(DatabaseType.SQLite)]
    [InlineData(DatabaseType.Oracle)]
    public void Explicit_cascade_is_still_emitted(DatabaseType engine)
    {
        var ddl = GenerateWith(ReferentialAction.Cascade, ReferentialAction.NoAction, engine);

        Assert.Contains("ON DELETE CASCADE", ddl, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(DatabaseType.MSSQL)]
    [InlineData(DatabaseType.PostgreSQL)]
    [InlineData(DatabaseType.MySQL)]
    [InlineData(DatabaseType.SQLite)]
    public void Explicit_set_null_is_emitted(DatabaseType engine)
    {
        var ddl = GenerateWith(ReferentialAction.SetNull, ReferentialAction.NoAction, engine);

        Assert.Contains("ON DELETE SET NULL", ddl, StringComparison.OrdinalIgnoreCase);
    }

    // ── Motora özgü kısıtlar ─────────────────────────────────────────────────

    [Theory]
    [InlineData(DatabaseType.MSSQL)]
    [InlineData(DatabaseType.Oracle)]
    public void Restrict_falls_back_on_engines_that_lack_it(DatabaseType engine)
    {
        // MSSQL ve Oracle RESTRICT bilmez. Olduğu gibi yazmak çalıştırılamayan DDL üretir.
        var ddl = GenerateWith(ReferentialAction.Restrict, ReferentialAction.NoAction, engine);

        Assert.DoesNotContain("RESTRICT", ddl, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(DatabaseType.PostgreSQL)]
    [InlineData(DatabaseType.MySQL)]
    [InlineData(DatabaseType.MariaDB)]
    [InlineData(DatabaseType.SQLite)]
    public void Restrict_is_emitted_where_supported(DatabaseType engine)
    {
        var ddl = GenerateWith(ReferentialAction.Restrict, ReferentialAction.NoAction, engine);

        Assert.Contains("ON DELETE RESTRICT", ddl, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Oracle_never_emits_on_update()
    {
        // Oracle ON UPDATE'i hiç desteklemez — yazılırsa ORA-00905 alınır.
        var ddl = GenerateWith(ReferentialAction.Cascade, ReferentialAction.Cascade, DatabaseType.Oracle);

        Assert.Contains("ON DELETE CASCADE", ddl, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ON UPDATE", ddl, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Oracle_does_not_emit_set_default()
    {
        // Oracle SET DEFAULT desteklemez. Veri kaybettiren CASCADE'e düşmek yerine
        // en kısıtlayıcı davranışa (hiçbir şey yazmama = NO ACTION) düşmeli.
        var ddl = GenerateWith(ReferentialAction.SetDefault, ReferentialAction.NoAction, DatabaseType.Oracle);

        Assert.DoesNotContain("SET DEFAULT", ddl, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("CASCADE", ddl, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(DatabaseType.MSSQL)]
    [InlineData(DatabaseType.PostgreSQL)]
    [InlineData(DatabaseType.MySQL)]
    public void On_update_is_emitted_where_supported(DatabaseType engine)
    {
        var ddl = GenerateWith(ReferentialAction.NoAction, ReferentialAction.Cascade, engine);

        Assert.Contains("ON UPDATE CASCADE", ddl, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Both_clauses_emitted_together_in_correct_order()
    {
        var ddl = GenerateWith(ReferentialAction.Cascade, ReferentialAction.SetNull, DatabaseType.PostgreSQL);

        Assert.Contains("ON DELETE CASCADE ON UPDATE SET NULL", ddl, StringComparison.OrdinalIgnoreCase);
    }

    // ── Güvenlik yönü: hiçbir düşüş veri kaybına doğru olmamalı ──────────────

    [Theory]
    [InlineData(DatabaseType.MSSQL)]
    [InlineData(DatabaseType.PostgreSQL)]
    [InlineData(DatabaseType.MySQL)]
    [InlineData(DatabaseType.MariaDB)]
    [InlineData(DatabaseType.SQLite)]
    [InlineData(DatabaseType.Oracle)]
    public void No_action_never_degrades_into_cascade(DatabaseType engine)
    {
        foreach (var action in new[]
                 {
                     ReferentialAction.NoAction,
                     ReferentialAction.Restrict,
                     ReferentialAction.SetNull,
                     ReferentialAction.SetDefault
                 })
        {
            var ddl = GenerateWith(action, ReferentialAction.NoAction, engine);

            Assert.False(
                ddl.Contains("CASCADE", StringComparison.OrdinalIgnoreCase),
                $"{engine}: {action} istendi ama çıktıda CASCADE var — bu sessiz veri kaybı demektir.");
        }
    }
}
