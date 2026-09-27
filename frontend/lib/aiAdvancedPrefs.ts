import type { AiAdvancedSettings } from '../store/useAIPolicyStore';

/**
 * Gelişmiş AI tercihlerinin tarayıcıdaki kopyası.
 *
 * Asıl kaynak sunucu (bkz. `useAIPolicyStore`); localStorage yalnızca modal
 * açılırken sunucu yanıtı gelene kadar gösterilecek değeri ve eski kurulumların
 * göçünü taşıyor. On bir anahtarın adı eskiden modalın üç ayrı yerinde elle
 * tekrarlanıyordu — biri değiştirilip diğeri unutulduğunda okuma ile yazma
 * farklı anahtara gidecekti.
 */
const STORAGE_KEYS: Record<keyof AiAdvancedSettings, string> = {
  seedDomain: 'namines-ai-seed-domain',
  docLevel: 'namines-ai-doc-level',
  scaffoldVersion: 'namines-ai-scaffold-version',
  dbaSeverity: 'namines-ai-dba-severity',
  temperature: 'namines-ai-temperature',
  promptStyle: 'namines-ai-prompt-style',
  namingConvention: 'namines-ai-naming-convention',
  fkAction: 'namines-ai-fk-action',
  maxTokens: 'namines-ai-max-tokens',
  sqlPrettyPrint: 'namines-ai-sql-pretty',
  autoIndex: 'namines-ai-auto-index',
};

/**
 * Hiç kayıt yokken gösterilen değerler — modalın bugüne kadar gösterdiği
 * değerlerle birebir aynı. `maxTokens` burada 32000, `defaultAdvanced`'ta 4096:
 * bu fark bölmeden önce de vardı ve bölme işinin kapsamı davranışı korumak.
 */
export const LOCAL_ADVANCED_FALLBACK: AiAdvancedSettings = {
  seedDomain: 'general',
  docLevel: 'standard',
  scaffoldVersion: '.net8',
  dbaSeverity: 'warning',
  temperature: '0.2',
  promptStyle: 'clean',
  namingConvention: 'snake_case',
  fkAction: 'restrict',
  maxTokens: '32000',
  sqlPrettyPrint: 'true',
  autoIndex: 'true',
};

const ADVANCED_KEYS = Object.keys(STORAGE_KEYS) as (keyof AiAdvancedSettings)[];

export function readLocalAdvanced(storage: Storage = localStorage): AiAdvancedSettings {
  const result = { ...LOCAL_ADVANCED_FALLBACK };
  for (const key of ADVANCED_KEYS) {
    const stored = storage.getItem(STORAGE_KEYS[key]);
    if (stored) result[key] = stored;
  }
  return result;
}

export function writeLocalAdvanced(values: AiAdvancedSettings, storage: Storage = localStorage): void {
  for (const key of ADVANCED_KEYS) storage.setItem(STORAGE_KEYS[key], values[key]);
}
