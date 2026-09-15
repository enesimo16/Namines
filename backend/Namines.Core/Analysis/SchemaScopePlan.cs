using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace Namines.Core.Analysis;

/// <param name="Name">Alan adı — "Identity", "Catalog" gibi kullanıcıya gösterilen bir grup başlığı.</param>
/// <param name="Tables">Bu alana ait tablo adları. Kimlik değil — kimlikler <see cref="SchemaIdConvention"/> ile türetilir.</param>
public sealed record SchemaScopeDomain(string Name, IReadOnlyList<string> Tables);

/// <summary>
/// "Plan" turunun ürettiği yapısal kapsam — hangi alanlarda hangi tabloların
/// üretileceği, serbest metin değil JSON olarak.
///
/// <b>Neden serbest metin değil:</b> eski plan turu 12 satırla sınırlı düzyazıydı
/// ve bu, üretim hattının kapsamını daha ilk turda küçük bir sisteme
/// kilitliyordu — kullanıcı 50-60 tablolu kapsamlı bir sistem istese bile.
/// Yapısal bir liste hem sınırsız büyüyebilir hem de sonraki bir adımın büyük
/// bir şemayı birkaç paralel üretim çağrısına ("chunk") bölmesine izin verir —
/// bu, o bölme adımının GİRDİSİ.
///
/// <b>Bu tür isim çakışması yüzünden "SchemaPlan" değil "SchemaScopePlan":</b>
/// <see cref="PlanBuilder"/>'daki mevcut <c>SchemaPlan</c> zaten netleştirme
/// turunun (archetype, assumptions, follow-up) sonucunu taşıyor ve çalışan,
/// test edilmiş kod. Onu bu yeni, ilgisiz kavrama yer açmak için yeniden
/// adlandırmak yanlış yön olurdu — üstelik bu tür zaten "şema planı" değil,
/// üretimin KAPSAMINI tanımlıyor; ad kendi başına da daha doğru.
/// </summary>
/// <param name="SchemaName">Üretilecek şemanın adı.</param>
/// <param name="Domains">Alan başına tablo grupları — tekilleştirilmiş, boş alan içermez.</param>
public sealed record SchemaScopePlan(string SchemaName, IReadOnlyList<SchemaScopeDomain> Domains)
{
    /// <summary>Toplam tablo sayısı — sonraki adımın büyük/küçük kapsam kararını buna göre vermesi için.</summary>
    public int TableCount => AllTableNames.Count;

    /// <summary>Alan sırasına göre, tekilleştirilmiş tüm tablo adları.</summary>
    public IReadOnlyList<string> AllTableNames => Domains.SelectMany(d => d.Tables).ToList();

    /// <summary>
    /// Planın düz metin gösterimi — kullanıcıya onay için gösterilecekse ya da
    /// eski (plansız) yolun beklediği düzyazı formatına düşürülecekse.
    /// </summary>
    public string RenderAsText() =>
        string.Join("\n", Domains.Select(d => $"- {d.Name}: {string.Join(", ", d.Tables)}"));

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>
    /// JSON'dan plan üretmeyi dener. <b>Asla fırlatmaz</b> — burada dönen <c>null</c>,
    /// çağıran tarafta "plan yok, mevcut plansız yola düş" anlamına geliyor;
    /// plan turu isteğe bağlı bir iyileştirme, zorunlu bir adım değil.
    /// </summary>
    public static SchemaScopePlan? TryParse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;

        // Model talimata rağmen ```json ... ``` ile sarabiliyor; ilk `{` ile
        // son `}` arasını almak bu sarmalı, başka hiçbir özel durum eklemeden
        // düşürüyor.
        var start = json.IndexOf('{');
        var end = json.LastIndexOf('}');
        if (start < 0 || end < start) return null;

        var candidate = json[start..(end + 1)];

        RawPlan? raw;
        try
        {
            raw = JsonSerializer.Deserialize<RawPlan>(candidate, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }

        if (raw?.Domains is not { Count: > 0 }) return null;

        // Aynı tablo iki alanda geçebilir — model bazen "users" tablosunu hem
        // Identity hem Security altına yazıyor. İlk geçen kazanır: alanların
        // sırası modelin önceliğini yansıtıyor, sonraki tekrarlar gürültü.
        var seenKeys = new HashSet<string>();
        var domains = new List<SchemaScopeDomain>();

        foreach (var rawDomain in raw.Domains)
        {
            if (rawDomain?.Tables is null) continue;

            var cleanedTables = new List<string>();
            foreach (var table in rawDomain.Tables)
            {
                if (string.IsNullOrWhiteSpace(table)) continue;

                var key = SchemaIdConvention.Normalize(table);
                if (key.Length == 0 || !seenKeys.Add(key)) continue;

                cleanedTables.Add(table);
            }

            if (cleanedTables.Count > 0)
            {
                domains.Add(new SchemaScopeDomain(rawDomain.Name ?? string.Empty, cleanedTables));
            }
        }

        // Tekilleştirme sonrası hiçbir alanda tablo kalmadıysa (hepsi boştu
        // ya da hepsi çakışıp elendi), kullanılabilir bir plan yok demektir.
        if (domains.Count == 0) return null;

        return new SchemaScopePlan(raw.SchemaName ?? string.Empty, domains);
    }

    /// <summary>Ham JSON'ı doğrudan karşılayan ayrıştırma modeli — hesaplanan alanlar (TableCount vb.) burada yok.</summary>
    private sealed record RawPlan(string? SchemaName, List<RawDomain>? Domains);

    private sealed record RawDomain(string? Name, List<string>? Tables);
}
