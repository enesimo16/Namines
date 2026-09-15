using System;
using System.Collections.Generic;
using System.Linq;
using Namines.Core.Models;

namespace Namines.Core.Analysis;

/// <summary>
/// Bir <see cref="SchemaDiffResult"/>'ı etkin <see cref="AutomationRule"/>'larla
/// eşleştiren saf, deterministik fonksiyon (Bölüm 3 Eki #2).
///
/// <b>Neden Id→Name sözlüğü:</b> <see cref="SchemaDiffResult"/> tabloları AD ile
/// tanımlar (<c>SchemaTable.Name</c>), <see cref="AutomationRule.ScopeTableId"/>
/// ise istemcinin stabil <c>SchemaTable.Id</c>'sini tutar. Silinen bir tablo
/// yalnızca <paramref name="oldSchema"/>'da, eklenen bir tablo yalnızca
/// <paramref name="newSchema"/>'da bulunur — bu yüzden sözlük İKİSİNİN
/// BİRLEŞİMİNDEN kurulur, tek taraflı olsaydı silinen tabloya bağlı kurallar
/// hiç bulunamazdı.
/// </summary>
public static class AutomationRuleMatcher
{
    public static IReadOnlyList<AutomationRule> Match(
        SchemaDiffResult diff,
        DatabaseSchema oldSchema,
        DatabaseSchema newSchema,
        IReadOnlyList<AutomationRule> rules)
    {
        var idToName = new Dictionary<string, string>();
        foreach (var t in oldSchema.Tables) idToName[t.Id] = t.Name;
        foreach (var t in newSchema.Tables) idToName[t.Id] = t.Name;

        var addedTableNames = new HashSet<string>(diff.AddedTables, StringComparer.Ordinal);
        var removedTableNames = new HashSet<string>(diff.RemovedTables, StringComparer.Ordinal);
        var addedColumnTables = new HashSet<string>(
            diff.ModifiedTables.Where(m => m.AddedColumns.Count > 0).Select(m => m.TableName), StringComparer.Ordinal);
        var removedColumnTables = new HashSet<string>(
            diff.ModifiedTables.Where(m => m.RemovedColumns.Count > 0).Select(m => m.TableName), StringComparer.Ordinal);
        var changedColumnTables = new HashSet<string>(
            diff.ModifiedTables.Where(m => m.ModifiedColumns.Count > 0).Select(m => m.TableName), StringComparer.Ordinal);
        var hasRelationAdded = diff.AddedRelations.Count > 0;
        var hasRelationRemoved = diff.RemovedRelations.Count > 0;

        bool Matches(AutomationRule rule)
        {
            if (!rule.Enabled) return false;

            // Proje geneli kural (ScopeTableId == null): hangi tablo olduğuna
            // bakmadan, o TÜR olayın hiç olup olmadığına bakar.
            var scopeName = rule.ScopeTableId is null
                ? null
                : idToName.TryGetValue(rule.ScopeTableId, out var n) ? n : null;

            // ScopeTableId dolu ama sözlükte yoksa (hiç var olmamış bir tablo id'si)
            // eşleşme imkansız.
            if (rule.ScopeTableId is not null && scopeName is null) return false;

            bool InScope(HashSet<string> tableNames) =>
                scopeName is null ? tableNames.Count > 0 : tableNames.Contains(scopeName);

            return rule.TriggerType switch
            {
                "TableAdded" => InScope(addedTableNames),
                "TableDeleted" => InScope(removedTableNames),
                "ColumnAdded" => InScope(addedColumnTables),
                "ColumnDeleted" => InScope(removedColumnTables),
                "ColumnChanged" => InScope(changedColumnTables),
                "RelationAdded" => scopeName is null && hasRelationAdded,
                "RelationDeleted" => scopeName is null && hasRelationRemoved,
                _ => false,
            };
        }

        return rules.Where(Matches).ToList();
    }
}
