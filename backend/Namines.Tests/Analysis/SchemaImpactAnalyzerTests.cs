using Namines.Core.Analysis;
using Namines.Core.Enums;
using Namines.Core.Models;
using Namines.Tests.Fixtures;

namespace Namines.Tests.Analysis;

/// <summary>
/// <see cref="SchemaImpactAnalyzer"/> — "bu şema değişikliği sistemin geri kalanını nasıl
/// etkiler?" sorusuna deterministik cevap. Bkz. new-phase/28-IMPACT-ANALYSIS-ENGINE.md §6
/// (test planı) ve §4 (risk skorlama: max, ortalama değil).
///
/// Fixture'lar (<see cref="SchemaFixtures"/>) sabit StableUuid üretir — aynı fixture'ı iki
/// kez çağırmak, aynı kimlikte iki bağımsız kopya verir. Bu, rename/modify testlerinin
/// StableUuid eşleşmesine güvenmesini sağlar (Id/isim değişse bile StableUuid sabit kalır).
/// </summary>
public class SchemaImpactAnalyzerTests
{
    [Fact]
    public void Identical_schemas_produce_no_findings()
    {
        var oldSchema = SchemaFixtures.ECommerce();
        var newSchema = SchemaFixtures.ECommerce();

        var report = SchemaImpactAnalyzer.Analyze(oldSchema, newSchema, DatabaseType.PostgreSQL);

        Assert.Empty(report.BreakingChanges);
        Assert.Empty(report.DataLossRisks);
        Assert.Empty(report.LockRisks);
        Assert.Equal(RiskLevel.Safe, report.OverallRisk);
        Assert.True(report.Rollback.IsReversible);
    }

    [Fact]
    public void Adding_nullable_column_is_safe()
    {
        var oldSchema = SchemaFixtures.ECommerce();
        var newSchema = SchemaFixtures.ECommerce();
        newSchema.Tables.Single(t => t.Id == "t_users").Columns.Add(new SchemaColumn
        {
            Id = "c_u_phone", Name = "Phone", StableUuid = "uuid-c_u_phone",
            Type = "NVARCHAR", Length = 20, IsNullable = true
        });

        var report = SchemaImpactAnalyzer.Analyze(oldSchema, newSchema, DatabaseType.PostgreSQL);

        Assert.Empty(report.BreakingChanges);
        Assert.Empty(report.DataLossRisks);
        Assert.Equal(RiskLevel.Safe, report.OverallRisk);
        Assert.Contains(report.AffectedTables, t => t.TableName == "Users" && t.Kind == ChangeKind.Modified);
    }

    [Fact]
    public void Adding_not_null_column_without_default_is_flagged()
    {
        var oldSchema = SchemaFixtures.ECommerce();
        var newSchema = SchemaFixtures.ECommerce();
        newSchema.Tables.Single(t => t.Id == "t_users").Columns.Add(new SchemaColumn
        {
            Id = "c_u_tier", Name = "Tier", StableUuid = "uuid-c_u_tier",
            Type = "INT", IsNullable = false, DefaultValue = null
        });

        var report = SchemaImpactAnalyzer.Analyze(oldSchema, newSchema, DatabaseType.PostgreSQL);

        Assert.Contains(report.BreakingChanges, b => b.Kind == BreakingChangeKind.NotNullWithoutDefault);
        Assert.Contains(report.LockRisks, l => l.Operation.Contains("NOT NULL") && l.Severity == LockSeverity.Blocking);
    }

    [Fact]
    public void Removing_column_is_breaking_with_data_loss_and_irreversible()
    {
        var oldSchema = SchemaFixtures.ECommerce();
        var newSchema = SchemaFixtures.ECommerce();
        var users = newSchema.Tables.Single(t => t.Id == "t_users");
        users.Columns.RemoveAll(c => c.Id == "c_u_created");

        var report = SchemaImpactAnalyzer.Analyze(oldSchema, newSchema, DatabaseType.PostgreSQL);

        Assert.Contains(report.BreakingChanges, b => b.Kind == BreakingChangeKind.ColumnRemoved && b.TableName == "Users");
        Assert.Contains(report.DataLossRisks, d => d.TableName == "Users" && d.ColumnName == "CreatedAt");
        Assert.False(report.Rollback.IsReversible);
        // ColumnRemoved hem BreakingChangeKind'in hem DataLossRisk'in bir üyesi —
        // §4'teki max kuralı gereği en yüksek seviye (Breaking) kazanır.
        Assert.Equal(RiskLevel.Breaking, report.OverallRisk);
    }

