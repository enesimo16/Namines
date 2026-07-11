import { useEffect } from 'react';
import { useSchemaStore } from '../store/useSchemaStore';
import { useLinterStore } from '../store/useLinterStore';
import { schemaService } from '../services/api';
import { flowToSchema } from '../lib/flowToSchema';

export function useLinter() {
  const { schema } = useSchemaStore();
  const { setResult, setIsLinting } = useLinterStore();

  useEffect(() => {
    if (!schema) return;

    let cancelled = false;
    const timer = setTimeout(async () => {
      setIsLinting(true);
      try {
        const result = await schemaService.lintSchema(schema);
        if (!cancelled) setResult(result); // stale-result yarışını engelle
      } catch (error) {
        if (!cancelled) console.error("Linter failed", error);
      } finally {
        if (!cancelled) setIsLinting(false);
      }
    }, 500); // 500ms debounce

    return () => { cancelled = true; clearTimeout(timer); };
  }, [schema, setResult, setIsLinting]);
}
