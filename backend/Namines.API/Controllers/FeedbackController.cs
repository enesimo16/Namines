using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Namines.Ops.Application;

namespace Namines.API.Controllers;

// Geri bildirim herkese açık (misafir de gönderebilir); spam'e karşı rate-limit.
[ApiController]
[Route("api/[controller]")]
[EnableRateLimiting("sensitive")]
public class FeedbackController : ControllerBase
{
    private readonly IFeedbackService _feedback;

    public FeedbackController(IFeedbackService feedback)
    {
        _feedback = feedback;
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
        var accepted = await _feedback.SubmitAsync(new FeedbackSubmission(
            User.FindFirstValue(ClaimTypes.NameIdentifier), request.Email, request.Category, request.Message));
        if (!accepted)
            return BadRequest(new { message = "Your feedback is too short." });

        return Ok(new { message = "Thanks for your feedback." });
    }
}
