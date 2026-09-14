/**
 * Namines Flow — Namines'in olay-tabanlı otomasyon katmanının çekirdek
 * olay veriyolu.
 *
 * Canvas'taki bir şema mutasyonu (tablo/kolon/ilişki ekleme-silme-değiştirme)
 * burada bir olay olarak yayınlanır. Bu modül YALNIZCA aynı tarayıcı
 * sekmesindeki ANLIK tepkiler içindir (toast, görsel ipucu) — ağa hiç
 * çıkmaz. Kalıcı/sunucu tarafı otomasyon (webhook, DBA kontrolü, örnek veri
 * üretimi) ayrı bir yoldan, sunucunun kendi hesapladığı şema diff'inden
 * tetiklenir (bkz. docs/superpowers/specs/2026-09-14-namines-flow-design.md
 * Bölüm 1 "Sunucu tarafı").
 */

export type NaminesFlowEvent =
  | { type: 'TableAdded'; tableId: string; tableName: string }
  | { type: 'TableDeleted'; tableId: string; tableName: string }
  | { type: 'ColumnAdded'; tableId: string; columnId: string; columnName: string }
  | { type: 'ColumnDeleted'; tableId: string; columnId: string; columnName: string }
  | { type: 'ColumnChanged'; tableId: string; columnId: string; columnName: string }
  | { type: 'RelationAdded'; relationId: string; sourceTableId: string; targetTableId: string }
  | { type: 'RelationDeleted'; relationId: string; sourceTableId: string; targetTableId: string };

type Listener = (event: NaminesFlowEvent) => void;
type ListenerKey = NaminesFlowEvent['type'] | '*';

function createNaminesFlowEventBus() {
  const listeners = new Map<ListenerKey, Set<Listener>>();

  function on(key: ListenerKey, listener: Listener): () => void {
    if (!listeners.has(key)) listeners.set(key, new Set());
    listeners.get(key)!.add(listener);
    return () => {
      listeners.get(key)?.delete(listener);
    };
  }

  function emit(event: NaminesFlowEvent): void {
    const targets = new Set<Listener>([
      ...(listeners.get(event.type) ?? []),
      ...(listeners.get('*') ?? []),
    ]);

    for (const listener of targets) {
      try {
        listener(event);
      } catch (err) {
        // Bir dinleyicinin patlaması diğerlerinin çalışmasını engellememeli —
        // örn. bozuk bir üçüncü parti entegrasyon, kullanıcının kendi
        // toast'unu görmesini engellemesin.
        console.error('[Namines Flow] listener threw:', err);
      }
    }
  }

  return { emit, on };
}

export const naminesFlow = createNaminesFlowEventBus();