    [Fact]
    public void Renaming_column_is_detected_via_stable_uuid_not_as_add_remove()
    {
        var oldSchema = SchemaFixtures.ECommerce();
        var newSchema = SchemaFixtures.ECommerce();
        var users = newSchema.Tables.Single(t => t.Id == "t_users");
        var email = users.Columns.Single(c => c.Id == "c_u_email");
        email.Name = "EmailAddress"; // StableUuid aynı kalıyor — gerçek bir rename

        var report = SchemaImpactAnalyzer.Analyze(oldSchema, newSchema, DatabaseType.PostgreSQL);

        Assert.Contains(report.BreakingChanges, b => b.Kind == BreakingChangeKind.ColumnRenamed && b.ColumnName == "EmailAddress");
        Assert.DoesNotContain(report.BreakingChanges, b => b.Kind == BreakingChangeKind.ColumnRemoved);
        Assert.Empty(report.DataLossRisks); // rename veri kaybetmez, sadece sözleşmeyi kırar
    }

    [Fact]
    public void Renaming_table_is_detected_via_stable_uuid()
    {
        var oldSchema = SchemaFixtures.ECommerce();
        var newSchema = SchemaFixtures.ECommerce();
        newSchema.Tables.Single(t => t.Id == "t_users").Name = "Customers";

        var report = SchemaImpactAnalyzer.Analyze(oldSchema, newSchema, DatabaseType.PostgreSQL);

        Assert.Contains(report.BreakingChanges, b => b.Kind == BreakingChangeKind.TableRenamed);
        Assert.Contains(report.AffectedTables, t => t.Kind == ChangeKind.RenamedFrom && t.TableName == "Customers" && t.PreviousName == "Users");
    }

    [Fact]
    public void Removing_table_is_destructive_and_irreversible()
    {
        var oldSchema = SchemaFixtures.ECommerce();
        var newSchema = SchemaFixtures.ECommerce();
        newSchema.Tables.RemoveAll(t => t.Id == "t_items");
        newSchema.Relations.RemoveAll(r => r.SourceTableId == "t_items");

        var report = SchemaImpactAnalyzer.Analyze(oldSchema, newSchema, DatabaseType.PostgreSQL);

        Assert.Contains(report.BreakingChanges, b => b.Kind == BreakingChangeKind.TableRemoved && b.TableName == "OrderItems");
        Assert.Contains(report.DataLossRisks, d => d.TableName == "OrderItems");
        Assert.False(report.Rollback.IsReversible);
    }

    [Fact]
    public void Narrowing_column_length_is_flagged_as_type_narrowed_with_data_loss_risk()
    {
        var oldSchema = SchemaFixtures.ECommerce();
        var newSchema = SchemaFixtures.ECommerce();
        var email = newSchema.Tables.Single(t => t.Id == "t_users").Columns.Single(c => c.Id == "c_u_email");
        email.Length = 50; // 255 -> 50, daraltma

        var report = SchemaImpactAnalyzer.Analyze(oldSchema, newSchema, DatabaseType.PostgreSQL);

        Assert.Contains(report.BreakingChanges, b => b.Kind == BreakingChangeKind.TypeNarrowed && b.ColumnName == "Email");
        Assert.Contains(report.DataLossRisks, d => d.ColumnName == "Email");
        Assert.NotEqual(RiskLevel.Safe, report.OverallRisk);
    }

    [Fact]
    public void Widening_column_length_is_not_flagged_as_narrowing()
    {
        var oldSchema = SchemaFixtures.ECommerce();
        var newSchema = SchemaFixtures.ECommerce();
        var email = newSchema.Tables.Single(t => t.Id == "t_users").Columns.Single(c => c.Id == "c_u_email");
        email.Length = 500; // 255 -> 500, genişletme — güvenli

        var report = SchemaImpactAnalyzer.Analyze(oldSchema, newSchema, DatabaseType.PostgreSQL);

        Assert.DoesNotContain(report.BreakingChanges, b => b.Kind == BreakingChangeKind.TypeNarrowed);
        Assert.Empty(report.DataLossRisks);
    }

