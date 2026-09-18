import type { DatabaseSchema } from '../types/schema';

/**
 * Canvas açılışında şema deposuna NE yükleneceğine karar verir.
 *
 * **Neden ayrı ve saf bir fonksiyon:** yanlış karar VERİ KAYBETTİRİYORDU ve
 * bunu bir sayfa bileşeninin içinde sınamak mümkün değildi.
 *
 * **Bulunma yeri (canlı test):** `useSchemaStore` şemayı bilerek persist
 * etmiyor — `partialize` yorumu "sayfa yenilendiğinde schema, activeProjectId
 * üzerinden loadProject() ile restore edilir" diyordu. Öyle bir restore hiç
 * yazılmamıştı. `/canvas`'a doğrudan girildiğinde (yenileme, yer imi, geri
 * tuşu) sayfa BOŞ bir şema kuruyor, üç saniye sonra otomatik kaydetme o boş
 * şemayı IndexedDB'deki aktif projenin ÜSTÜNE yazıyordu. Onbir tabloluk bir
 * şema tek bir yenilemeyle sıfır tabloya düştü; kayıtta yalnızca proje adı
 * kaldı.
 *
 * Kural bu yüzden üç adımlı ve sırası önemli:
 * 1. Depo IndexedDB'den hidre OLMADAN hiçbir şey yükleme. `projects` o anda
 *    boş bir dizi ve "proje yok" ile "henüz okumadık" ayırt edilemez —
 *    ikisini karıştırmak tam olarak yukarıdaki kaybı üretiyor.
 * 2. Aktif projenin şeması varsa onu geri yükle.
 * 3. Yalnızca gerçekten proje yoksa boş şema kur (ilk kez gelen kullanıcı).
 */
export type CanvasBootDecision =
  | { action: 'wait' }
  | { action: 'restore'; schema: DatabaseSchema; nodePositions?: Record<string, { x: number; y: number }> }
  | { action: 'empty' };

export interface CanvasBootInput {
  /** Şema deposunda hâlihazırda bir şema var mı. */
  hasSchema: boolean;
  /** Paylaşılan odaya katılındıysa şema karşı taraftan gelir. */
  joinedSharedRoom: boolean;
  /** Proje deposu IndexedDB'den okundu mu. */
  hasHydrated: boolean;
  activeProjectId: string | null;
  projects: ReadonlyArray<{
    id: string;
    schema?: DatabaseSchema | null;
    nodePositions?: Record<string, { x: number; y: number }> | null;
  }>;
}

export function decideCanvasBoot(input: CanvasBootInput): CanvasBootDecision {
  const { hasSchema, joinedSharedRoom, hasHydrated, activeProjectId, projects } = input;

  // Zaten bir şema var ya da odadan gelecek — dokunma.
  if (hasSchema || joinedSharedRoom) return { action: 'wait' };

  // Hidrasyon bitmeden karar verme (bkz. yukarıdaki 1. kural).
  if (!hasHydrated) return { action: 'wait' };

  const active = activeProjectId ? projects.find(p => p.id === activeProjectId) : undefined;

  // Tablosu olan bir proje varsa geri yükle. Tablosuz bir kayıt için boş şema
  // kurmak ile onu "geri yüklemek" aynı sonucu verir; ayrım yapmıyoruz.
  if (active?.schema && active.schema.tables && active.schema.tables.length > 0) {
    return {
      action: 'restore',
      schema: active.schema,
      nodePositions: active.nodePositions ?? undefined,
    };
  }

  return { action: 'empty' };
}
