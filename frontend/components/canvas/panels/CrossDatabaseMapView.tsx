'use client';

import { useEffect, useLayoutEffect, useRef, useState } from 'react';
import { Network, X, Key, Loader2, Eye, EyeOff } from 'lucide-react';
import { DatabaseSchema, SchemaTable } from '../../../types/schema';

/** Bir mantıksal bağın iki ucu — panelden gelen çözülmüş adlar. */
export interface MapLink {
  id: string;
  /** "tablo.kolon" — bu projede. */
  localColumn: string;
  /** "tablo.kolon" — karşı projede. */
  otherColumn: string;
  note: string | null;
}

interface Props {
  isOpen: boolean;
  onClose: () => void;
  localName: string;
  localSchema: DatabaseSchema;
  otherName: string;
  otherSchema: DatabaseSchema | null;
  links: MapLink[];
}

/**
 * second-phase/10-COKLU-DB.md — iki veritabanını yan yana ve aralarındaki
 * MANTIKSAL bağları görünür kılan harita.
 *
 * <b>İki React Flow canvas'ı DEĞİL, bilerek.</b> Doc'un kendi uyarısı: "üç
 * canvas aynı anda React Flow demek; node sayısı büyüdükçe ağırlaşır". Ayrıca
 * <c>TableNode</c> düzenleme/silme için şema store'una bağlı — onu KARŞI
 * projenin tabloları için kullanmak, yalnızca görüntülediğin bir projeden
 * tablo silebilmen demek olurdu. Bu görünüm salt-okunur olarak inşa edildi:
 * hiçbir store mutasyonu, hiçbir düzenleme yüzeyi yok.
 *
 * <b>Bağlar KESİK ÇİZGİ.</b> Doc'un açık şartı — gerçek bir yabancı anahtar
 * gibi göstermek, veritabanının koruduğu yanılsamasını yaratır ve tam da
 * önlemeye çalıştığımız hatayı üretir.
 *
 * <b>Varsayılan olarak yalnızca BAĞLI tablolar gösteriliyor.</b> Kırk alakasız
 * tablo, önemli olan ikisini gömer.
 */
