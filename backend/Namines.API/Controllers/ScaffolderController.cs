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
            _logger.LogWarning("Scaffolder: Invalid schema request - Schema is null");
            return BadRequest(new { error = "Schema details cannot be empty" });
        }

        _logger.LogInformation("Scaffolder: Full-stack project generation request received. Schema: {Name}", schema.Name);

        try
        {
            byte[] zipBytes = await _scaffolderService.GenerateFullStackProjectAsync(schema);
            return File(zipBytes, "application/zip", "namines-project.zip");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Scaffolder: Error occurred during project generation and packaging.");
            return StatusCode(500, new { error = $"Project generation error: {ex.Message}" });
        }
    }

    [HttpPost("export/python")]
    public async Task<IActionResult> ExportPythonProject([FromBody] DatabaseSchema schema)
    {
        if (schema == null)
        {
            _logger.LogWarning("Scaffolder: Invalid schema request - Schema is null");
            return BadRequest(new { error = "Schema details cannot be empty" });
        }

        _logger.LogInformation("Scaffolder: Python Freemium project generation request received. Schema: {Name}", schema.Name);

        try
        {
            byte[] zipBytes = await _scaffolderService.GeneratePythonFreemiumProjectAsync(schema);
            return File(zipBytes, "application/zip", "namines-python-crud.zip");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Scaffolder: Error occurred during Python project generation and packaging.");
            return StatusCode(500, new { error = $"Project generation error: {ex.Message}" });
        }
    }
}
