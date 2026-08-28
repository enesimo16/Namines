using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Namines.Core.Analysis;
using Namines.Core.Enums;
using Namines.Core.Models;

namespace Namines.API.Controllers;

/// <param name="Responses">Gözlemlenen JSON yanıtları — yalnızca uç nokta yolu ve gövde.</param>
public sealed record InferShapesRequest(List<ObservedResponse> Responses);

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
/// <b>AI kullanmıyor</b> — ayrıştırıcılar tamamen deterministik, dolayısıyla
/// token kotasından düşmüyor. Ama giriş gerektiriyor: bkz. aşağıdaki
/// <c>[Authorize]</c> gerekçesi.
///
/// <b>Kod okunur, DEĞİŞTİRİLMEZ</b> ve hiçbir migration dosyası
/// ÇALIŞTIRILMAZ (doc'un iki açık yasağı; ikincisi bir güvenlik kararı —
/// rastgele bir depodan gelen migration'ı çalıştırmak kod çalıştırmaktır).
/// Bu uç yalnızca metin okur.
/// </summary>
[ApiController]
[Route("api/[controller]")]
// Giriş ZORUNLU. Bu uçlar AI kullanmıyor ama bedava değil: 200 dosya / 2 MB'a
// kadar metni regex'lerle ayrıştırıyorlar. Kimliksiz bırakmak, sunucuyu
// sınırsız dövülebilir bir CPU kaynağına çevirirdi. "Deterministik olması"
// bedava olmasını gerektirmiyor — /clarify ve /plan saf ve ucuz fonksiyonlar
// olduğu için anonim; bunlar öyle değil.
[Authorize]
public class CodeSchemaController : ControllerBase
{
    /// <summary>
    /// second-phase/06-VERI-KAYNAKLARI.md kademe 3 — gözlemlenen JSON
    /// yanıtlarının ŞEKLİNDEN veri modeli çıkarımı.
    ///
    /// <b>Sonuç bir TASLAKTIR, şema değil.</b> Doc'un kuralı: kullanıcı
    /// varlığı kabul eder, yeniden adlandırır ya da reddeder — otomatik onay
    /// yok. Bu yüzden uç bir <c>DatabaseSchema</c> DÖNDÜRMÜYOR; güven puanı ve
    /// "belirsiz" işaretleriyle birlikte aday listesi döndürüyor.
    ///
    /// <b>Değerler saklanmıyor</b> — çıkarım yalnızca alan adı ve tip üretir
    /// (bkz. <see cref="JsonShapeInferencer"/>).
    ///
    /// Bir tarayıcı extension'ı bu uca <c>ObservedResponse</c> gönderir; ama
    /// extension olmadan da (örnek JSON yapıştırılarak) çalışır.
    /// </summary>
    [HttpPost("infer-shapes")]

    public IActionResult InferShapes([FromBody] InferShapesRequest request)
    {
        if (request?.Responses is null || request.Responses.Count == 0)
            return BadRequest(new { message = "At least one observed response is required." });

        var result = JsonShapeInferencer.Infer(request.Responses);

        return Ok(new
        {
            // "Çıkarılan her şey tahmin" — doc'un açık şartı, yanıtın içinde taşınıyor.
            isGuess = true,
            entities = result.Entities.Select(e => new
            {
                e.Name,
                e.SampleCount,
                e.EndpointCount,
                e.Confidence,
                fields = e.Fields.Select(f => new { f.Name, f.Type, f.SeenCount, f.IsUncertain }),
            }),
            relations = result.Relations.Select(r => new { r.FromEntity, r.FromField, r.ToEntity }),
        });
    }

    [HttpPost("extract")]

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
