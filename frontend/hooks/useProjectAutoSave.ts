'use client';

import { useEffect, useRef } from 'react';
import { useSchemaStore } from '../store/useSchemaStore';
import { useProjectHistoryStore } from '../store/useProjectHistoryStore';

/**
 * Canvas'taki schema veya node değişikliklerini 2 saniyelik debounce
 * ile arka planda IndexedDB'ye otomatik kaydeder.
 *
 * Kullanım: Canvas sayfasında `useProjectAutoSave()` çağrısı yeterlidir.
 */
export function useProjectAutoSave() {
  const schema = useSchemaStore(s => s.schema);
  const nodes  = useSchemaStore(s => s.nodes);
  const projectName = useSchemaStore(s => s.projectName);
  const dbType = useSchemaStore(s => s.dbType);

  const { saveCurrentProject } = useProjectHistoryStore();

  const timerRef = useRef<ReturnType<typeof setTimeout> | null>(null);

  useEffect(() => {
    // Schema yoksa kaydetme (landing sayfasında schema null olur)
    if (!schema) return;

    // Önceki timer'ı iptal et
    if (timerRef.current) clearTimeout(timerRef.current);

    // 2 saniye sonra kaydet
    timerRef.current = setTimeout(() => {
      saveCurrentProject(schema, nodes, projectName, dbType);
    }, 2000);

    return () => {
      if (timerRef.current) clearTimeout(timerRef.current);
    };
    // nodes değişimi (sürükleme) de tetiklemeli — eslint disable ile
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [schema, nodes, projectName, dbType]);
}