    [Fact]
    public void New_foreign_key_without_covering_index_suggests_index()
    {
        var oldSchema = SchemaFixtures.ECommerce();
        var newSchema = SchemaFixtures.ECommerce();

        var reviews = new SchemaTable
        {
            Id = "t_reviews", Name = "Reviews", StableUuid = "uuid-t_reviews",
            Columns =
            {
                new SchemaColumn { Id = "c_r_id", Name = "Id", StableUuid = "uuid-c_r_id", Type = "INT", IsPK = true },
                new SchemaColumn { Id = "c_r_user", Name = "UserId", StableUuid = "uuid-c_r_user", Type = "INT", IsFK = true }
            }
        };
        newSchema.Tables.Add(reviews);
        newSchema.Relations.Add(new SchemaRelation
        {
            Id = "r4", Type = "OneToMany",
            SourceTableId = "t_reviews", SourceColumnId = "c_r_user",
            TargetTableId = "t_users", TargetColumnId = "c_u_id"
        });

        var report = SchemaImpactAnalyzer.Analyze(oldSchema, newSchema, DatabaseType.PostgreSQL);

        Assert.Contains(report.IndexSuggestions, s => s.TableName == "Reviews" && s.ColumnName == "UserId");
        Assert.Contains(report.LockRisks, l => l.Operation == "ADD FOREIGN KEY");
    }

    [Fact]
    public void New_index_without_concurrently_is_a_blocking_lock_risk()
    {
        var oldSchema = SchemaFixtures.ECommerce();
        var newSchema = SchemaFixtures.ECommerce();
        newSchema.Tables.Single(t => t.Id == "t_users").Indexes.Add(new SchemaIndex
        {
            Id = "ix_new", StableUuid = "uuid-ix_new",
            Columns = { new SchemaIndexColumn { ColumnId = "c_u_email" } }
        });

        var report = SchemaImpactAnalyzer.Analyze(oldSchema, newSchema, DatabaseType.PostgreSQL);

        Assert.Contains(report.LockRisks, l => l.Operation == "CREATE INDEX" && l.Severity == LockSeverity.Blocking);
        Assert.Contains(report.AffectedIndexes, i => i.TableName == "Users" && i.Kind == ChangeKind.Added);
    }

    [Fact]
    public void Multi_cascade_path_is_inherited_from_fk_cascade_analyzer_as_breaking()
    {
        var oldSchema = SchemaFixtures.MultiCascadePath();
        var newSchema = SchemaFixtures.MultiCascadePath();
        foreach (var rel in newSchema.Relations)
            rel.OnDelete = ReferentialAction.Cascade;

        var report = SchemaImpactAnalyzer.Analyze(oldSchema, newSchema, DatabaseType.MSSQL);

        Assert.Contains(report.BreakingChanges, b => b.Kind == BreakingChangeKind.MultipleCascadePaths);
        Assert.Equal(RiskLevel.Breaking, report.OverallRisk);
    }

    [Fact]
    public void Single_cascade_path_produces_no_cascade_breaking_change()
    {
        var oldSchema = SchemaFixtures.ECommerce();
        var newSchema = SchemaFixtures.ECommerce();
        newSchema.Relations.Single(r => r.Id == "r2").OnDelete = ReferentialAction.Cascade;

        var report = SchemaImpactAnalyzer.Analyze(oldSchema, newSchema, DatabaseType.MSSQL);

        Assert.DoesNotContain(report.BreakingChanges, b => b.Kind == BreakingChangeKind.MultipleCascadePaths);
        Assert.DoesNotContain(report.BreakingChanges, b => b.Kind == BreakingChangeKind.CascadeCycle);
    }

    [Fact]
    public void Overall_risk_uses_max_not_average()
    {
        // 3 güvenli değişiklik (yeni nullable kolonlar) + 1 kolon silme (destructive/breaking).
        // Ortalama alınsaydı "orta risk" çıkardı; max kuralı gereği en kötü bulgu kazanmalı.
        var oldSchema = SchemaFixtures.ECommerce();
        var newSchema = SchemaFixtures.ECommerce();
        var users = newSchema.Tables.Single(t => t.Id == "t_users");
        users.Columns.Add(new SchemaColumn { Id = "c_a", Name = "A", StableUuid = "uuid-a", Type = "NVARCHAR", Length = 50, IsNullable = true });
        users.Columns.Add(new SchemaColumn { Id = "c_b", Name = "B", StableUuid = "uuid-b", Type = "NVARCHAR", Length = 50, IsNullable = true });
        users.Columns.Add(new SchemaColumn { Id = "c_c", Name = "C", StableUuid = "uuid-c", Type = "NVARCHAR", Length = 50, IsNullable = true });
        users.Columns.RemoveAll(c => c.Id == "c_u_created");

        var report = SchemaImpactAnalyzer.Analyze(oldSchema, newSchema, DatabaseType.PostgreSQL);

        Assert.Equal(RiskLevel.Breaking, report.OverallRisk);
    }
}
