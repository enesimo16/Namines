using System.Linq;
using Namines.Core.Analysis;
using Namines.Core.Enums;
using Namines.Core.Models;

namespace Namines.Tests.Analysis;

/// <summary>
/// second-phase/11-KODDAN-SEMA.md — koddan çıkarılan şemanın canlı şemayla
/// karşılaştırılabilmesi.
///
/// <b>Bu dosya bir REGRESYON testi.</b> Hizalama olmadan drift raporu her
/// tabloyu "silindi + eklendi" gösteriyordu; ayrıştırıcı ve analizör ayrı ayrı
/// doğru çalıştığı için birim testler bunu kaçırmıştı, hata ancak gerçek uca
/// istek atılınca görüldü.
/// </summary>
public class SchemaUuidAlignerTests
{
    private static DatabaseSchema LiveSchema() => new()
    {
        Name = "live",
        Tables =
        {
            new SchemaTable
            {
                Id = "t1", Name = "User", StableUuid = "uuid-user",
                Columns =
                {
                    new SchemaColumn { Id = "c1", Name = "id", StableUuid = "uuid-id", Type = "INT", IsPK = true },
                    new SchemaColumn { Id = "c2", Name = "email", StableUuid = "uuid-email", Type = "VARCHAR", Length = 320 },
                    new SchemaColumn { Id = "c3", Name = "last_login", StableUuid = "uuid-last-login", Type = "TIMESTAMP", IsNullable = true },
                },
            },
        },
    };

    private static DatabaseSchema FromCode() => new()
    {
        Name = "from-prisma",
        Tables =
        {
            new SchemaTable
            {
                Id = "User", Name = "User", // StableUuid: rastgele — kod UUID taşımaz
                Columns =
                {
                    new SchemaColumn { Id = "User.id", Name = "id", Type = "INT", IsPK = true },
                    new SchemaColumn { Id = "User.email", Name = "email", Type = "VARCHAR", Length = 320 },
                },
            },
        },
    };

    [Fact]
    public void Matching_tables_and_columns_take_the_reference_uuid()
    {
        var aligned = SchemaUuidAligner.AlignTo(FromCode(), LiveSchema());

        var table = Assert.Single(aligned.Tables);
        Assert.Equal("uuid-user", table.StableUuid);
        Assert.Equal("uuid-email", table.Columns.Single(c => c.Name == "email").StableUuid);
    }

    [Fact]
    public void A_column_only_in_code_keeps_its_own_uuid_so_it_still_reads_as_new()
    {
        var code = FromCode();
        code.Tables[0].Columns.Add(new SchemaColumn { Id = "User.nickname", Name = "nickname", Type = "VARCHAR" });

        var aligned = SchemaUuidAligner.AlignTo(code, LiveSchema());

        var nickname = aligned.Tables[0].Columns.Single(c => c.Name == "nickname");
        Assert.DoesNotContain("uuid-", nickname.StableUuid);
    }

    [Fact]
    public void Name_matching_is_case_insensitive()
    {
        var code = FromCode();
        code.Tables[0].Name = "USER";
        code.Tables[0].Columns[1].Name = "EMAIL";

        var aligned = SchemaUuidAligner.AlignTo(code, LiveSchema());

        Assert.Equal("uuid-user", aligned.Tables[0].StableUuid);
        Assert.Equal("uuid-email", aligned.Tables[0].Columns[1].StableUuid);
    }

    [Fact]
    public void After_aligning_the_impact_report_shows_a_modified_table_not_a_deleted_one()
    {
        // Asıl hatanın kendisi: hizalama olmadan analizör "TableRemoved" +
        // "TableAdded" üretiyor ve rapor tamamen yanıltıcı oluyordu.
        var aligned = SchemaUuidAligner.AlignTo(FromCode(), LiveSchema());

        var impact = SchemaImpactAnalyzer.Analyze(aligned, LiveSchema(), DatabaseType.PostgreSQL);

        Assert.DoesNotContain(impact.BreakingChanges, b => b.Kind == BreakingChangeKind.TableRemoved);
        var affected = Assert.Single(impact.AffectedTables);
        Assert.Equal(ChangeKind.Modified, affected.Kind);
        Assert.Contains("last_login", affected.ChangedColumns);
    }

    [Fact]
    public void Without_aligning_the_same_comparison_is_misleading()
    {
        // Hizalamanın neden var olduğunu kanıtlayan karşıt test: biri bu adımı
        // "gereksiz" diye kaldırırsa burası kırmızı yanar.
        var impact = SchemaImpactAnalyzer.Analyze(FromCode(), LiveSchema(), DatabaseType.PostgreSQL);

        Assert.Contains(impact.BreakingChanges, b => b.Kind == BreakingChangeKind.TableRemoved);
    }

    [Fact]
    public void A_table_missing_from_the_reference_is_left_untouched()
    {
        var code = FromCode();
        code.Tables.Add(new SchemaTable { Id = "Post", Name = "Post", Columns = { new SchemaColumn { Id = "Post.id", Name = "id", Type = "INT" } } });

        var aligned = SchemaUuidAligner.AlignTo(code, LiveSchema());

        var post = aligned.Tables.Single(t => t.Name == "Post");
        Assert.DoesNotContain("uuid-", post.StableUuid);
    }
}
