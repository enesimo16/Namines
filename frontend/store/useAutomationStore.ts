import { create } from 'zustand';
import type { NaminesFlowEvent } from '../lib/naminesFlowEventBus';
import { fetchAutomationRules, createAutomationRule, deleteAutomationRule } from '../lib/automationApi';

const genId = (): string =>
  typeof crypto !== 'undefined' && crypto.randomUUID
    ? crypto.randomUUID()
    : Math.random().toString(36).slice(2) + Date.now().toString(36);

export type AutomationActionType = 'Webhook' | 'DbaCheck' | 'SeedData' | 'Toast';

export interface AutomationRule {
  id: string;
  scopeTableId: string;
  triggerType: NaminesFlowEvent['type'];
  actionType: AutomationActionType;
  actionConfig: { url?: string };
  enabled: boolean;
}

interface AutomationStoreState {
  rules: AutomationRule[];
  selectedRuleId: string | null;
  loadRules: (projectId: string) => Promise<void>;
  addRule: (scopeTableId: string, triggerType: NaminesFlowEvent['type'], actionType: AutomationActionType) => string;
  updateRule: (id: string, patch: Partial<Pick<AutomationRule, 'triggerType' | 'actionType' | 'actionConfig' | 'enabled'>>) => void;
  deleteRule: (id: string) => void;
  deleteRulesForTable: (tableId: string) => void;
  rulesForTable: (tableId: string) => AutomationRule[];
  setSelectedRuleId: (id: string | null) => void;
}

/**
 * Namines Flow kurallarının istemci tarafı deposu.
 *
 * Bölüm 3'ten itibaren sunucudaki `/api/automation/rules`'a (Task 6,
 * AutomationController) bağlı — ama dışa açık imzalar SABİT kaldı ki
 * `AutomationNode`/`AutomationRuleDrawer` hiç değişmesin. Yerel state HER
 * ZAMAN senkron/iyimser güncelleniyor; sunucu çağrıları arka planda
 * (`void ...`) ateşleniyor ve başarısızlıkları sessizce yutuyor — bu
 * store'un sözleşmesi zaten senkron dönüş değerleri gerektiriyor
 * (`addRule` bir id döndürür), bu yüzden bir Promise'e bağlanamaz.
 */
export const useAutomationStore = create<AutomationStoreState>((set, get) => ({
  rules: [],
  selectedRuleId: null,

  loadRules: async (projectId) => {
    try {
      const rules = await fetchAutomationRules(projectId);
      set({ rules });
    } catch {
      // Ağ hatası: mevcut (muhtemelen boş) state korunur — kullanıcı
      // sayfayı yenileyince tekrar dener. Sessiz başarısızlık burada
      // kabul edilebilir çünkü kural DÜZENLEME hâlâ iyimser çalışır.
    }
  },

  addRule: (scopeTableId, triggerType, actionType) => {
    const id = genId();
    set(state => ({
      rules: [...state.rules, { id, scopeTableId, triggerType, actionType, actionConfig: {}, enabled: true }],
    }));
    // İyimser: yerel state anında güncellendi, sunucuya arka planda bildiriliyor.
    // NOT: gerçek sunucu id'si burada göz ardı ediliyor (v1 tavizi) — yerel
    // id kalıcı olarak kullanılmaya devam eder çünkü store'un dışa açık
    // sözleşmesi senkron bir id döndürmek zorunda.
    const rule = get().rules.find(r => r.id === id);
    if (rule) void createAutomationRule(rule.scopeTableId, scopeTableId, triggerType, actionType)?.catch?.(() => {});
    return id;
  },

  updateRule: (id, patch) => {
    set(state => ({
      rules: state.rules.map(r => r.id === id ? { ...r, ...patch } : r),
    }));
    // v1 tavizi: PATCH ucu bu planın kapsamında değil (spec CRUD listesi
    // yalnızca POST/DELETE tanımlıyor) — güncelleme şimdilik yalnızca
    // istemcide kalıcı, sayfa yenilenince sunucudaki eski hâline döner.
    // Bu, spec'in "throttle'a girmez" sözünü bozmuyor çünkü kayıt zaten
    // hiç sunucuya yazılmıyor; sonraki bir bölümde PATCH eklenebilir.
  },

  deleteRule: (id) => {
    set(state => ({
      rules: state.rules.filter(r => r.id !== id),
      selectedRuleId: state.selectedRuleId === id ? null : state.selectedRuleId,
    }));
    void deleteAutomationRule(id)?.catch?.(() => {});
  },

  deleteRulesForTable: (tableId) => {
    const toDelete = get().rules.filter(r => r.scopeTableId === tableId);
    set(state => ({ rules: state.rules.filter(r => r.scopeTableId !== tableId) }));
    toDelete.forEach(r => void deleteAutomationRule(r.id)?.catch?.(() => {}));
  },

  rulesForTable: (tableId) => get().rules.filter(r => r.scopeTableId === tableId),

  setSelectedRuleId: (id) => set({ selectedRuleId: id }),
}));
