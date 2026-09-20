import type {
  AutomationActionStep,
  AutomationCondition,
} from '../store/useAutomationStore';
import type { NaminesFlowEvent } from './naminesFlowEventBus';

/**
 * Tek tıkla kurulan hazır flow'lar.
 *
 * <b>Neden gerekli:</b> boş bir kuralla başlayan kullanıcı, dört ayrı
 * seçicinin ne işe yaradığını çözmeden anlamlı bir şey kuramıyordu. Şablonlar
 * "ne yapılabileceğini" örnekle gösteriyor — özellikle koşul ve çoklu aksiyon
 * gibi, varlığından haberdar olunmadan aranmayacak özellikleri.
 *
 * Şablonlar kuralı OLUŞTURUR, sonra kullanıcı düzenler. Kurulur kurulmaz
 * çekmece açılıyor ki eksik alan (ör. webhook URL'i) hemen görülsün.
 */
export interface NaminesFlowTemplate {
  id: string;
  label: string;
  description: string;
  /** Boş string = proje geneli. Tabloya bağlı şablonlar seçili tabloyu kullanır. */
  requiresTable: boolean;
  triggerType: NaminesFlowEvent['type'];
  name: string;
  conditions: AutomationCondition[];
  actions: AutomationActionStep[];
}

export const NAMINES_FLOW_TEMPLATES: NaminesFlowTemplate[] = [
  {
    id: 'warn-on-delete',
    label: 'Warn me when a table is deleted',
    description: 'A notification in this browser, the moment it happens.',
    requiresTable: true,
    triggerType: 'TableDeleted',
    name: 'Warn on table delete',
    conditions: [],
    actions: [{ actionType: 'Toast', actionConfig: {} }],
  },
  {
    id: 'slack-on-schema-change',
    label: 'Tell Slack when the schema changes',
    description: 'Posts to a Slack incoming webhook. Add your URL after creating it.',
    requiresTable: false,
    triggerType: 'TableAdded',
    name: 'Notify Slack on schema change',
    conditions: [],
    actions: [{
      actionType: 'Slack',
      actionConfig: { message: '{{projectName}}: {{trigger}} on {{tableName}}' },
    }],
  },
  {
    id: 'lint-on-column-added',
    label: 'Lint the schema when a column is added',
    description: 'Runs the linter on the server. No AI quota.',
    requiresTable: true,
    triggerType: 'ColumnAdded',
    name: 'Lint on new column',
    conditions: [],
    actions: [{ actionType: 'Lint', actionConfig: {} }],
  },
  {
    id: 'key-column-guard',
    label: 'Guard key columns',
    description: 'When a column ending in _id is deleted: notify, then call a webhook.',
    requiresTable: true,
    triggerType: 'ColumnDeleted',
    name: 'Key column guard',
    conditions: [{ field: 'columnName', op: 'endsWith', value: '_id' }],
    actions: [
      { actionType: 'Toast', actionConfig: {} },
      { actionType: 'Webhook', actionConfig: {} },
    ],
  },
];
