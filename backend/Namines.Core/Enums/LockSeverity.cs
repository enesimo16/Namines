namespace Namines.Core.Enums;

/// <summary>
/// Bir DDL operasyonunun tabloyu ne kadar süreyle kilitlemesi beklendiği.
/// new-phase/11-MIGRATIONS-BRANCHING.md §2'deki "Kilit (PG)" / "Kilit (MSSQL)" sütunlarının
/// motor-bağımsız özeti. Faz 0'da canlı DB istatistiği olmadığı için süre değil, sınıf tahmin edilir.
/// </summary>
public enum LockSeverity
{
    /// <summary>Kilit yok veya ölçülemeyecek kadar kısa (metadata-only değişiklik).</summary>
    None = 0,

    /// <summary>Anlık kilit — pratikte fark edilmez (ör. ADD COLUMN nullable).</summary>
    Brief = 1,

    /// <summary>Tablo taraması/yeniden yazımı gerektirir — büyük tablolarda uzun süre kilitler.</summary>
    Blocking = 2
}
