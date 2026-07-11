using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Namines.Core.Models;
using Namines.Infrastructure.Data;

namespace Namines.API.Controllers;

// Geri bildirim herkese açık (misafir de gönderebilir); spam'e karşı rate-limit.
[ApiController]
[Route("api/[controller]")]
[EnableRateLimiting("sensitive")]
public class FeedbackController : ControllerBase
{
    private readonly AuthDbContext _context;

    public FeedbackController(AuthDbContext context)
    {
        _context = context;
    }

    public class FeedbackRequest
    {
        public string? Email { get; set; }
        public string? Category { get; set; }
        public string Message { get; set; } = string.Empty;
    }

    [HttpPost]
    public async Task<IActionResult> Submit([FromBody] FeedbackRequest request)
    {
        var message = request.Message?.Trim() ?? string.Empty;
        if (message.Length < 3)
            return BadRequest(new { message = "Geri bildirim çok kısa." });
        if (message.Length > 4000)
            message = message.Substring(0, 4000);

        var category = request.Category switch
        {
            "bug" or "idea" or "general" => request.Category,
            _ => "general"
        };

        var feedback = new Feedback
        {
            UserId = User.FindFirstValue(ClaimTypes.NameIdentifier),
            Email = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim(),
            Category = category!,
            Message = message,
            CreatedAt = DateTime.UtcNow
        };

        await _context.Feedbacks.AddAsync(feedback);
        await _context.SaveChangesAsync();

        return Ok(new { message = "Geri bildiriminiz için teşekkürler!" });
    }
}
