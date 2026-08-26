using System.Collections.Generic;
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

/// <param name="Prompt">Kullanıcının ilk cümlesi — iş türünü belirlemek için.</param>
/// <param name="Answers">
/// O ana kadar toplanan tüm cevaplar (soru id → seçilen metin), takip
/// sorularının cevapları dahil (id'leri <c>"{soruId}.followup"</c> biçiminde).
/// Turlar arasında BİRİKTİRİLİYOR — her istek önceki turun cevaplarını da taşır.
/// </param>
/// <param name="Round">Kaçıncı netleştirme turu — en fazla 3 ek soru üretilir.</param>
public sealed record PlanRequest(string Prompt, Dictionary<string, string>? Answers, int Round = 1);
