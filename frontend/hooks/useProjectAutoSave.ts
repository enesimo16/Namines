'use client';

import { useEffect, useRef } from 'react';
import { useSchemaStore } from '../store/useSchemaStore';
import { useProjectHistoryStore } from '../store/useProjectHistoryStore';
import { DatabaseSchema } from '../types/schema';
import { Node } from '@xyflow/react';

/**
 * Canvas'taki schema veya node değişikliklerini 3 saniyelik debounce
 * ile arka planda IndexedDB'ye otomatik kaydeder.
 *
 * Shallow comparison guard: önceki schema/nodes JSON hash'i ile karşılaştırır;
 * gerçekten değişmişse kaydet, aksi hâlde döngüyü kır.
 *
 * Kullanım: Canvas sayfasında `useProjectAutoSave()` çağrısı yeterlidir.
 */
export function useProjectAutoSave() {
  const schema = useSchemaStore(s => s.schema);
  const nodes  = useSchemaStore(s => s.nodes);
  const projectName = useSchemaStore(s => s.projectName);
  const dbType = useSchemaStore(s => s.dbType);

  const { saveCurrentProject } = useProjectHistoryStore();

  const timerRef    = useRef<ReturnType<typeof setTimeout> | null>(null);
  // Shallow comparison: son kaydedilen verinin JSON hash'ini sakla
  const lastHashRef = useRef<string>('');

  useEffect(() => {
    // Schema yoksa kaydetme (landing sayfasında schema null olur)
    if (!schema) return;

    // ── Değişiklik guard'ı ────────────────────────────────────────────────
    // Şemanın TAMAMI hash'lenmeli. Daha önce yalnızca tablo adı ve kolon SAYISI
    // hash'leniyordu; bu yüzden kolon adı/tipi/PK/nullable değişiklikleri ile
    // ilişki uç noktası değişiklikleri hash'i değiştirmiyor, guard erken return
    // ediyor ve düzenlemeler hiç kaydedilmeden sayfa yenilemesinde kayboluyordu.
    const currentHash = JSON.stringify({
      schema,
      nodePositions: nodes.map(n => ({ id: n.id, x: Math.round(n.position.x), y: Math.round(n.position.y) })),
      projectName,
      dbType,
    });

    if (currentHash === lastHashRef.current) return; // Değişiklik yok — döngüyü kır
    // ─────────────────────────────────────────────────────────────────────

    // Önceki timer'ı iptal et
    if (timerRef.current) clearTimeout(timerRef.current);

    // 3 saniye debounce (drag storm'larında çok sık yazımı engeller)
    timerRef.current = setTimeout(() => {
      lastHashRef.current = currentHash; // Hash'i güncelle — bir sonraki çağrıda döngü kırılır
      saveCurrentProject(schema, nodes, projectName, dbType);
    }, 3000);

    return () => {
      if (timerRef.current) clearTimeout(timerRef.current);
    };
    // nodes değişimi (sürükleme) de tetiklemeli — eslint disable ile
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [schema, nodes, projectName, dbType]);
}
