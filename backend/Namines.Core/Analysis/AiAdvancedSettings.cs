using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Namines.Core.Analysis;

/// <summary>
/// Kullanıcının "Advanced AI Tuning" ayarları.
///
/// <b>Neden bu tip var:</b> bu ayarlar arayüzde vardı ama YALNIZCA
/// <c>localStorage</c>'a yazılıyordu ve hiçbir yerde okunmuyordu — on bir ayarın
/// tamamı süstü. Kullanıcı "SQL Schema Naming Standard: snake_case" seçip
/// kaydediyor, sonuç hiç değişmiyordu. Ayar göstermek onu uygulamak demektir;
/// uygulanmayan ayar, kullanıcıya yalan söyler.
///
/// <b>Tek JSON kolonunda tutuluyor</b>, on bir ayrı kolonda değil: bunlar
/// üzerinde sorgu çalıştırılmayan, yalnızca kullanıcı başına okunup prompt'a
/// yazılan tercihler. Her yeni tercih için migration gerektirmesi, ayar eklemeyi
/// gereksiz pahalı yapardı.
/// </summary>
public sealed record AiAdvancedSettings
{
    /// <summary>Mock veri üretiminde kullanılacak sektör bağlamı.</summary>
    [JsonPropertyName("seedDomain")]
    public string SeedDomain { get; init; } = "general";

    /// <summary>Üretilen dokümantasyonun teknik derinliği.</summary>
    [JsonPropertyName("docLevel")]
    public string DocLevel { get; init; } = "standard";

    /// <summary>Scaffolding çıktısının hedef framework'ü.</summary>
    [JsonPropertyName("scaffoldVersion")]
    public string ScaffoldVersion { get; init; } = ".net8";

    /// <summary>DBA analizinde raporlanacak en düşük önem derecesi.</summary>
    [JsonPropertyName("dbaSeverity")]
    public string DbaSeverity { get; init; } = "warning";

    /// <summary>
    /// Şema üretiminde modelin yaratıcılık düzeyi.
    ///
    /// <b>Yalnızca şema üretimini etkiliyor.</b> Diğer özelliklerin sıcaklıkları
    /// bilerek ayrı ayrı ayarlanmış (ör. etki açıklayıcı 0.0 kullanıyor ki bulgu
    /// icat etmesin); onları tek bir global değerle ezmek, o gerekçeleri sessizce
    /// çöpe atmak olurdu.
    /// </summary>
    [JsonPropertyName("temperature")]
    public string Temperature { get; init; } = "0.2";

    /// <summary>Üretilen kodun yorum/isimlendirme yoğunluğu.</summary>
    [JsonPropertyName("promptStyle")]
    public string PromptStyle { get; init; } = "clean";

    /// <summary>Tablo ve kolon adlarının yazım biçimi.</summary>
    [JsonPropertyName("namingConvention")]
    public string NamingConvention { get; init; } = "snake_case";

    /// <summary>
    /// Yabancı anahtarların varsayılan silme davranışı.
    ///
    /// Varsayılan <c>restrict</c>, <c>cascade</c> DEĞİL. Bu kod tabanının kuralı:
    /// varsayılan asla veri kaybına doğru düşmemeli. CASCADE'i varsayılan yapmak,
    /// hiçbir ayara dokunmamış bir kullanıcının bir satırı silerken ilişkili tüm
    /// kayıtları da sessizce silmesi demek olurdu.
    /// </summary>
    [JsonPropertyName("fkAction")]
    public string FkAction { get; init; } = "restrict";

    /// <summary>Şema üretiminde modelin üretebileceği en fazla token.</summary>
    [JsonPropertyName("maxTokens")]
    public string MaxTokens { get; init; } = "4096";

    /// <summary>Yabancı anahtarlar için otomatik index önerilsin mi.</summary>
    [JsonPropertyName("autoIndex")]
    public string AutoIndex { get; init; } = "true";

    /// <summary>Üretilen SQL girintilenip biçimlendirilsin mi.</summary>
    [JsonPropertyName("sqlPrettyPrint")]
    public string SqlPrettyPrint { get; init; } = "true";

    public static AiAdvancedSettings Default { get; } = new();

    /// <summary>
    /// JSON'dan okur; bozuk ya da boşsa varsayılanlara düşer.
    ///
    /// Bozuk JSON isteği REDDETTİRMİYOR: bunlar tercih, zorunluluk değil.
    /// Kullanıcının şema üretme isteğinin, kayıtlı bir tercih satırı bozuk diye
    /// tamamen düşmesi orantısız olurdu.
    /// </summary>
    public static AiAdvancedSettings Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Default;

        try
        {
            return JsonSerializer.Deserialize<AiAdvancedSettings>(json) ?? Default;
        }
        catch (JsonException)
        {
            return Default;
        }
    }

    public string ToJson() => JsonSerializer.Serialize(this);

    /// <summary>
    /// Şema üretiminde kullanılacak sıcaklık.
    ///
    /// Aralık dışı ya da ayrıştırılamayan değer varsayılana düşer: kullanıcıdan
    /// gelen bir metnin doğrudan sağlayıcıya gitmesi, sağlayıcının isteği
    /// tamamen reddetmesine yol açardı.
    /// </summary>
    public double TemperatureValue =>
        double.TryParse(Temperature, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var t) && t >= 0 && t <= 2
            ? t
            : 0.2;

    /// <summary>Şema üretiminde izin verilen en fazla çıktı token'ı.</summary>
    public int MaxTokensValue =>
        int.TryParse(MaxTokens, out var m) && m >= 256 && m <= 32_000 ? m : 4096;

    /// <summary>
    /// Bu tercihlerin prompt'a yazılacak hâli — üretimi gerçekten etkileyen kısım.
    ///
    /// Yalnızca ÜRETİLEN ŞEMAYI değiştiren tercihler yazılıyor. Dokümantasyon
    /// derinliği ya da scaffolding framework'ü gibi başka özelliklere ait
    /// tercihleri de buraya eklemek, her şema isteğinde alakasız talimatlarla
    /// token harcamak olurdu.
    /// </summary>
    public string ToSchemaPromptContext()
    {
        var naming = NamingConvention switch
        {
            "PascalCase" => "PascalCase (e.g. OrderItem, CreatedAt)",
            "camelCase" => "camelCase (e.g. orderItem, createdAt)",
            _ => "snake_case (e.g. order_item, created_at)",
        };

        var fk = FkAction switch
        {
            "cascade" => "CASCADE",
            "set_null" => "SET NULL",
            _ => "RESTRICT / NO ACTION",
        };

        var lines = new System.Collections.Generic.List<string>
        {
            $"- Name tables and columns in {naming}.",
            $"- Default foreign key ON DELETE behaviour: {fk}.",
        };

        if (string.Equals(AutoIndex, "true", StringComparison.OrdinalIgnoreCase))
            lines.Add("- Add an index on every foreign key column.");
        else
            lines.Add("- Do not add indexes unless they are explicitly required.");

        if (PromptStyle == "minimalist")
            lines.Add("- Keep the schema compact: no optional convenience columns.");
        else if (PromptStyle == "documented")
            lines.Add("- Prefer explicit, self-describing column names over short ones.");

        return string.Join("\n", lines);
    }
}
