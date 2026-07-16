import { DatabaseSchema, SchemaTable, SchemaRelation } from '../types/schema';

/**
 * Şemalar için üç yönlü (three-way) birleştirme.
 *
 * SORUN: Oda senkronizasyonu şemanın TAMAMINI yayınlayıp gelen şemayla yereli kör
 * şekilde değiştiriyordu. İki kişi aynı anda tablo eklerse her biri diğerinin
 * şemasını ezer ve tablolardan biri sessizce kaybolur (son-yazan-kazanır).
 *
 * ÇÖZÜM: Her iki tarafın da en son üzerinde anlaştığı şemayı (base) referans alıp
 * "kim neyi değiştirdi" sorusunu yanıtlamak. Böylece:
 *   - Farklı tablolara yapılan eşzamanlı değişiklikler İKİSİ DE korunur.
 *   - Silmeler yayılır (union-merge'in aksine silinen tablo geri gelmez).
 *   - Yalnızca AYNI varlık iki taraftan da değiştiyse çakışma olur.
 *
 * YAKINSAMA: Çakışmada "remote kazanır" demek YETMEZ — A remote(B)'yi, B remote(A)'yı
 * seçer ve iki taraf sonsuza dek yer değiştirir. Bu yüzden kazanan, her iki tarafın da
 * aynı sonucu hesaplayacağı deterministik bir kuralla seçilir (serileştirilmiş hâlin
 * sözlük sırasına göre büyük olanı).
 *
 * Bu bir CRDT değildir: aynı tablo aynı anda iki taraftan düzenlenirse o tablonun
 * bir sürümü kaybolur. Ancak kayıp tüm şema yerine tek varlıkla sınırlıdır.
 */

interface Identifiable {
  id: string;
}

const serialize = (value: unknown): string => JSON.stringify(value);

const isEqual = (a: unknown, b: unknown): boolean => serialize(a) === serialize(b);

const toMap = <T extends Identifiable>(items: T[]): Map<string, T> =>
  new Map(items.map(item => [item.id, item]));

/**
 * Çakışmayı her iki peer'ın da AYNI şekilde çözmesi için deterministik seçim.
 * Girdi sırasına veya "kim remote" olduğuna bağlı değildir.
 */
function pickDeterministic<T>(a: T, b: T): T {
  return serialize(a) > serialize(b) ? a : b;
}

function mergeEntities<T extends Identifiable>(base: T[], local: T[], remote: T[]): T[] {
  const baseMap = toMap(base);
  const localMap = toMap(local);
  const remoteMap = toMap(remote);

  const allIds = new Set([...localMap.keys(), ...remoteMap.keys()]);
  const merged: T[] = [];

  for (const id of allIds) {
    const inBase = baseMap.get(id);
    const inLocal = localMap.get(id);
    const inRemote = remoteMap.get(id);

    // Yalnızca bir tarafta var ve base'de yoktu → o taraf ekledi, koru.
    if (!inBase) {
      if (inLocal && !inRemote) { merged.push(inLocal); continue; }
      if (!inLocal && inRemote) { merged.push(inRemote); continue; }
      // İkisi de aynı id ile ekledi (uuid'lerde pratikte olmaz) → deterministik seç.
      if (inLocal && inRemote) { merged.push(pickDeterministic(inLocal, inRemote)); continue; }
      continue;
    }

    // Base'de vardı ama bir tarafta yok → o taraf sildi, silme kazanır.
    if (!inLocal || !inRemote) continue;

    // Her iki tarafta da var: kim değiştirdi?
    const localChanged = !isEqual(inLocal, inBase);
    const remoteChanged = !isEqual(inRemote, inBase);

    if (localChanged && !remoteChanged) merged.push(inLocal);
    else if (!localChanged && remoteChanged) merged.push(inRemote);
    else if (!localChanged && !remoteChanged) merged.push(inLocal); // ikisi de base
    else merged.push(pickDeterministic(inLocal, inRemote));         // gerçek çakışma
  }

  return merged;
}

/**
 * base: iki tarafın en son anlaştığı şema (ortak ata)
 * local: bu istemcideki güncel şema
 * remote: peer'dan gelen şema
 */
export function mergeSchemas(
  base: DatabaseSchema,
  local: DatabaseSchema,
  remote: DatabaseSchema
): DatabaseSchema {
  const tables = mergeEntities<SchemaTable>(base.tables ?? [], local.tables ?? [], remote.tables ?? []);
  const relations = mergeEntities<SchemaRelation>(base.relations ?? [], local.relations ?? [], remote.relations ?? []);

  // Referans bütünlüğü: bir tablo silinmişse ona bağlı ilişkiler de düşmeli.
  // Aksi halde birleştirme, olmayan bir tabloyu işaret eden ilişki üretir ve
  // derlemede geçersiz FOREIGN KEY çıkar.
  const tableIds = new Set(tables.map(t => t.id));
  const columnIds = new Set(tables.flatMap(t => t.columns.map(c => c.id)));

  const validRelations = relations.filter(r =>
    tableIds.has(r.sourceTableId) &&
    tableIds.has(r.targetTableId) &&
    columnIds.has(r.sourceColumnId) &&
    columnIds.has(r.targetColumnId)
  );

  return {
    ...remote,
    // İsim, base'e göre değişen tarafı izler; ikisi de değiştiyse deterministik seç.
    name: local.name === base.name ? remote.name
        : remote.name === base.name ? local.name
        : pickDeterministic(local.name, remote.name),
    tables,
    relations: validRelations,
  };
}
