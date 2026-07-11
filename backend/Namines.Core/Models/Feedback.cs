using System;

namespace Namines.Core.Models;

/// <summary>Kullanıcı geri bildirimi (misafir veya giriş yapmış kullanıcı).</summary>
public class Feedback
{
    public int Id { get; set; }
    public string? UserId { get; set; }        // giriş yapmışsa
    public string? Email { get; set; }         // opsiyonel iletişim
    public string Category { get; set; } = "general"; // bug | idea | general
    public string Message { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
