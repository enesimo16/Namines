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
    /// <summary>
    /// Bir GraphQL uç noktası ya da OpenAPI/Swagger doküman adresi.
    ///
    /// <b>Eski adı "ReferenceUrl" idi ve sayfanın metnini kazıyordu.</b> Bu,
    /// üç yerden kırıktı ve second-phase/06-VERI-KAYNAKLARI.md'de tamamen
    /// kaldırıldı — bkz. <see cref="Namines.Infrastructure.Services.ApiSpecExtractor"/>.
    /// Alan adı değişti çünkü artık düz bir web sayfası DEĞİL, yapılandırılmış
    /// bir API tanımı bekleniyor.
    /// </summary>
    public string? ApiSpecUrl { get; set; }

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

/// <summary>second-phase/07-MOTOR-DONUSUMU.md — kayıp raporu isteği.</summary>
/// <param name="Schema">Kaynak motordaki mevcut şema.</param>
/// <param name="Source">Şemanın şu an yazıldığı motor.</param>
/// <param name="Target">Dönüştürülmek istenen motor.</param>
public sealed record ConvertAnalyzeRequest(DatabaseSchema Schema, DatabaseType Source, DatabaseType Target);

/// <summary>Kayıp raporundaki bulgular için kullanıcının seçtiği çözümler.</summary>
/// <param name="Schema">Kaynak şema (analiz uçuna verilenle aynı olmalı).</param>
/// <param name="Target">Dönüştürülecek motor.</param>
/// <param name="Resolutions">Bulgu id'si → seçilen seçenek key'i (ör. <c>"child_table"</c>).</param>
public sealed record ConvertApplyRequest(DatabaseSchema Schema, DatabaseType Source, DatabaseType Target, Dictionary<string, string> Resolutions);
