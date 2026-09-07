/**
 * `CloudProject.SchemaJson` / `NodePositionsJson`'dan Canvas'ın (D3) ihtiyaç
 * duyduğu İKİ şeyi çıkarır: tablo adı → id eşlemesi, ve id → {x,y} konumu.
 *
 * `Namines.Core.Models.DatabaseSchema` KOPYALANMIYOR (mikroservis sınırı,
 * third-phase §5) — yalnızca burada gereken alanlar okunuyor. Alan adları
 * büyük/küçük harfe duyarsız okunuyor çünkü tasarım tarafı bunu üreten yer
 * ana frontend'in kendi (camelCase) modeli; backend'in `SchemaJsonOptions`
 * (GatewayController) da aynı sebeple `PropertyNameCaseInsensitive` kullanıyor.
 */

export interface DesignTableRef { id: string; name: string; }
export interface NodePosition { x: number; y: number; }

function firstProp(obj: Record<string, unknown>, ...names: string[]): unknown {
  for (const n of names) {
    if (obj[n] !== undefined) return obj[n];
  }
  return undefined;
}

/** `SchemaJson`'daki tabloları `{id, name}` olarak çıkarır. Ayrıştırılamazsa boş dizi — uydurmaz. */
export function parseDesignTables(schemaJson: string | null): DesignTableRef[] {
  if (!schemaJson) return [];
  try {
    const parsed = JSON.parse(schemaJson) as Record<string, unknown>;
    const tables = firstProp(parsed, 'tables', 'Tables');
    if (!Array.isArray(tables)) return [];
    return tables
      .map(t => {
        const obj = t as Record<string, unknown>;
        const id = firstProp(obj, 'id', 'Id');
        const name = firstProp(obj, 'name', 'Name');
        return typeof id === 'string' && typeof name === 'string' ? { id, name } : null;
      })
      .filter((t): t is DesignTableRef => t !== null);
  } catch {
    return [];
  }
}

/** `NodePositionsJson`'u `{id: {x,y}}` olarak çıkarır. Ayrıştırılamazsa boş nesne. */
export function parseNodePositions(nodePositionsJson: string | null): Record<string, NodePosition> {
  if (!nodePositionsJson) return {};
  try {
    const parsed = JSON.parse(nodePositionsJson) as Record<string, unknown>;
    const out: Record<string, NodePosition> = {};
    for (const [id, pos] of Object.entries(parsed)) {
      const p = pos as Record<string, unknown>;
      const x = firstProp(p, 'x', 'X');
      const y = firstProp(p, 'y', 'Y');
      if (typeof x === 'number' && typeof y === 'number') out[id] = { x, y };
    }
    return out;
  } catch {
    return {};
  }
}
