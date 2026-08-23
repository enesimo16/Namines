using System.Collections.Generic;
using System.Linq;
using Namines.Core.Models;

namespace Namines.Core.Analysis;

/// <summary>
/// Bir kolonun değerini veritabanının üretip üretmeyeceği (04 §3 <c>identity</c>).
///
/// <b>Tek karar noktası, altı motor.</b> Bu kural bugüne kadar altı DDL
/// üreticisinin her birinde ayrı ayrı yazılıydı ve hepsi aynı şeyi söylüyordu:
/// "tek kolonlu tamsayı birincil anahtar → otomatik artan". Aynı kuralın altı
/// kopyası, biri değiştiğinde diğerlerinin sessizce ayrışması demektir; üstelik
/// kuralın kendisi bir VARSAYIMDI — kullanıcının "hayır" deme yolu yoktu.
///
/// Artık <see cref="SchemaColumn.Identity"/> açıkça söylenmişse o geçerli;
/// söylenmemişse eski çıkarım korunuyor (mevcut şemalar bozulmasın diye).
/// </summary>
public static class IdentityPolicy
{
    /// <param name="primaryKeyCount">
    /// Tablodaki birincil anahtar kolonu sayısı. Bileşik anahtarda otomatik artan
    /// UYGULANMAZ: motorların çoğu tabloda tek bir otomatik kolona izin verir
    /// (SQL Server Msg 2744, Oracle ORA-30673) ve ikisine birden vermek DDL'i
    /// çalıştırılamaz hâle getirir.
    /// </param>
    public static bool IsGenerated(SchemaColumn column, int primaryKeyCount)
    {
        // Değeri bir İFADEDEN gelen kolon aynı anda otomatik artan OLAMAZ: ikisi
        // de "bu değeri kim koyuyor" sorusuna cevap veriyor ve iki cevap birden
        // olamaz. PostgreSQL bunu `SERIAL GENERATED ALWAYS AS (...)` ile
        // reddediyor; SQLite ise sessizce ifadeyi düşürüp kolonu boş bırakıyordu —
        // ikincisi daha kötü, çünkü hata veriler yazılana kadar görünmüyor.
        if (!string.IsNullOrWhiteSpace(column.Generated)) return false;

        if (column.Identity == true) return true;
        if (column.Identity == false) return false;

        return column.IsPK && primaryKeyCount == 1 && IsIntegerType(column.Type);
    }

    public static bool IsGenerated(SchemaTable table, SchemaColumn column) =>
        IsGenerated(column, table.Columns.Count(c => c.IsPK));

    /// <summary>
    /// Otomatik artan yalnızca tamsayı tiplerinde anlamlıdır; bir <c>uuid</c> ya da
    /// <c>varchar</c> anahtarı "artırmanın" karşılığı yoktur.
    /// </summary>
    public static bool IsIntegerType(string? type)
    {
        var normalized = (type ?? string.Empty).Trim().ToUpperInvariant();
        return normalized is "INT" or "INTEGER" or "BIGINT" or "SMALLINT";
    }
}
