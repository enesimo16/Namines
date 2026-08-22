using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Namines.Core.Analysis;
using System.Linq;
using Namines.Infrastructure.Generators.Eject;
using Namines.Infrastructure.Observability;
using Namines.Core.Enums;
using Namines.Core.Interfaces;
using Namines.Core.Nsl;
using Namines.Core.Models;
using Namines.Infrastructure.Generators.DdlGenerator;

namespace Namines.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed record NslParseRequest(string Text, DatabaseType DbType = DatabaseType.PostgreSQL);

public class CompileController : ControllerBase
{
    private readonly IDdlGeneratorFactory _ddlFactory;
    private readonly IEfCoreGenerator _efCoreGenerator;
    private readonly IPrismaGenerator _prismaGenerator;
    private readonly IEjectGeneratorRegistry _eject;

    public CompileController(
        IDdlGeneratorFactory ddlFactory,
        IEfCoreGenerator efCoreGenerator,
        IPrismaGenerator prismaGenerator,
        IEjectGeneratorRegistry eject)
    {
        _ddlFactory = ddlFactory;
        _efCoreGenerator = efCoreGenerator;
        _prismaGenerator = prismaGenerator;
        _eject = eject;
    }

    [HttpPost("sql")]
    public IActionResult CompileSql([FromBody] CompileRequest request)
    {
        if (request.Schema == null) return BadRequest("Schema is required.");

        var generator = _ddlFactory.GetGenerator(request.DbType);
        var sql = generator.Generate(request.Schema);

        // Yabancı anahtar davranışlarını denetle. Amaç, çalıştırılamayan veya veri
        // kaybettiren DDL'in kullanıcıya sessizce ulaşmasını engellemek.
        //
        // İstek BLOKLANMAZ — SQL yine döner. Kullanıcı kendi veritabanını tasarlıyor;
        // uyarıyı görüp bilerek devam etmeyi seçebilir. Bloklamak, motoru bilmediğimiz
        // (ör. sonradan farklı bir motora export edecek) durumlarda yanlış olurdu.
        var diagnostics = FkCascadeAnalyzer.Analyze(request.Schema, request.DbType)
            .Select(i => new
            {
                kind = i.Kind.ToString(),
                severity = i.Kind is CascadeIssueKind.MultipleCascadePaths or CascadeIssueKind.CascadeCycle
                    ? "error"
                    : "warning",
                message = i.Message,
                relationId = i.RelationId,
                fromTable = i.FromTable,
                toTable = i.ToTable
            })
            .ToList();

        return Ok(new { sql, diagnostics });
    }

    [HttpPost("efcore")]
    public IActionResult CompileEfCore([FromBody] CompileRequest request)
    {
        if (request.Schema == null) return BadRequest("Schema is required.");

        var files = _efCoreGenerator.Generate(request.Schema);

        using var memoryStream = new MemoryStream();
        using (var archive = new ZipArchive(memoryStream, ZipArchiveMode.Create, true))
        {
            foreach (var file in files)
            {
                var entry = archive.CreateEntry(file.Key);
                using var entryStream = entry.Open();
                using var streamWriter = new StreamWriter(entryStream, Encoding.UTF8);
                streamWriter.Write(file.Value);
            }
        }

        memoryStream.Position = 0;
        return File(memoryStream.ToArray(), "application/zip", $"{request.Schema.Name ?? "Models"}_EFCore.zip");
    }