export default function CrossDatabaseMapView({
  isOpen, onClose, localName, localSchema, otherName, otherSchema, links,
}: Props) {
  const containerRef = useRef<HTMLDivElement>(null);
  const rowRefs = useRef<Record<string, HTMLDivElement | null>>({});
  const [paths, setPaths] = useState<{ id: string; d: string }[]>([]);
  const [showAll, setShowAll] = useState(false);

  // Bağın dokunduğu "tablo.kolon" anahtarları.
  const localKeys = new Set(links.map(l => l.localColumn.toLowerCase()));
  const otherKeys = new Set(links.map(l => l.otherColumn.toLowerCase()));

  const tableIsLinked = (table: SchemaTable, keys: Set<string>) =>
    table.columns.some(c => keys.has(`${table.name}.${c.name}`.toLowerCase()));

  const localTables = showAll
    ? localSchema.tables
    : localSchema.tables.filter(t => tableIsLinked(t, localKeys));
  const otherTables = !otherSchema ? [] : showAll
    ? otherSchema.tables
    : otherSchema.tables.filter(t => tableIsLinked(t, otherKeys));

  // Çizgiler DOM ölçümüne dayanıyor — kartlar yerleştikten SONRA hesaplanmalı.
  useLayoutEffect(() => {
    if (!isOpen) return;

    const compute = () => {
      const container = containerRef.current;
      if (!container) return;
      const base = container.getBoundingClientRect();

      const next: { id: string; d: string }[] = [];
      for (const link of links) {
        const from = rowRefs.current[`L:${link.localColumn.toLowerCase()}`];
        const to = rowRefs.current[`R:${link.otherColumn.toLowerCase()}`];
        if (!from || !to) continue; // gizli (filtrelenmiş) uç — çizgi de çizilmez

        const f = from.getBoundingClientRect();
        const t = to.getBoundingClientRect();
        const x1 = f.right - base.left + container.scrollLeft;
        const y1 = f.top + f.height / 2 - base.top + container.scrollTop;
        const x2 = t.left - base.left + container.scrollLeft;
        const y2 = t.top + t.height / 2 - base.top + container.scrollTop;
        const mid = (x1 + x2) / 2;

        next.push({ id: link.id, d: `M ${x1} ${y1} C ${mid} ${y1}, ${mid} ${y2}, ${x2} ${y2}` });
      }
      setPaths(next);
    };

    compute();
    // Kaydırma/yeniden boyutlandırma çizgileri kaydırır — yeniden ölç.
    const container = containerRef.current;
    container?.addEventListener('scroll', compute);
    window.addEventListener('resize', compute);
    return () => {
      container?.removeEventListener('scroll', compute);
      window.removeEventListener('resize', compute);
    };
  }, [isOpen, links, showAll, localTables.length, otherTables.length]);

  if (!isOpen) return null;

  return (
    <div className="fixed inset-0 z-[110] flex items-center justify-center bg-scrim/75 backdrop-blur-sm animate-in fade-in duration-200">
      <div className="bg-surface-800 border border-content-primary/12 rounded-2xl w-[94vw] max-w-5xl h-[86vh] flex flex-col shadow-[0_20px_60px_rgba(0,0,0,0.6)] overflow-hidden">
        <div className="border-b border-content-primary/10 px-5 py-3.5 flex items-center justify-between shrink-0">
          <div className="flex items-center gap-2.5">
            <div className="h-8 w-8 bg-surface-600 border border-content-primary/10 rounded-lg flex items-center justify-center">
              <Network className="w-4 h-4 text-content-primary" />
            </div>
            <div>
              <h2 className="text-sm font-bold text-content-primary">
                {localName} <span className="text-content-subtle font-normal">↔</span> {otherName}
              </h2>
              <p className="text-[11px] text-content-muted">
                {links.length} logical link{links.length === 1 ? '' : 's'} — dashed, because no database enforces these.
              </p>
            </div>
          </div>
          <div className="flex items-center gap-1.5">
            <button
              onClick={() => setShowAll(v => !v)}
              className="flex items-center gap-1.5 px-2.5 py-1.5 rounded-lg text-[11px] font-semibold text-content-muted hover:text-content-primary hover:bg-white/[0.06] transition-colors"
              title={showAll ? 'Show only linked tables' : 'Show every table'}
            >
              {showAll ? <EyeOff className="w-3.5 h-3.5" /> : <Eye className="w-3.5 h-3.5" />}
              {showAll ? 'Linked only' : 'Show all tables'}
            </button>
            <button onClick={onClose} className="p-1.5 hover:bg-white/[0.06] rounded-lg text-content-subtle hover:text-content-primary transition-colors" aria-label="Close">
              <X className="w-4 h-4" />
            </button>
          </div>
        </div>

        <div ref={containerRef} className="flex-1 overflow-auto relative p-6">
          {/* Bağ çizgileri — kartların ALTINDA, tıklamayı engellemesin diye pointer-events yok. */}
          <svg className="absolute inset-0 w-full h-full pointer-events-none" style={{ overflow: 'visible' }}>
            {paths.map(p => (
              <path
                key={p.id}
                d={p.d}
                fill="none"
                stroke="currentColor"
                className="text-content-muted"
                strokeWidth={1.5}
                strokeDasharray="5 4"
              />
            ))}
          </svg>

          <div className="relative grid grid-cols-2 gap-24">
            <TableColumn
              heading={localName}
              tables={localTables}
              linkedKeys={localKeys}
              side="L"
              rowRefs={rowRefs}
              emptyText={showAll ? 'This project has no tables.' : 'No tables here take part in a link.'}
            />
            {otherSchema ? (
              <TableColumn
                heading={otherName}
                tables={otherTables}
                linkedKeys={otherKeys}
                side="R"
                rowRefs={rowRefs}
                emptyText={showAll ? 'That project has no tables.' : 'No tables there take part in a link.'}
              />
            ) : (
              <div className="flex items-center justify-center text-xs text-content-muted">
                <Loader2 className="w-4 h-4 animate-spin mr-2" /> Loading {otherName}…
              </div>
            )}
          </div>
        </div>

        <div className="border-t border-content-primary/10 px-5 py-2.5 shrink-0">
          <p className="text-[11px] text-content-muted">
            These links are Namines&apos; own record. No database validates them — deleting a
            linked column on either side will not be blocked by any foreign key.
          </p>
        </div>
      </div>
    </div>
  );
}

function TableColumn({
  heading, tables, linkedKeys, side, rowRefs, emptyText,
}: {
  heading: string;
  tables: SchemaTable[];
  linkedKeys: Set<string>;
  side: 'L' | 'R';
  rowRefs: React.MutableRefObject<Record<string, HTMLDivElement | null>>;
  emptyText: string;
}) {
  return (
    <div className="flex flex-col gap-3">
      <p className="text-[10px] uppercase tracking-wider text-content-subtle font-bold sticky top-0">{heading}</p>

      {tables.length === 0 && <p className="text-xs text-content-muted">{emptyText}</p>}

      {tables.map(table => (
        <div key={table.id} className="rounded-xl border border-content-primary/12 bg-surface-700 overflow-hidden">
          <div className="px-3 py-2 border-b border-content-primary/10 bg-surface-600">
            <span className="font-mono text-xs font-semibold text-content-primary">{table.name}</span>
          </div>
          <div className="flex flex-col">
            {table.columns.map(col => {
              const key = `${table.name}.${col.name}`.toLowerCase();
              const isLinked = linkedKeys.has(key);
              return (
                <div
                  key={col.id}
                  ref={el => { if (isLinked) rowRefs.current[`${side}:${key}`] = el; }}
                  className={`flex items-center gap-1.5 px-3 py-1 text-[11px] ${
                    isLinked ? 'bg-white/[0.06] text-content-primary font-medium' : 'text-content-muted'
                  }`}
                >
                  {col.isPK && <Key className="w-2.5 h-2.5 shrink-0 text-content-subtle" />}
                  <span className="font-mono truncate">{col.name}</span>
                  <span className="text-content-subtle ml-auto shrink-0">{col.type}</span>
                </div>
              );
            })}
          </div>
        </div>
      ))}
    </div>
  );
}
