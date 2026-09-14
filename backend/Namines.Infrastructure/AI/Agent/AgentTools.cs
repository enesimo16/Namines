using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Namines.Core.Enums;
using Namines.Core.Interfaces;
using Namines.Core.Models;
using Namines.Core.Nsl;
using Namines.Infrastructure.Generators.DdlGenerator;

namespace Namines.Infrastructure.AI.Agent;

/// <summary>
/// Modelin çağırabileceği araçlar.
///
/// <b>Araçlar kararı DEĞİŞTİRMEZ.</b> Turu bitiren denetim yine
/// <see cref="Namines.Infrastructure.Services.SchemaAgentPipeline"/> içinde,
/// deterministik tarafta yapılıyor. Buradaki araçlar modele yalnızca ERKEN
/// bilgi veriyor: kolon gerçekten var mı, bu şema derleniyor mu, hangi kural
/// ihlal ediliyor. Modelin kendi çıktısına "temiz" demesi hiçbir şeyi kapatmaz.
///
/// <b>Hiçbir araç istisna fırlatmaz.</b> Bir aracın patlaması tüm turu
/// düşürürdü; oysa model hata metnini okuyup düzeltebilir. Bu yüzden her hata
/// bir CEVAP olarak dönüyor.
/// </summary>
public sealed class AgentTools
{
    private readonly DatabaseType _engine;
    private readonly IDdlGeneratorFactory _ddlFactory;

    /// <summary>Üzerinde çalışılan şema; taslak turunda <c>null</c> olabilir.</summary>
    public DatabaseSchema? Context { get; set; }

    public AgentTools(DatabaseSchema? context, DatabaseType engine, IDdlGeneratorFactory ddlFactory)
    {
        Context = context;
        _engine = engine;
        _ddlFactory = ddlFactory;
    }

    public IReadOnlyList<AgentToolDefinition> Definitions { get; } = new[]
    {
        new AgentToolDefinition(
            "validate_schema",
            "Run the deterministic schema rule engine and return the errors it finds. " +
            "Pass the candidate schema JSON to check a schema you are about to output.",
            """
            {
              "type": "object",
              "properties": {
                "schema": {
                  "type": "string",
                  "description": "Optional DatabaseSchema JSON to validate instead of the current one."
                }
              }
            }
            """),
        new AgentToolDefinition(
            "get_column_info",
            "List the columns and their types for one table of the current schema.",
            """
            {
              "type": "object",
              "properties": {
                "tableName": { "type": "string", "description": "Name of the table to inspect." }
              },
              "required": ["tableName"]
            }
            """),
        new AgentToolDefinition(
            "preview_ddl",
            "Generate the real DDL for the target database engine and return it, " +
            "or return the generator's error message if it cannot be generated.",
            """
            {
              "type": "object",
              "properties": {
                "schema": {
                  "type": "string",
                  "description": "Optional DatabaseSchema JSON to compile instead of the current one."
                }
              }
            }
            """),
    };

    public string Invoke(AgentToolCall call)
    {
        try
        {
            // Her aracın sonucu SINIRLI: hepsi mesaj geçmişine ekleniyor ve
            // model her sonraki araç turunda tüm geçmişi yeniden görüyor.
            return Truncate(call.Name switch
            {
                "validate_schema" => ValidateSchema(call.ArgumentsJson),
                "get_column_info" => GetColumnInfo(call.ArgumentsJson),
                "preview_ddl" => PreviewDdl(call.ArgumentsJson),
                _ => $"Unknown tool '{call.Name}'.",
            });
        }
        catch (Exception ex)
        {
            // Bozuk argüman, okunamayan JSON, beklenmedik durum — hepsi cevap
            // olarak dönüyor ki model düzeltebilsin.
            return $"The tool failed: {ex.Message}";
        }
    }

    /// <summary>
    /// Argümanda şema verildiyse onu, verilmediyse bağlamdaki şemayı kullanır.
    ///
    /// Argümanla şema alabilmek şart: model henüz YAZMADIĞI bir şemayı kontrol
    /// etmek isteyebilir, bağlamdaki ise her zaman bir önceki tur.
    /// </summary>
    private DatabaseSchema? SchemaFrom(string argumentsJson)
    {
        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson);

