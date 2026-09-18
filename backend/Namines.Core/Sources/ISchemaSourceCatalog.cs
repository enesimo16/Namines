using System.Collections.Generic;

namespace Namines.Core.Sources;

/// <summary>
/// Ürünün bildiği şema kaynaklarının tek listesi.
///
/// <b>Arayüz, kaynakların çalıştırılmasını DEĞİL tanıtılmasını kapsıyor</b>
/// (github/07-FAZ-PLANI.md F0 kapsam kararı 1): her kaynağın bugün kendi ucu
/// var ve istemci onları doğrudan çağırıyor. Çalıştırılabilir ortak sözleşme,
/// ilk gerçek implementorü (GitHub, F1) geldiğinde yazılacak — implementorü
/// olmayan bir arayüz, o geldiğinde nasılsa yeniden yazılırdı.
/// </summary>
public interface ISchemaSourceCatalog
{
    IReadOnlyList<SchemaSourceDescriptor> All();
}
