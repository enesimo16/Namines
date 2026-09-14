import { create } from 'zustand';
import type { NaminesFlowEvent } from '../lib/naminesFlowEventBus';

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
 * Bölüm 3'e kadar YALNIZCA istemcide yaşar — sunucu tarafı aksiyonlar
 * (webhook/DBA/seed) henüz bu kuralları görmüyor, yalnızca `Toast` aksiyonu
 * bugünden itibaren çalışabilir (bkz. AutomationNode). Bölüm 3, bu
 * dosyanın action gövdelerini gerçek `/api/automation/rules` çağrılarıyla
 * değiştirecek — dışa açık imzalar SABİT kalıyor ki
 * `AutomationNode`/`AutomationRuleDrawer` hiç değişmesin.
 */
export const useAutomationStore = create<AutomationStoreState>((set, get) => ({
  rules: [],
  selectedRuleId: null,

  addRule: (scopeTableId, triggerType, actionType) => {
    const id = genId();
    set(state => ({
      rules: [...state.rules, { id, scopeTableId, triggerType, actionType, actionConfig: {}, enabled: true }],
    }));
    return id;
  },

  updateRule: (id, patch) => {
    set(state => ({
      rules: state.rules.map(r => r.id === id ? { ...r, ...patch } : r),
    }));
  },

  deleteRule: (id) => {
    set(state => ({
      rules: state.rules.filter(r => r.id !== id),
      selectedRuleId: state.selectedRuleId === id ? null : state.selectedRuleId,
    }));
  },

  deleteRulesForTable: (tableId) => {
    set(state => ({ rules: state.rules.filter(r => r.scopeTableId !== tableId) }));
  },

  rulesForTable: (tableId) => get().rules.filter(r => r.scopeTableId === tableId),

  setSelectedRuleId: (id) => set({ selectedRuleId: id }),
}));
