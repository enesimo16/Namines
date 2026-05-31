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
            return BadRequest(new { error = "Lütfen geçerli bir görsel yükleyin." });
        }

        // Validate format/content type
        var contentType = image.ContentType.ToLower();
        if (!AllowedContentTypes.Contains(contentType))
        {
            _logger.LogWarning("ReverseEngineer: Desteklenmeyen resim formatı: {ContentType}", contentType);
            return BadRequest(new { error = "Desteklenmeyen görsel formatı. Sadece JPEG, PNG veya WebP yükleyebilirsiniz." });
        }

        // Validate file size
        if (image.Length > MaxFileSizeBytes)
        {
            _logger.LogWarning("ReverseEngineer: Dosya boyutu limiti aşıldı ({Size} bytes).", image.Length);
            return BadRequest(new { error = "Görsel boyutu 10MB sınırını aşamaz." });
        }

        _logger.LogInformation("ReverseEngineer: Görsel analiz talebi alındı. Dosya Adı: {FileName}, Boyut: {Size} bytes", 
            image.FileName, image.Length);

        try
        {
            using var ms = new MemoryStream();
            await image.CopyToAsync(ms);
            var imageBytes = ms.ToArray();

            var parsedSchema = await _aiService.AnalyzeImageAsync(imageBytes, contentType);

            if (parsedSchema == null || parsedSchema.Tables == null || parsedSchema.Tables.Count == 0)
            {
                _logger.LogWarning("ReverseEngineer: AI görselden geçerli bir şema üretemedi.");
                return BadRequest(new { error = "Görselden tablo yapısı çözümlenemedi. Lütfen çizgilerin ve yazıların net olduğundan emin olun." });
            }

            return Ok(parsedSchema);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ReverseEngineer: Görsel çözümleme sırasında hata oluştu.");
            return StatusCode(500, new { error = $"Beyaz tahta çözümleme hatası: {ex.Message}" });
        }
    }
}
