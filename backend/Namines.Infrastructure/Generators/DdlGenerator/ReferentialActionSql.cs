using Namines.Core.Enums;

namespace Namines.Infrastructure.Generators.DdlGenerator;

/// <summary>
/// <see cref="ReferentialAction"/> değerini motora özgü SQL cümleciğine çevirir.
///
/// Motorlar aynı fiilleri desteklemez; desteklenmeyen bir fiili olduğu gibi yazmak
/// çalıştırılamayan DDL üretir. Bu sınıf farkları tek yerde toplar:
///
///   | Fiil        | MSSQL | PostgreSQL | MySQL/MariaDB | SQLite | Oracle |
///   |-------------|-------|------------|---------------|--------|--------|
///   | NO ACTION   |  ✔    |     ✔      |      ✔        |   ✔    |   ✔    |
///   | RESTRICT    |  ✖    |     ✔      |      ✔        |   ✔    |   ✖    |
///   | CASCADE     |  ✔    |     ✔      |      ✔        |   ✔    |   ✔    |
///   | SET NULL    |  ✔    |     ✔      |      ✔        |   ✔    |   ✔    |
///   | SET DEFAULT |  ✔    |     ✔      |    ayrıştırır |   ✔    |   ✖    |
///   | ON UPDATE   |  ✔    |     ✔      |      ✔        |   ✔    |   ✖    |
///
/// Desteklenmeyen fiiller en yakın güvenli karşılığa düşer (RESTRICT → NO ACTION);
/// veri kaybettirecek bir yöne ASLA düşülmez.
/// </summary>
internal static class ReferentialActionSql
{
    /// <summary>
    /// ON DELETE cümleciğini döndürür. NO ACTION zaten SQL varsayılanı olduğu için
    /// boş string döner — çıktı gereksiz gürültüyle dolmaz.
    /// </summary>
    public static string OnDelete(ReferentialAction action, DatabaseType engine)
    {
        var verb = Verb(action, engine);
        return string.IsNullOrEmpty(verb) ? string.Empty : $"ON DELETE {verb}";
    }

    /// <summary>
    /// ON UPDATE cümleciğini döndürür. Oracle ON UPDATE'i hiç desteklemez → her zaman boş.
    /// </summary>
    public static string OnUpdate(ReferentialAction action, DatabaseType engine)
    {
        if (engine == DatabaseType.Oracle) return string.Empty;

        var verb = Verb(action, engine);
        return string.IsNullOrEmpty(verb) ? string.Empty : $"ON UPDATE {verb}";
    }

    private static string Verb(ReferentialAction action, DatabaseType engine) => action switch
    {
        // Varsayılan davranış — açıkça yazmaya gerek yok.
        ReferentialAction.NoAction => string.Empty,

        // MSSQL ve Oracle RESTRICT bilmez. Anlamı NO ACTION'a çok yakındır
        // (fark yalnızca kontrolün ertelenip ertelenmediğidir), güvenle düşürülebilir.
        ReferentialAction.Restrict => engine is DatabaseType.MSSQL or DatabaseType.Oracle
            ? string.Empty
            : "RESTRICT",

        ReferentialAction.Cascade => "CASCADE",

        ReferentialAction.SetNull => "SET NULL",

        // Oracle SET DEFAULT desteklemez. CASCADE'e düşmek veri kaybettirir,
        // bu yüzden en kısıtlayıcı davranışa (NO ACTION) düşülür.
        ReferentialAction.SetDefault => engine == DatabaseType.Oracle
            ? string.Empty
            : "SET DEFAULT",

        _ => string.Empty
    };

    /// <summary>
    /// FK satırının sonuna eklenecek tüm cümlecikleri birleştirir (boş olanları atlar).
    /// Örn. " ON DELETE CASCADE ON UPDATE SET NULL" veya "" (ikisi de varsayılansa).
    /// </summary>
    public static string Clauses(ReferentialAction onDelete, ReferentialAction onUpdate, DatabaseType engine)
    {
        var parts = new[] { OnDelete(onDelete, engine), OnUpdate(onUpdate, engine) }
            .Where(p => !string.IsNullOrEmpty(p));

        var joined = string.Join(" ", parts);
        return string.IsNullOrEmpty(joined) ? string.Empty : " " + joined;
    }
}
