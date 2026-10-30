namespace Namines.API.Controllers;

/// <summary>
/// Tam <c>SchemaJson</c> kabul eden uçların gövde sınırı. Veritabanında SchemaJson için
/// 10M karakterlik CHECK var; sınırı DB'ye bırakmak büyük yükü önce belleğe alıp ayrıştırıp
/// sonra 500/23514 ile reddetmek demekti. Burada istek, gövde okunurken 413 ile kesilir.
/// </summary>
internal static class SchemaRequestLimits
{
    /// <summary>Tek şema taşıyan uçlar: 10M karakterlik CHECK'in biraz üstü.</summary>
    public const int SingleSchemaBytes = 12 * 1024 * 1024;

    /// <summary>Birden çok proje taşıyabilen toplu eşitleme ucu (her proje yine 10M karakter ile sınırlı).</summary>
    public const int BulkSyncBytes = 48 * 1024 * 1024;
}