    /// <summary>
    /// Prisma şeması — önizleme için düz metin.
    ///
    /// ZIP'ten AYRI bir uç: kullanıcının çıktıyı indirmeden önce görmesi gerekir,
    /// çünkü Prisma bazı yapıları (CHECK kısıtları gibi) ifade edemez ve bunlar
    /// <c>warnings</c> içinde bildirilir. Uyarıyı ancak indirdikten sonra görmek,
    /// kısıtı zaten kaybettikten sonra öğrenmek olurdu.
    /// </summary>
    [HttpPost("prisma")]
    public IActionResult CompilePrisma([FromBody] CompileRequest request)
    {
        if (request.Schema == null) return BadRequest("Schema is required.");

        try
        {
            var result = _prismaGenerator.Generate(request.Schema, request.DbType);
            return Ok(new
            {
                schema = result.Files["schema.prisma"],
                env = result.Files[".env.example"],
                warnings = result.Warnings,
            });
        }
        catch (NotSupportedException ex)
        {
            // Oracle: Prisma'nın provider'ı yok. Uydurma bir provider ile
            // ayrıştırılabilir ama tamamen yanlış bir dosya üretmektense reddet.
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("prisma/zip")]
    public IActionResult CompilePrismaZip([FromBody] CompileRequest request)
    {
        if (request.Schema == null) return BadRequest("Schema is required.");

        PrismaGenerationResult result;
        try
        {
            result = _prismaGenerator.Generate(request.Schema, request.DbType);
        }
        catch (NotSupportedException ex)
        {
            return BadRequest(new { error = ex.Message });
        }

        using var memoryStream = new MemoryStream();
        using (var archive = new ZipArchive(memoryStream, ZipArchiveMode.Create, true))
        {
            foreach (var file in result.Files)
            {
                // Prisma `prisma/schema.prisma` bekler; kök dizine koymak kullanıcıyı
                // dosyayı elle taşımaya zorlardı.
                var path = file.Key == "schema.prisma" ? "prisma/schema.prisma" : file.Key;
                var entry = archive.CreateEntry(path);
                using var entryStream = entry.Open();
                using var streamWriter = new StreamWriter(entryStream, Encoding.UTF8);
                streamWriter.Write(file.Value);
            }
        }

        memoryStream.Position = 0;
        return File(memoryStream.ToArray(), "application/zip", $"{request.Schema.Name ?? "Schema"}_Prisma.zip");
    }

    // ── Eject hedefleri (12-CODEGEN-EJECT.md) ────────────────────────────────

    /// <summary>Kullanılabilir hedefler — arayüzün listeyi elle taşımaması için.</summary>
    [HttpGet("eject/targets")]
    public IActionResult EjectTargets() =>
        Ok(_eject.All.Select(g => new { target = g.Target, name = g.DisplayName }));

    /// <summary>
    /// Bir hedef için dosyaları üretir ve UYARILARLA birlikte döndürür.
    ///
    /// ZIP'ten ayrı bir önizleme ucu: uyarıları ancak indirdikten sonra görmek,
    /// hedefin neyi taşımadığını iş işten geçtikten sonra öğrenmek olurdu
    /// (Prisma ucunda alınan aynı karar).
    /// </summary>
    [HttpPost("eject/{target}")]
    public IActionResult Eject(string target, [FromBody] CompileRequest request)
    {
        if (request.Schema == null) return BadRequest("Schema is required.");

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var result = _eject.Get(target).Generate(request.Schema, request.DbType);
            NaminesMetrics.SchemaCompiled(request.DbType.ToString(), target, success: true, stopwatch.Elapsed);
            return Ok(new { files = result.Files, warnings = result.Warnings });
        }
        catch (NotSupportedException ex)
        {
            // Desteklenmeyen motor da bir sonuç: "hangi hedef hangi motorda
            // isteniyor ama çalışmıyor" sorusunu ancak bu sayaç cevaplar.
            NaminesMetrics.SchemaCompiled(request.DbType.ToString(), target, success: false, stopwatch.Elapsed);
            // Hedefin bu motoru desteklememesi de (ör. Drizzle + Oracle) buraya düşer;
            // uydurma bir çıktı üretmektense reddetmek doğru.
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("eject/{target}/zip")]
    public IActionResult EjectZip(string target, [FromBody] CompileRequest request)
    {
        if (request.Schema == null) return BadRequest("Schema is required.");

        EjectResult result;
        IEjectGenerator generator;
        try
        {
            generator = _eject.Get(target);
            result = generator.Generate(request.Schema, request.DbType);
        }
        catch (NotSupportedException ex)
        {
            return BadRequest(new { error = ex.Message });
        }

        using var memoryStream = new MemoryStream();
        using (var archive = new ZipArchive(memoryStream, ZipArchiveMode.Create, true))
        {
            foreach (var file in result.Files)
            {
                var entry = archive.CreateEntry(file.Key);
                using var entryStream = entry.Open();
                using var streamWriter = new StreamWriter(entryStream, Encoding.UTF8);
                streamWriter.Write(file.Value);
            }

            // Uyarılar ZIP'in İÇİNE de yazılıyor: dosyayı bir hafta sonra açan kişi
            // hangi yapıların taşınmadığını hatırlamaz, ve o bilgi olmadan üretilen
            // kodu şemanın tam karşılığı sanar.
            if (result.Warnings.Count > 0)
            {
                var entry = archive.CreateEntry("NAMINES-WARNINGS.txt");
                using var entryStream = entry.Open();
                using var streamWriter = new StreamWriter(entryStream, Encoding.UTF8);
                streamWriter.WriteLine($"{generator.DisplayName} could not express the following:");
                streamWriter.WriteLine();
                foreach (var warning in result.Warnings) streamWriter.WriteLine($"  - {warning}");
            }
        }

        memoryStream.Position = 0;
        var safeTarget = target.Replace('.', '-');
        return File(memoryStream.ToArray(), "application/zip",
            $"{request.Schema.Name ?? "Schema"}_{safeTarget}.zip");
    }

    // ── NSL (04-NSL-SCHEMA-IR.md) ────────────────────────────────────────────

    /// <summary>
    /// <c>.nsl</c> metnini şemaya çevirir ve doğrular.
    ///
    /// Ayrıştırma hatası 400 döner ve SATIR NUMARASINI taşır — "geçersiz NSL"
    /// tek başına kullanıcıya hiçbir şey söylemez.
    /// </summary>
    [HttpPost("nsl/parse")]
    public IActionResult ParseNsl([FromBody] NslParseRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Text))
            return BadRequest(new { error = "NSL text is required." });

        try
        {
            var schema = NslParser.Parse(request.Text);
            var findings = NslValidator.Validate(schema, request.DbType);

            return Ok(new { schema, findings });
        }
        catch (NslParseException ex)
        {
            return BadRequest(new { error = ex.Message, line = ex.Line });
        }
    }

    /// <summary>Şemayı doğrular; metne çevirmeden yalnızca bulguları döndürür.</summary>
    [HttpPost("nsl/validate")]
    public IActionResult ValidateSchema([FromBody] CompileRequest request)
    {
        if (request.Schema == null) return BadRequest("Schema is required.");

        var findings = NslValidator.Validate(request.Schema, request.DbType);

        return Ok(new
        {
            findings,
            // Özet, arayüzün bulguları saymak zorunda kalmaması için: "3 hata,
            // 5 uyarı" cümlesi listeyi açmadan karar vermeyi sağlıyor.
            errors = findings.Count(f => f.Severity == "error"),
            warnings = findings.Count(f => f.Severity == "warning"),
            infos = findings.Count(f => f.Severity == "info"),
        });
    }
}
