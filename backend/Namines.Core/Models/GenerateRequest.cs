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

    /// <summary>
    /// Netleştirme sorularının cevapları, JSON sözlük olarak
    /// (<c>{"scale":"Büyük","auth":"Evet, roller ve izinlerle"}</c>).
    ///
    /// Boş bırakılabilir: kullanıcı soruları atlarsa varsayılan cevaplar
    /// kullanılır. Zorunlu kılmak, hızlı bir taslak isteyen kullanıcıyı forma
    /// mahkûm etmek olurdu.
    /// </summary>
    public string? Answers { get; set; }
}

/// <param name="Prompt">Kullanıcının ilk cümlesi.</param>
public sealed record ClarifyRequest(string Prompt);
