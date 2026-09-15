'use client';

import * as Dialog from '@radix-ui/react-dialog';
import { X, Zap } from 'lucide-react';
import { useAutomationStore, type AutomationActionType } from '../../store/useAutomationStore';
import type { NaminesFlowEvent } from '../../lib/naminesFlowEventBus';

const TRIGGER_OPTIONS: { value: NaminesFlowEvent['type']; label: string }[] = [
  { value: 'TableAdded', label: 'Table added' },
  { value: 'TableDeleted', label: 'Table deleted' },
  { value: 'ColumnAdded', label: 'Column added' },
  { value: 'ColumnDeleted', label: 'Column deleted' },
  { value: 'ColumnChanged', label: 'Column changed' },
  // RelationAdded/RelationDeleted BİLEREK yok: AutomationRuleMatcher bu iki
  // tetikleyiciyi yalnızca PROJE GENELİ kurallar (ScopeTableId == null) için
  // eşleştiriyor, ama kural oluşturmanın tek yolu olan CanvasContextMenu her
  // zaman bir tablo id'si veriyor. Dolayısıyla bu seçenekler kullanıcının
  // oluşturabildiği hiçbir kuralda ASLA tetiklenemezdi. Proje geneli kural
  // oluşturma eklendiğinde geri konulmalı.
];

const ACTION_OPTIONS: { value: AutomationActionType; label: string }[] = [
  { value: 'Toast', label: 'Show a notification' },
  { value: 'Webhook', label: 'Call a webhook' },
  { value: 'DbaCheck', label: 'Run a DBA check' },
  { value: 'SeedData', label: 'Generate sample data' },
];

const inputClass = 'w-full bg-surface-700 border border-content-primary/10 rounded-[var(--radius-control)] px-3 py-2 text-sm text-content-primary placeholder:text-content-subtle focus:outline-none focus:border-focus-ring transition-colors';

/**
 * Namines Flow kural çekmecesi — bir tabloya bağlı bir otomasyon kuralının
 * tetikleyicisini ve aksiyonunu düzenler.
 */
export default function AutomationRuleDrawer() {
  const selectedRuleId = useAutomationStore(s => s.selectedRuleId);
  const rule = useAutomationStore(s => s.rules.find(r => r.id === s.selectedRuleId));
  const updateRule = useAutomationStore(s => s.updateRule);
  const setSelectedRuleId = useAutomationStore(s => s.setSelectedRuleId);

  const isOpen = !!selectedRuleId && !!rule;

  return (
    <Dialog.Root open={isOpen} onOpenChange={(open) => { if (!open) setSelectedRuleId(null); }}>
      <Dialog.Portal>
        <Dialog.Overlay className="fixed inset-0 bg-scrim/60 z-[90]" />
        <Dialog.Content className="fixed right-0 top-0 h-full w-full max-w-sm bg-surface-800 border-l border-content-primary/10 z-[91] p-5 flex flex-col gap-4 overflow-y-auto">
          <div className="flex items-center justify-between">
            <Dialog.Title className="flex items-center gap-2 text-sm font-semibold text-content-primary">
              <Zap className="w-4 h-4 text-warning-text" />
              Namines Flow
            </Dialog.Title>
            <Dialog.Close asChild>
              <button type="button" aria-label="Close" className="p-1 rounded-[var(--radius-control)] text-content-muted hover:text-content-primary">
                <X className="w-4 h-4" />
              </button>
            </Dialog.Close>
          </div>

          {rule && (
            <>
              <label className="flex flex-col gap-1.5 text-xs font-medium text-content-secondary">
                Trigger
                <select
                  className={inputClass}
                  value={rule.triggerType}
                  onChange={(e) => updateRule(rule.id, { triggerType: e.target.value as NaminesFlowEvent['type'] })}
                >
                  {TRIGGER_OPTIONS.map(opt => (
                    <option key={opt.value} value={opt.value}>{opt.label}</option>
                  ))}
                </select>
              </label>

              <label className="flex flex-col gap-1.5 text-xs font-medium text-content-secondary">
                Action
                <select
                  className={inputClass}
                  value={rule.actionType}
                  onChange={(e) => updateRule(rule.id, { actionType: e.target.value as AutomationActionType })}
                >
                  {ACTION_OPTIONS.map(opt => (
                    <option key={opt.value} value={opt.value}>{opt.label}</option>
                  ))}
                </select>
              </label>

              {rule.actionType === 'Webhook' && (
                <label className="flex flex-col gap-1.5 text-xs font-medium text-content-secondary">
                  Webhook URL
                  <input
                    type="url"
                    aria-label="Webhook URL"
                    className={inputClass}
                    defaultValue={rule.actionConfig.url ?? ''}
                    placeholder="https://example.com/hook"
                    onBlur={(e) => updateRule(rule.id, { actionConfig: { ...rule.actionConfig, url: e.target.value } })}
                  />
                </label>
              )}
            </>
          )}
        </Dialog.Content>
      </Dialog.Portal>
    </Dialog.Root>
  );
}
