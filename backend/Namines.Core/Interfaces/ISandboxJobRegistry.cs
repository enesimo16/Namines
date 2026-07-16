using System.Collections.Generic;

namespace Namines.Core.Interfaces;

/// <summary>
/// Çalışmakta olan sandbox işlerinin kaydı.
/// <para>
/// Arka plan süpürücüsü (sweeper) eski container'ları temizler ama bir container'ın
/// "zombi" mi yoksa "hâlâ çalışan uzun bir iş" mi olduğunu yalnızca yaşına bakarak
/// ayırt edemez. Bu arayüz o ayrımı yapar: kaydı tutan katman (API) ile temizliği
/// yapan katman (Infrastructure) arasındaki bağımlılığı ters çevirmeden bilgi taşır.
/// </para>
/// </summary>
public interface ISandboxJobRegistry
{
    /// <summary>Hâlâ süren işlerin kimlikleri. Bunlara ait container'lar silinmemeli.</summary>
    IReadOnlyCollection<string> GetActiveJobIds();
}
