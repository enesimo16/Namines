using System.IO;
using System.IO.Compression;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Namines.Core.Analysis;
using Namines.Core.Interfaces;
using Namines.Core.Models;
using Namines.Infrastructure.Generators.DdlGenerator;

namespace Namines.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class CompileController : ControllerBase
{
    private readonly IDdlGeneratorFactory _ddlFactory;
    private readonly IEfCoreGenerator _efCoreGenerator;
    private readonly IPrismaGenerator _prismaGenerator;

    public CompileController(
        IDdlGeneratorFactory ddlFactory,
        IEfCoreGenerator efCoreGenerator,
        IPrismaGenerator prismaGenerator)
    {
        _ddlFactory = ddlFactory;
        _efCoreGenerator = efCoreGenerator;
        _prismaGenerator = prismaGenerator;
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
}
