using Namines.Core.Enums;

namespace Namines.Core.Models;

public class SchemaDiffRequest
{
    public DatabaseSchema OldSchema { get; set; } = new();
    public DatabaseSchema NewSchema { get; set; } = new();

    /// <summary>
    /// Hedef motor — <see cref="Analysis.SchemaImpactAnalyzer"/>'ın cascade/kilit mesajlarını
    /// motora göre yazması için (ör. MSSQL'de "Msg 1785"). Belirtilmezse PostgreSQL varsayılır
    /// (mevcut çağıranlarla geriye uyumluluk için nullable — bkz. G9).
    /// </summary>
    public DatabaseType? DbType { get; set; }
}
