using Microsoft.AspNetCore.Http;
using Namines.Core.Enums;

namespace Namines.Core.Models;

public class GenerateRequest
{
    public string Prompt { get; set; } = string.Empty;
    public DatabaseType DbType { get; set; }
    public string AIProvider { get; set; } = "Groq";
    public string ModelName { get; set; } = string.Empty;
    public IFormFile? Image { get; set; }
    public string? ReferenceUrl { get; set; }
}
