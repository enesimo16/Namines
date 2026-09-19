using System;
using System.Collections.Generic;
using System.Linq;
using Namines.Core.Models;

namespace Namines.Core.Analysis;

/// <summary>Eşleşen bir kural ve onu tetikleyen SOMUT bağlam.</summary>
public readonly record struct AutomationMatch(AutomationRule Rule, AutomationTriggerContext Context);

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
///
/// <b>Neden yalnızca kural değil, bağlam da dönüyor:</b> koşullar (ve ileride
/// aksiyon şablonları) "hangi tablo / hangi kolon" bilgisine ihtiyaç duyuyor.
/// Bu bilgi burada zaten hesaplanıyor; çağıranın diff'i ikinci kez taraması
/// hem tekrar hem de iki tarafın ayrışma riski olurdu.
/// </summary>
public static class AutomationRuleMatcher
{
    public static IReadOnlyList<AutomationMatch> Match(
        SchemaDiffResult diff,
        DatabaseSchema oldSchema,
        DatabaseSchema newSchema,
        IReadOnlyList<AutomationRule> rules)
    {
        var idToName = new Dictionary<string, string>();
        foreach (var t in oldSchema.Tables) idToName[t.Id] = t.Name;
        foreach (var t in newSchema.Tables) idToName[t.Id] = t.Name;

        var matches = new List<AutomationMatch>();

        foreach (var rule in rules)
        {
            if (!rule.Enabled) continue;

            // Proje geneli kural (ScopeTableId == null): hangi tablo olduğuna
            // bakmadan, o TÜR olayın hiç olup olmadığına bakar.
            string? scopeName = null;
            if (rule.ScopeTableId is not null)
            {
                // ScopeTableId dolu ama sözlükte yoksa (hiç var olmamış bir
                // tablo id'si) eşleşme imkansız.
                if (!idToName.TryGetValue(rule.ScopeTableId, out scopeName)) continue;
            }

            foreach (var context in CandidateContexts(rule.TriggerType, diff, oldSchema, newSchema))
            {
                if (scopeName is not null && !string.Equals(context.TableName, scopeName, StringComparison.Ordinal))
                    continue;
                if (!AutomationConditionEvaluator.Matches(rule.ConditionsJson, context)) continue;

                // İlk uyan bağlam yeterli — kural bir kez tetiklenir. Aksi
                // hâlde on kolonu birden değişen bir tablo aynı kuralı on kez
                // çalıştırır, on webhook atardı.
                matches.Add(new AutomationMatch(rule, context));
                break;
            }
        }

        return matches;
    }

    /// <summary>
    /// Bu tetikleyici tipinin diff'te karşılık geldiği somut olaylar. Koşullar
    /// bunların HER BİRİNE ayrı ayrı uygulanıyor; biri bile uyarsa kural
    /// tetikleniyor.
    /// </summary>
    private static IEnumerable<AutomationTriggerContext> CandidateContexts(
        string triggerType, SchemaDiffResult diff, DatabaseSchema oldSchema, DatabaseSchema newSchema)
    {
        switch (triggerType)
        {
            case "TableAdded":
                foreach (var name in diff.AddedTables)
                    yield return new AutomationTriggerContext(name, null, null);
                break;

            case "TableDeleted":
                foreach (var name in diff.RemovedTables)
                    yield return new AutomationTriggerContext(name, null, null);
                break;

            case "ColumnAdded":
                foreach (var table in diff.ModifiedTables)
                    foreach (var column in table.AddedColumns)
                        yield return new AutomationTriggerContext(
                            table.TableName, column, ColumnType(newSchema, table.TableName, column));
                break;

            case "ColumnDeleted":
                foreach (var table in diff.ModifiedTables)
                    foreach (var column in table.RemovedColumns)
                        // Silinen kolonun tipi yalnızca ESKİ şemada var.
                        yield return new AutomationTriggerContext(
                            table.TableName, column, ColumnType(oldSchema, table.TableName, column));
                break;

            case "ColumnChanged":
                foreach (var table in diff.ModifiedTables)
                    foreach (var column in table.ModifiedColumns)
                        yield return new AutomationTriggerContext(
                            table.TableName, column, ColumnType(newSchema, table.TableName, column));
                break;

            case "RelationAdded":
                // İlişkiler diff'te bir tabloya atfedilmiyor, bu yüzden
                // tabloya bağlı kurallarla eşleştirilemiyorlar; yalnızca proje
                // geneli kurallar için anlamlılar (TableName null bırakıldığı
                // için kapsam filtresi zaten tabloya bağlı kuralları eliyor).
                if (diff.AddedRelations.Count > 0)
                    yield return new AutomationTriggerContext(null, null, null);
                break;

            case "RelationDeleted":
                if (diff.RemovedRelations.Count > 0)
                    yield return new AutomationTriggerContext(null, null, null);
                break;
        }
    }

    private static string? ColumnType(DatabaseSchema schema, string tableName, string columnName) =>
        schema.Tables
            .FirstOrDefault(t => string.Equals(t.Name, tableName, StringComparison.Ordinal))?
            .Columns.FirstOrDefault(c => string.Equals(c.Name, columnName, StringComparison.Ordinal))?
            .Type;
}
