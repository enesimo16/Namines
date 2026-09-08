using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Namines.Core.Interfaces;
using Namines.Core.Models;

namespace Namines.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ReverseEngineerController : ControllerBase
{
    private readonly IAIService _aiService;
    private readonly ILogger<ReverseEngineerController> _logger;

    // Allowed image formats
    private static readonly string[] AllowedContentTypes = { "image/jpeg", "image/png", "image/webp" };
    // Max file size: 10MB
    private const long MaxFileSizeBytes = 10 * 1024 * 1024; 

    public ReverseEngineerController(IAIService aiService, ILogger<ReverseEngineerController> logger)
    {
        _aiService = aiService;
        _logger = logger;
    }

    [HttpPost("analyze")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> AnalyzeImage(IFormFile image)
    {
        if (image == null || image.Length == 0)
        {
            _logger.LogWarning("ReverseEngineer: Yüklenen resim boş veya geçersiz.");
            return BadRequest(new { error = "Upload a valid image." });
        }

        // Validate format/content type
        var contentType = image.ContentType.ToLowerInvariant();
        if (!AllowedContentTypes.Contains(contentType))
        {
            _logger.LogWarning("ReverseEngineer: Desteklenmeyen resim formatı: {ContentType}", contentType);
            return BadRequest(new { error = "Unsupported image format. Upload a JPEG, PNG, or WebP file." });
        }

        // Validate file size
        if (image.Length > MaxFileSizeBytes)
        {
            _logger.LogWarning("ReverseEngineer: Dosya boyutu limiti aşıldı ({Size} bytes).", image.Length);
            return BadRequest(new { error = "The image cannot be larger than 10MB." });
        }

        _logger.LogInformation("ReverseEngineer: Görsel analiz talebi alındı. Dosya Adı: {FileName}, Boyut: {Size} bytes", 
            image.FileName, image.Length);

        bool forceLocal = HttpContext.Items.ContainsKey("FallbackToLocal") && HttpContext.Items["FallbackToLocal"] is true;
        if (forceLocal)
        {
            _logger.LogInformation("ReverseEngineer: Local Fallback active: returning basic template schema.");
            var fallbackSchema = new DatabaseSchema
            {
                Name = "ReverseEngineered_LocalFallback",
                Tables = new System.Collections.Generic.List<SchemaTable>
                {
                    new SchemaTable
                    {
                        Id = Guid.NewGuid().ToString(),
                        Name = "users",
                        Columns = new System.Collections.Generic.List<SchemaColumn>
                        {
                            new SchemaColumn { Id = Guid.NewGuid().ToString(), Name = "id", Type = "int", IsPK = true, IsNullable = false },
                            new SchemaColumn { Id = Guid.NewGuid().ToString(), Name = "username", Type = "varchar", Length = 100, IsNullable = false },
                            new SchemaColumn { Id = Guid.NewGuid().ToString(), Name = "email", Type = "varchar", Length = 150, IsNullable = true }
                        }
                    },
                    new SchemaTable
                    {
                        Id = Guid.NewGuid().ToString(),
                        Name = "orders",
                        Columns = new System.Collections.Generic.List<SchemaColumn>
                        {
                            new SchemaColumn { Id = Guid.NewGuid().ToString(), Name = "id", Type = "int", IsPK = true, IsNullable = false },
                            new SchemaColumn { Id = Guid.NewGuid().ToString(), Name = "user_id", Type = "int", IsFK = true, IsNullable = false },
                            new SchemaColumn { Id = Guid.NewGuid().ToString(), Name = "amount", Type = "decimal", IsNullable = false }
                        }
                    }
                },
                Relations = new System.Collections.Generic.List<SchemaRelation>
                {
                    new SchemaRelation
                    {
                        Id = Guid.NewGuid().ToString(),
                        Type = "OneToMany",
                        SourceTableId = "", // resolved on frontend if needed or left empty
                        SourceColumnId = "",
                        TargetTableId = "",
                        TargetColumnId = ""
                    }
                }
            };
            return Ok(fallbackSchema);
        }

        try
        {
            using var ms = new MemoryStream();
            await image.CopyToAsync(ms);
            var imageBytes = ms.ToArray();

            var parsedSchema = await _aiService.AnalyzeImageAsync(imageBytes, contentType);

            if (parsedSchema == null || parsedSchema.Tables == null || parsedSchema.Tables.Count == 0)
            {
                _logger.LogWarning("ReverseEngineer: AI görselden geçerli bir şema üretemedi.");
                return BadRequest(new { error = "No table structure could be read from the image. Make sure the lines and text are clear." });
            }

            return Ok(parsedSchema);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ReverseEngineer: Görsel çözümleme sırasında hata oluştu.");
            return StatusCode(500, new { error = $"Whiteboard analysis failed: {ex.Message}" });
        }
    }
}