        if (doc.RootElement.ValueKind == JsonValueKind.Object &&
            doc.RootElement.TryGetProperty("schema", out var schemaEl) &&
            schemaEl.ValueKind == JsonValueKind.String)
        {
            var raw = schemaEl.GetString();
            if (!string.IsNullOrWhiteSpace(raw))
                return JsonSerializer.Deserialize<DatabaseSchema>(raw, Namines.Infrastructure.Services.SchemaJsonOptions.Default);
        }

        return Context;
    }

    private string ValidateSchema(string argumentsJson)
    {
        var schema = SchemaFrom(argumentsJson);
        if (schema is null) return "There is no schema to validate yet.";

        var errors = NslValidator.Validate(schema, _engine)
            .Where(f => f.Severity == "error")
            .ToList();

        if (errors.Count == 0) return $"Validation passed with no errors for {_engine}.";

        return "Errors found:\n" + string.Join("\n", errors.Select(e => $"- {e.Code}: {e.Message}"));
    }

    private string GetColumnInfo(string argumentsJson)
    {
        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson);

        var name = doc.RootElement.ValueKind == JsonValueKind.Object &&
                   doc.RootElement.TryGetProperty("tableName", out var el)
            ? el.GetString()
            : null;

        if (string.IsNullOrWhiteSpace(name)) return "tableName is required.";
        if (Context is null) return "There is no schema loaded yet.";

        var table = Context.Tables.FirstOrDefault(t =>
            string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));

        // Yalnızca "bulunamadı" demek, modelin aynı yanlış adı tekrar denemesine
        // yol açıyor; bilinen adları göstermek turu kurtarıyor.
        if (table is null)
            return $"Table '{name}' was not found. Known tables: " +
                   string.Join(", ", Context.Tables.Select(t => t.Name));

        return $"Table '{table.Name}' (id: {table.Id}):\n" + string.Join("\n", table.Columns.Select(c =>
            $"- {c.Name} (id: {c.Id}, type: {c.Type}{(c.IsPK ? ", PK" : "")}{(c.IsFK ? ", FK" : "")}" +
            $"{(string.IsNullOrWhiteSpace(c.Generated) ? "" : $", generated: {c.Generated}")})"));
    }

    /// <summary>
    /// Bir araç sonucunun üst sınırı, karakter olarak.
    ///
    /// <b>Neden gerekli:</b> bu sonuç mesaj geçmişine EKLENİYOR ve model her
    /// sonraki araç turunda tüm geçmişi yeniden görüyor (bkz.
    /// GroqSchemaDraftSource.RepairWithToolsAsync). Büyük bir şemada
    /// preview_ddl'in ürettiği ham DDL onlarca kilobayt olabilir — sınırsız
    /// bırakmak, tek bir araç çağrısının aynı yanıtı 2-3 kez yeniden
    /// göndererek turu tüketmesi demekti.
    /// </summary>
    private const int MaxToolResultLength = 4000;

    private static string Truncate(string text)
    {
        if (text.Length <= MaxToolResultLength) return text;

        return text[..MaxToolResultLength] +
               $"\n... (truncated, {text.Length - MaxToolResultLength} more characters)";
    }

    private string PreviewDdl(string argumentsJson)
    {
        var schema = SchemaFrom(argumentsJson);
        if (schema is null) return "There is no schema to compile yet.";

        try
        {
            var ddl = _ddlFactory.GetGenerator(_engine).Generate(schema);

            return string.IsNullOrWhiteSpace(ddl)
                ? $"{_engine}: the schema produced no DDL at all."
                : $"{_engine} DDL:\n{ddl}";
        }
        catch (Exception ex)
        {
            // Üreticinin reddi bir CEVAP: model neyin kabul edilmediğini
            // görüp düzeltebilir.
            return $"{_engine} rejected this schema: {ex.Message}";
        }
    }
}
