import { describe, it, expect } from 'vitest';
import { LOCAL_ADVANCED_FALLBACK, readLocalAdvanced, writeLocalAdvanced } from './aiAdvancedPrefs';

function memoryStorage(initial: Record<string, string> = {}): Storage {
  const data = new Map(Object.entries(initial));
  return {
    get length() { return data.size; },
    clear: () => data.clear(),
    getItem: (key) => data.get(key) ?? null,
    key: (index) => [...data.keys()][index] ?? null,
    removeItem: (key) => { data.delete(key); },
    setItem: (key, value) => { data.set(key, value); },
  };
}

describe('aiAdvancedPrefs', () => {
  it('returns the modal fallback when nothing is stored', () => {
    expect(readLocalAdvanced(memoryStorage())).toEqual(LOCAL_ADVANCED_FALLBACK);
  });

  it('keeps RESTRICT as the default FK action — the default must never fall toward data loss', () => {
    expect(LOCAL_ADVANCED_FALLBACK.fkAction).toBe('restrict');
  });

  it('reads the legacy storage keys the modal has always written', () => {
    const storage = memoryStorage({ 'namines-ai-fk-action': 'set_null', 'namines-ai-sql-pretty': 'false' });
    const result = readLocalAdvanced(storage);
    expect(result.fkAction).toBe('set_null');
    expect(result.sqlPrettyPrint).toBe('false');
    expect(result.seedDomain).toBe(LOCAL_ADVANCED_FALLBACK.seedDomain);
  });

  it('treats an empty stored value as missing, like the old `|| fallback` reads did', () => {
    expect(readLocalAdvanced(memoryStorage({ 'namines-ai-max-tokens': '' })).maxTokens).toBe('32000');
  });

  it('round-trips every field through storage', () => {
    const storage = memoryStorage();
    const values = { ...LOCAL_ADVANCED_FALLBACK, docLevel: 'deep', temperature: '0.7', autoIndex: 'false' };
    writeLocalAdvanced(values, storage);
    expect(readLocalAdvanced(storage)).toEqual(values);
    expect(storage.length).toBe(Object.keys(values).length);
  });
});
