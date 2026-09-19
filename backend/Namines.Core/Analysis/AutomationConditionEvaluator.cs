using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Namines.Core.Analysis;

/// <summary>Bir tetiklenmenin somut bağlamı — koşullar bu değerlere bakar.</summary>
public readonly record struct AutomationTriggerContext(string? TableName, string? ColumnName, string? ColumnType);

/// <summary>
/// <c>AutomationRule.ConditionsJson</c>'ı değerlendiren saf fonksiyon.
///
/// Biçim: <c>[{"field":"columnName","op":"endsWith","value":"_id"}]</c>.
/// Koşullar arasında VE mantığı var; boş dizi her zaman eşleşir.
///
/// <b>Neden regex YOK:</b> desen kullanıcıdan geliyor ve sunucuda
/// çalıştırılıyor — kötü kurgulanmış bir regex katastrofik geri izlemeyle
/// (ReDoS) iş parçacığını kilitleyebilir. Dört metin operatörü pratikteki
/// ihtiyacın tamamına yakınını karşılıyor; gerçekten desen gerekirse
/// sınırlı bir glob eklenir, serbest regex değil.
/// </summary>
public static class AutomationConditionEvaluator
{
    public static bool Matches(string? conditionsJson, AutomationTriggerContext context)
    {
        var conditions = Parse(conditionsJson);
        if (conditions.Count == 0) return true;

        foreach (var condition in conditions)
        {
            if (!Evaluate(condition, context)) return false;
        }
        return true;
    }

    /// <summary>
    /// Bozuk/okunamayan JSON BOŞ koşul listesi sayılıyor, yani kural koşulsuz
    /// çalışıyor. Alternatif (hiç eşleşmeme) kuralı sessizce ölü hâle
    /// getirirdi; kullanıcı kuralı kurmuş ve etkin bırakmışken çalışmaması,
    /// fazladan çalışmasından daha zor fark edilir.
    /// </summary>
    private static List<AutomationCondition> Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new List<AutomationCondition>();
        try
        {
            return JsonSerializer.Deserialize<List<AutomationCondition>>(json, JsonOptions)
                   ?? new List<AutomationCondition>();
        }
        catch (JsonException)
        {
            return new List<AutomationCondition>();
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private static bool Evaluate(AutomationCondition condition, AutomationTriggerContext context)
    {
        var actual = condition.Field?.ToLowerInvariant() switch
        {
            "tablename" => context.TableName,
            "columnname" => context.ColumnName,
            "columntype" => context.ColumnType,
            _ => null,
        };

        // Tanınmayan alan ya da bu tetikleyicide karşılığı olmayan bir alan
        // (ör. tablo olayında columnName) koşulu DÜŞÜRÜR. Aksi hâlde anlamsız
        // bir koşul sessizce yok sayılır ve kural kullanıcının sandığından
        // daha geniş tetiklenirdi.
        if (actual is null) return false;

        var expected = condition.Value ?? string.Empty;
        var cmp = StringComparison.OrdinalIgnoreCase;

        return condition.Op?.ToLowerInvariant() switch
        {
            "equals" => actual.Equals(expected, cmp),
            "notequals" => !actual.Equals(expected, cmp),
            "contains" => actual.Contains(expected, cmp),
            "startswith" => actual.StartsWith(expected, cmp),
            "endswith" => actual.EndsWith(expected, cmp),
            _ => false,
        };
    }

    private sealed class AutomationCondition
    {
        public string? Field { get; set; }
        public string? Op { get; set; }
        public string? Value { get; set; }
    }
}
