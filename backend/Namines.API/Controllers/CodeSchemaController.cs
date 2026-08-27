using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Namines.Core.Analysis;
using Namines.Core.Enums;
using Namines.Core.Models;

namespace Namines.API.Controllers;

/// <param name="Files">Dosya adı → içerik. İstemci hangi dosyaları göndereceğini seçer.</param>
/// <param name="CompareWith">
/// Doluysa çıkarılan şema BUNA karşı karşılaştırılır — "kodun şunu diyor,
/// veritabanında şu var". Boşsa yalnızca çıkarım yapılır.
/// </param>
public sealed record ExtractCodeSchemaRequest(
    Dictionary<string, string> Files,
    DatabaseSchema? CompareWith,
    DatabaseType DbType = DatabaseType.PostgreSQL);

/// <summary>
/// second-phase/11-KODDAN-SEMA.md — depodaki model/entity tanımlarından şema
/// çıkarır, isteğe bağlı olarak mevcut şemayla farkını gösterir.
///
/// <b>Bedava ve AI kullanmıyor</b> — ayrıştırıcılar tamamen deterministik.
///
/// <b>Kod okunur, DEĞİŞTİRİLMEZ</b> ve hiçbir migration dosyası
/// ÇALIŞTIRILMAZ (doc'un iki açık yasağı; ikincisi bir güvenlik kararı —
/// rastgele bir depodan gelen migration'ı çalıştırmak kod çalıştırmaktır).
/// Bu uç yalnızca metin okur.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class CodeSchemaController : ControllerBase
{
    [HttpPost("extract")]
    [AllowAnonymous]
    public IActionResult Extract([FromBody] ExtractCodeSchemaRequest request)
    {
        if (request?.Files is null || request.Files.Count == 0)
            return BadRequest(new { message = "At least one file is required." });

        CodeExtractionResult extraction;
        try
        {
            extraction = CodeSchemaExtractor.Extract(request.Files);
        }
        catch (CodeSchemaExtractor.UnknownFormatException ex)
        {
            return BadRequest(new { message = ex.Message });
        }

        object? drift = null;
        if (request.CompareWith is not null)
        {
            // Kod UUID taşımaz — hizalanmadan karşılaştırmak HER tabloyu
            // "silindi + eklendi" gösterir (bkz. SchemaUuidAligner).
            var aligned = SchemaUuidAligner.AlignTo(extraction.Schema, request.CompareWith);

            // Yön bilinçli: ESKİ = kodun söylediği, YENİ = şu an elimizdeki şema.
            // Böylece "eklendi/silindi" ifadeleri "veritabanında var ama kodda
            // yok" diye okunuyor — kullanıcının sorduğu soru bu.
            var impact = SchemaImpactAnalyzer.Analyze(aligned, request.CompareWith, request.DbType);
            drift = new
            {
                hasDrift = impact.AffectedTables.Count > 0 || impact.BreakingChanges.Count > 0,
                overallRisk = impact.OverallRisk.ToString(),
                affectedTables = impact.AffectedTables.Select(t => new
                {
                    t.TableName,
                    kind = t.Kind.ToString(),
                    t.ChangedColumns,
                }),
                breakingChanges = impact.BreakingChanges.Select(b => new
                {
                    b.TableName,
                    b.ColumnName,
                    b.Description,
                    kind = b.Kind.ToString(),
                }),
            };
        }

        return Ok(new
        {
            format = extraction.Format,
            schema = extraction.Schema,
            // Dürüst kısmi rapor — doc'un açık kuralı. Kaç model okundu, kaç
            // tanesi neden okunamadı; ikisi de görünür.
            parsedCount = extraction.ParsedModels.Count,
            skippedCount = extraction.Skipped.Count,
            parsedModels = extraction.ParsedModels,
            skipped = extraction.Skipped.Select(s => new { s.Name, s.Reason }),
            drift,
        });
    }
}
