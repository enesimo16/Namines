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

    const timer = setTimeout(async () => {
      setIsLinting(true);
      try {
        const result = await schemaService.lintSchema(schema);
        setResult(result);
      } catch (error) {
        console.error("Linter failed", error);
      } finally {
        setIsLinting(false);
      }
    }, 500); // 500ms debounce

    return () => clearTimeout(timer);
  }, [schema, setResult, setIsLinting]);
}
