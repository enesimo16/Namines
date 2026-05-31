using System.IO;
using System.IO.Compression;
using System.Text;
using Microsoft.AspNetCore.Mvc;
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
        
        return Ok(new { sql });
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
