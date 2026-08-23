using System.Linq;
using System.Text;
using Namines.Core.Enums;
using Namines.Core.Models;

namespace Namines.Core.Prompts;

/// <summary>
/// Doğal dil → SQL istemi (08 §2 <c>/query/nl</c>).
///
/// <b>İstem yalnızca SELECT istiyor ve bu bir savunma katmanı DEĞİL.</b> Modelin
/// talimata uyacağına güvenmek bir güvenlik kararı olamaz; asıl kapı,
/// üretilen SQL'i <see cref="Analysis.SqlStatementKind"/> ile sınıflandırıp
/// okuma olmayanı çalıştırmamak. İstemdeki kural, modelin işini kolaylaştırmak
/// için — kapının kendisi değil.
/// </summary>
public static class NlQueryPromptBuilder
{
    public static (string System, string User) Build(DatabaseSchema schema, DatabaseType engine, string question)
    {
        var system = new StringBuilder();
        system.AppendLine("You translate a question into a single SQL SELECT statement.");
        system.AppendLine();
        system.AppendLine("Rules:");
        system.AppendLine("- Return ONLY the SQL. No prose, no markdown fences, no explanation.");
        system.AppendLine("- Exactly one statement. Never chain statements with ';'.");
        system.AppendLine("- Read-only. Never INSERT, UPDATE, DELETE, DROP, ALTER or CALL.");
        system.AppendLine("- Use only the tables and columns listed below. Do not invent names.");
        system.AppendLine("- Always add a LIMIT (or the engine's equivalent) unless the question asks for an aggregate.");
        system.AppendLine($"- Target engine: {engine}. Use that engine's syntax and quoting.");
        system.AppendLine();
        // "Bilmiyorum" diyebilmesi ŞART: uyduran bir sorgu, boş dönen bir
        // sorgudan çok daha kötüdür — kullanıcı sonucun doğru olduğunu sanar.
        system.AppendLine("If the question cannot be answered from this schema, return exactly: UNANSWERABLE");

        var user = new StringBuilder();
        user.AppendLine("Schema:");
        foreach (var table in schema.Tables)
        {
            var columns = table.Columns.Select(c =>
            {
                var type = string.IsNullOrWhiteSpace(c.EnumRef) ? c.Type : $"enum({c.EnumRef})";
                return $"{c.Name} {type}{(c.IsPK ? " pk" : string.Empty)}{(c.IsNullable ? " null" : string.Empty)}";
            });

            user.AppendLine($"  {table.Name}({string.Join(", ", columns)})");
        }

        if (schema.Enums.Count > 0)
        {
            user.AppendLine();
            user.AppendLine("Enums:");
            foreach (var definition in schema.Enums)
                user.AppendLine($"  {definition.Name}: {string.Join(", ", definition.Values)}");
        }

        if (schema.Relations.Count > 0)
        {
            user.AppendLine();
            user.AppendLine("Foreign keys:");
            foreach (var relation in schema.Relations)
            {
                var from = schema.Tables.FirstOrDefault(t => t.Id == relation.SourceTableId);
                var to = schema.Tables.FirstOrDefault(t => t.Id == relation.TargetTableId);
                var fromColumn = from?.Columns.FirstOrDefault(c => c.Id == relation.SourceColumnId);
                var toColumn = to?.Columns.FirstOrDefault(c => c.Id == relation.TargetColumnId);

                if (from is null || to is null || fromColumn is null || toColumn is null) continue;

                user.AppendLine($"  {from.Name}.{fromColumn.Name} -> {to.Name}.{toColumn.Name}");
            }
        }

        user.AppendLine();
        user.AppendLine("Question:");
        user.AppendLine(question);

        return (system.ToString(), user.ToString());
    }

    /// <summary>
    /// Model cevabındaki markdown çitlerini temizler.
    ///
    /// Modeller talimata rağmen sık sık ```sql ile sarar; çiti bırakmak, SQL'i
    /// motorun ilk karakterde reddetmesi demek olurdu.
    /// </summary>
    public static string StripFences(string? answer)
    {
        var text = (answer ?? string.Empty).Trim();

        if (!text.StartsWith("```")) return text;

        var firstNewline = text.IndexOf('\n');
        if (firstNewline < 0) return string.Empty;

        text = text[(firstNewline + 1)..];
        var closing = text.LastIndexOf("```", System.StringComparison.Ordinal);

        return (closing >= 0 ? text[..closing] : text).Trim();
    }
}
