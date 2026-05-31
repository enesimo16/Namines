using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Namines.Core.Interfaces;
using Namines.Core.Models;

namespace Namines.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ScaffolderController : ControllerBase
{
    private readonly IScaffolderService _scaffolderService;
    private readonly ILogger<ScaffolderController> _logger;

    public ScaffolderController(IScaffolderService scaffolderService, ILogger<ScaffolderController> logger)
    {
        _scaffolderService = scaffolderService;
        _logger = logger;
    }

    [HttpPost("export")]
    public async Task<IActionResult> ExportProject([FromBody] DatabaseSchema schema)
    {
        if (schema == null)
        {
            _logger.LogWarning("Scaffolder: Geçersiz şema isteği - Schema null");
            return BadRequest(new { error = "Şema bilgisi boş olamaz" });
        }

        _logger.LogInformation("Scaffolder: Full-stack proje üretim talebi alındı. Şema: {Name}", schema.Name);

        try
        {
            byte[] zipBytes = await _scaffolderService.GenerateFullStackProjectAsync(schema);
            return File(zipBytes, "application/zip", "namines-project.zip");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Scaffolder: Proje üretimi ve paketlenmesi sırasında hata oluştu.");
            return StatusCode(500, new { error = $"Proje üretimi hatası: {ex.Message}" });
        }
    }
}
