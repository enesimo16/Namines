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

    public CompileController(IDdlGeneratorFactory ddlFactory, IEfCoreGenerator efCoreGenerator)
    {
        _ddlFactory = ddlFactory;
        _efCoreGenerator = efCoreGenerator;
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
}
