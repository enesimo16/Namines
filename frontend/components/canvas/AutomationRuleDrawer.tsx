'use client';

import * as Dialog from '@radix-ui/react-dialog';
import { ArrowDown, ArrowUp, Plus, Trash2, X, Zap } from 'lucide-react';
import {
  newUid,
  useAutomationStore,
  type AutomationActionStep,
  type AutomationActionType,
  type AutomationCondition,
  type AutomationConditionField,
  type AutomationConditionOp,
} from '../../store/useAutomationStore';
import { useSchemaStore } from '../../store/useSchemaStore';
import AutomationRunHistory from './AutomationRunHistory';
import type { NaminesFlowEvent } from '../../lib/naminesFlowEventBus';

type TriggerType = NaminesFlowEvent['type'];

const TRIGGER_OPTIONS: { value: TriggerType; label: string; hint: string }[] = [
  { value: 'TableAdded', label: 'Table added', hint: 'A new table appears in the schema.' },
  { value: 'TableDeleted', label: 'Table deleted', hint: 'A table is removed from the schema.' },
  { value: 'ColumnAdded', label: 'Column added', hint: 'A column is added to the table.' },
  { value: 'ColumnDeleted', label: 'Column deleted', hint: 'A column is removed — often a breaking change.' },
  { value: 'ColumnChanged', label: 'Column changed', hint: 'A column’s type or constraints change.' },
  { value: 'RelationAdded', label: 'Relation added', hint: 'A foreign key is created anywhere in the project.' },
  { value: 'RelationDeleted', label: 'Relation deleted', hint: 'A foreign key is removed anywhere in the project.' },
];

/**
 * İlişki tetikleyicileri bir tabloya atfedilemiyor: şema diff'i ilişkileri
 * tablo bazında raporlamıyor, bu yüzden sunucudaki eşleştirici onları yalnızca
 * PROJE GENELİ kurallarda değerlendiriyor. Tabloya bağlı bir kuralda seçilseler
 * asla tetiklenmezlerdi — bu yüzden yalnızca kapsam "tüm proje" iken sunuluyorlar.
 */
const RELATION_TRIGGERS: TriggerType[] = ['RelationAdded', 'RelationDeleted'];
const isRelationTrigger = (t: TriggerType) => RELATION_TRIGGERS.includes(t);
const isColumnTrigger = (t: TriggerType) =>
  t === 'ColumnAdded' || t === 'ColumnDeleted' || t === 'ColumnChanged';

const ACTION_OPTIONS: { value: AutomationActionType; label: string; hint: string }[] = [
  { value: 'Toast', label: 'Show a notification', hint: 'Instant, in this browser only.' },
  { value: 'Slack', label: 'Post to Slack', hint: 'Sends your message to a Slack incoming webhook.' },
  { value: 'Discord', label: 'Post to Discord', hint: 'Sends your message to a Discord webhook.' },
  { value: 'Webhook', label: 'Call a webhook', hint: 'Runs on the server, within ~30s of the next sync.' },
  { value: 'Lint', label: 'Run the linter', hint: 'Checks the schema locally. No AI quota.' },
  { value: 'DbaCheck', label: 'Run a DBA check', hint: 'Runs on the server and spends AI quota.' },
  { value: 'SeedData', label: 'Generate sample data', hint: 'Spends AI quota. The result is shown, never written to your database.' },
];

/** URL isteyen aksiyonlar — hepsi aynı HTTP yürütücüsünü kullanıyor. */
const URL_ACTIONS: AutomationActionType[] = ['Webhook', 'Slack', 'Discord'];
/**
 * Serbest mesaj yazılan aksiyonlar. Toast da burada: bildirimi sunucu değil
 * tarayıcı basıyor ama şablon aynı değişkenleri okuyor, dolayısıyla kullanıcı
 * açısından fark yok.
 */
const MESSAGE_ACTIONS: AutomationActionType[] = ['Toast', 'Slack', 'Discord'];

const TEMPLATE_VARIABLES = '{{trigger}} {{tableName}} {{columnName}} {{columnType}} {{projectName}} {{timestamp}}';

const CONDITION_OPS: { value: AutomationConditionOp; label: string }[] = [
  { value: 'equals', label: 'is' },
  { value: 'notEquals', label: 'is not' },
  { value: 'contains', label: 'contains' },
  { value: 'startsWith', label: 'starts with' },
  { value: 'endsWith', label: 'ends with' },
];

const inputClass = 'w-full bg-surface-700 border border-content-primary/10 rounded-[var(--radius-control)] px-3 py-2 text-sm text-content-primary placeholder:text-content-subtle focus:outline-none focus:border-focus-ring transition-colors';
const smallInputClass = 'bg-surface-700 border border-content-primary/10 rounded-[var(--radius-control)] px-2 py-1 text-xs text-content-primary focus:outline-none focus:border-focus-ring transition-colors';
const labelClass = 'flex flex-col gap-1.5 text-xs font-medium text-content-secondary';
const iconButtonClass = 'rounded-[var(--radius-control)] p-1 text-content-muted transition-colors hover:bg-content-primary/12 hover:text-content-primary cursor-pointer disabled:opacity-30 disabled:cursor-not-allowed';

const PROJECT_SCOPE = '__project__';

/**
 * Başlıkları düzenlenebilir düz metne ve geri çevirir.
 *
 * Değerdeki iki nokta korunuyor (`Authorization: Bearer a:b` geçerli bir
 * başlıktır), bu yüzden yalnızca İLK iki noktadan bölünüyor. Adı boş olan
 * satır atılıyor — geçersiz bir başlık sunucuda sessizce düşerdi.
 */
function headersToText(headers: Record<string, string> | undefined): string {
  if (!headers) return '';
  return Object.entries(headers).map(([k, v]) => `${k}: ${v}`).join('\n');
}

function textToHeaders(text: string): Record<string, string> | undefined {
  const entries = text
    .split('\n')
    .map(line => {
      const at = line.indexOf(':');
      if (at < 0) return null;
      const name = line.slice(0, at).trim();
      return name === '' ? null : ([name, line.slice(at + 1).trim()] as const);
    })
    .filter((e): e is readonly [string, string] => e !== null);

  // Boş nesne yerine undefined: yapılandırma JSON'unda gereksiz `"headers":{}`
  // bırakmamak için.
  return entries.length === 0 ? undefined : Object.fromEntries(entries);
}

/**
 * Namines Flow kural çekmecesi — bir kuralın kapsamını, tetikleyicisini,
 * koşullarını ve aksiyon zincirini düzenler.
 *
 * Kaydet düğmesi YOK: her değişiklik anında store'a yazılıyor ve store iyimser
 * davranıp sunucuya arka planda bildiriyor (bkz. useAutomationStore).
 */
export default function AutomationRuleDrawer() {
  const selectedRuleId = useAutomationStore(s => s.selectedRuleId);
  const rule = useAutomationStore(s => s.rules.find(r => r.id === s.selectedRuleId));
  const updateRule = useAutomationStore(s => s.updateRule);
  const setSelectedRuleId = useAutomationStore(s => s.setSelectedRuleId);
  const tables = useSchemaStore(s => s.schema?.tables);

  // Seçim yokken hiç mount edilmiyor: alanlar `defaultValue` ile
  // KONTROLSÜZ çalışıyor (her tuş vuruşunda store'a yazmamak için), bu yüzden
  // farklı bir kural seçildiğinde bileşenin baştan kurulması gerekiyor —
  // aksi hâlde önceki kuralın metni kutularda kalırdı.
  if (!selectedRuleId || !rule) return null;

  const isProjectScope = rule.scopeTableId === '';
  const triggerOptions = TRIGGER_OPTIONS.filter(
    opt => isProjectScope || !isRelationTrigger(opt.value),
  );
  const triggerHint = TRIGGER_OPTIONS.find(o => o.value === rule.triggerType)?.hint ?? '';

  // Koşullar somut bir tablo/kolon adına bakıyor; ilişki olaylarında bu
  // bilgilerin hiçbiri yok, dolayısıyla yazılan her koşul kuralı sessizce
  // hiç tetiklenemez hâle getirirdi. Bölüm o yüzden gizleniyor.
  const conditionsSupported = !isRelationTrigger(rule.triggerType);
  const conditionFields: { value: AutomationConditionField; label: string }[] = isColumnTrigger(rule.triggerType)
    ? [
        { value: 'tableName', label: 'Table name' },
        { value: 'columnName', label: 'Column name' },
        { value: 'columnType', label: 'Column type' },
      ]
    : [{ value: 'tableName', label: 'Table name' }];

  const changeScope = (value: string) => {
    const scopeTableId = value === PROJECT_SCOPE ? '' : value;
    // Tabloya dönerken ilişki tetikleyicisi seçiliyse kural asla
    // tetiklenemez hâle gelirdi; sessizce kırılmış bir kural bırakmaktansa
    // tetikleyiciyi geçerli bir değere çekiyoruz.
    const triggerType: TriggerType =
      scopeTableId !== '' && isRelationTrigger(rule.triggerType) ? 'TableDeleted' : rule.triggerType;
    updateRule(rule.id, { scopeTableId, triggerType, ...conditionsPatchFor(triggerType) });
  };

  /**
   * İlişki tetikleyicisine geçerken koşullar TEMİZLENİYOR.
   *
   * Bölüm yalnızca GİZLENSEYDİ koşullar kayıtta kalırdı: ilişki olayında tablo
   * ve kolon adı yok, sunucu bunları boş görüp koşulu düşürür ve kural sessizce
   * hiç tetiklenmez. Kullanıcı ekranda koşul görmediği için sebebini
   * bulamazdı — görünmeyen ama canlı bir ayar, olmayan bir ayardan kötüdür.
   */
  const conditionsPatchFor = (triggerType: TriggerType) =>
    isRelationTrigger(triggerType) && rule.conditions.length > 0
      ? { conditions: [] as AutomationCondition[] }
      : {};

  const setActions = (actions: AutomationActionStep[]) => updateRule(rule.id, { actions });
  const setConditions = (conditions: AutomationCondition[]) => updateRule(rule.id, { conditions });

  const patchAction = (index: number, patch: Partial<AutomationActionStep>) =>
    setActions(rule.actions.map((a, i) => (i === index ? { ...a, ...patch } : a)));

  const moveAction = (index: number, delta: number) => {
    const target = index + delta;
    if (target < 0 || target >= rule.actions.length) return;
    const next = [...rule.actions];
    [next[index], next[target]] = [next[target], next[index]];
    setActions(next);
  };

  return (
    <Dialog.Root open onOpenChange={(open) => { if (!open) setSelectedRuleId(null); }}>
      <Dialog.Portal>
        <Dialog.Overlay className="fixed inset-0 bg-scrim/60 z-[90]" />
        {/* `key`: alanlar kontrolsüz (defaultValue). Çekmeceyi kapatmadan
            başka bir kurala geçildiğinde bu olmadan önceki kuralın metni
            kutularda kalırdı — React alt ağacı yeniden kurmadığı için. */}
        <Dialog.Content
          key={rule.id}
          className="fixed right-0 top-0 h-full w-full max-w-sm bg-surface-800 border-l border-content-primary/10 z-[91] p-5 flex flex-col gap-4 overflow-y-auto"
        >
          <div className="flex items-center justify-between">
            <Dialog.Title className="flex items-center gap-2 text-sm font-semibold text-content-primary">
              <Zap className="w-4 h-4 text-warning-text" />
              Namines Flow
            </Dialog.Title>
            <Dialog.Close asChild>
              <button type="button" aria-label="Close" className="p-1 rounded-[var(--radius-control)] text-content-muted hover:text-content-primary cursor-pointer">
                <X className="w-4 h-4" />
              </button>
            </Dialog.Close>
          </div>

          <label className={labelClass}>
            Name
            <input
              type="text"
              aria-label="Flow name"
              className={inputClass}
              defaultValue={rule.name}
              placeholder="Warn me when a key column goes"
              onBlur={(e) => updateRule(rule.id, { name: e.target.value })}
            />
          </label>

          <label className="flex items-center gap-2 text-xs font-medium text-content-secondary cursor-pointer">
            <input
              type="checkbox"
              checked={rule.enabled}
              onChange={(e) => updateRule(rule.id, { enabled: e.target.checked })}
              className="cursor-pointer accent-warning"
            />
            Enabled
          </label>

          {/* İpuçları bilerek <label>'ın DIŞINDA: etiketin içinde olsalardı
              seçicinin erişilebilir adına karışırlardı (ör. kapsam ipucundaki
              "Relation triggers" yüzünden "Trigger" araması iki alanı birden
              bulur) ve ekran okuyucu her odaklanmada tüm paragrafı okurdu. */}
          <div className="flex flex-col gap-1">
            <label className={labelClass}>
              Scope
              <select
                className={inputClass}
                value={isProjectScope ? PROJECT_SCOPE : rule.scopeTableId}
                onChange={(e) => changeScope(e.target.value)}
              >
                <option value={PROJECT_SCOPE}>Whole project</option>
                {tables?.map(t => (
                  <option key={t.id} value={t.id}>{t.name}</option>
                ))}
                {/* Kuralın bağlı olduğu tablo listede yoksa (şema henüz
                    yüklenmediyse ya da tablo silindiyse) seçicinin değeri
                    hiçbir seçenekle eşleşmez ve tarayıcı ilk seçeneği —
                    "Whole project"i — gösterir. Kural tabloya bağlıyken
                    proje geneliymiş gibi görünürdü; üstelik sonraki her
                    düzenleme bu yanlış kapsamı gerçekten kaydederdi. */}
                {!isProjectScope && !tables?.some(t => t.id === rule.scopeTableId) && (
                  <option value={rule.scopeTableId}>Table not in this schema</option>
                )}
              </select>
            </label>
            <p className="text-micro text-content-muted">
              {isProjectScope
                ? 'Fires for any table. Relation triggers are only available here.'
                : 'Fires only for changes to this table.'}
            </p>
          </div>

          <div className="flex flex-col gap-1">
            <label className={labelClass}>
              Trigger
              <select
                className={inputClass}
                value={rule.triggerType}
                onChange={(e) => {
                  const triggerType = e.target.value as TriggerType;
                  updateRule(rule.id, { triggerType, ...conditionsPatchFor(triggerType) });
                }}
              >
                {triggerOptions.map(opt => (
                  <option key={opt.value} value={opt.value}>{opt.label}</option>
                ))}
              </select>
            </label>
            <p className="text-micro text-content-muted">{triggerHint}</p>
          </div>

          {/* ── Koşullar ──────────────────────────────────────────────── */}
          <section className="flex flex-col gap-2">
            <div className="flex items-center justify-between">
              <h3 className="text-xs font-medium text-content-secondary">Only if</h3>
              {conditionsSupported && (
                <button
                  type="button"
                  aria-label="Add condition"
                  className={iconButtonClass}
                  onClick={() => setConditions([
                    ...rule.conditions,
                    { uid: newUid(), field: conditionFields[0].value, op: 'contains', value: '' },
                  ])}
                >
                  <Plus className="w-3.5 h-3.5" />
                </button>
              )}
            </div>

            {!conditionsSupported ? (
              <p className="text-micro leading-snug text-content-muted">
                Relation events don’t carry a table or column name, so conditions can’t be
                evaluated for this trigger.
              </p>
            ) : rule.conditions.length === 0 ? (
              <p className="text-micro text-content-muted">No conditions — fires every time.</p>
            ) : (
              <ul className="flex flex-col gap-1.5">
                {rule.conditions.map((condition, index) => (
                  // `key` DİZİN DEĞİL: kutular kontrolsüz (defaultValue), dizinle
                  // anahtarlanınca bir satır silindiğinde React DOM'u taşımıyor ve
                  // kutuda önceki komşunun metni kalıyordu.
                  <li key={condition.uid} className="flex items-center gap-1">
                    <select
                      aria-label={`Condition ${index + 1} field`}
                      className={smallInputClass}
                      value={condition.field}
                      onChange={(e) => setConditions(rule.conditions.map((c, i) =>
                        i === index ? { ...c, field: e.target.value as AutomationConditionField } : c))}
                    >
                      {conditionFields.map(f => (
                        <option key={f.value} value={f.value}>{f.label}</option>
                      ))}
                    </select>
                    <select
                      aria-label={`Condition ${index + 1} operator`}
                      className={smallInputClass}
                      value={condition.op}
                      onChange={(e) => setConditions(rule.conditions.map((c, i) =>
                        i === index ? { ...c, op: e.target.value as AutomationConditionOp } : c))}
                    >
                      {CONDITION_OPS.map(o => (
                        <option key={o.value} value={o.value}>{o.label}</option>
                      ))}
                    </select>
                    <input
                      type="text"
                      aria-label={`Condition ${index + 1} value`}
                      className={`${smallInputClass} min-w-0 flex-1`}
                      defaultValue={condition.value}
                      placeholder="_id"
                      onBlur={(e) => setConditions(rule.conditions.map((c, i) =>
                        i === index ? { ...c, value: e.target.value } : c))}
                    />
                    <button
                      type="button"
                      aria-label={`Remove condition ${index + 1}`}
                      className={iconButtonClass}
                      onClick={() => setConditions(rule.conditions.filter((_, i) => i !== index))}
                    >
                      <Trash2 className="w-3.5 h-3.5" />
                    </button>
                  </li>
                ))}
              </ul>
            )}
          </section>

          {/* ── Aksiyon zinciri ───────────────────────────────────────── */}
          <section className="flex flex-col gap-2">
            <div className="flex items-center justify-between">
              <h3 className="text-xs font-medium text-content-secondary">Then, in order</h3>
              <button
                type="button"
                aria-label="Add action"
                className={iconButtonClass}
                onClick={() => setActions([...rule.actions, { uid: newUid(), actionType: 'Toast', actionConfig: {} }])}
              >
                <Plus className="w-3.5 h-3.5" />
              </button>
            </div>

            {rule.actions.length === 0 ? (
              <p className="text-micro text-content-muted">
                No actions yet — this flow will fire but do nothing.
              </p>
            ) : (
              <ul className="flex flex-col gap-2">
                {rule.actions.map((action, index) => (
                  <li
                    key={action.uid}
                    className="rounded-[var(--radius-card)] border border-content-primary/10 bg-surface-700 p-2.5"
                  >
                    <div className="flex items-center gap-1">
                      <span className="text-micro font-semibold text-content-muted">{index + 1}</span>
                      <select
                        aria-label={`Action ${index + 1}`}
                        className={`${smallInputClass} min-w-0 flex-1`}
                        value={action.actionType}
                        onChange={(e) => patchAction(index, { actionType: e.target.value as AutomationActionType })}
                      >
                        {ACTION_OPTIONS.map(opt => (
                          <option key={opt.value} value={opt.value}>{opt.label}</option>
                        ))}
                      </select>
                      <button
                        type="button"
                        aria-label={`Move action ${index + 1} up`}
                        className={iconButtonClass}
                        disabled={index === 0}
                        onClick={() => moveAction(index, -1)}
                      >
                        <ArrowUp className="w-3.5 h-3.5" />
                      </button>
                      <button
                        type="button"
                        aria-label={`Move action ${index + 1} down`}
                        className={iconButtonClass}
                        disabled={index === rule.actions.length - 1}
                        onClick={() => moveAction(index, 1)}
                      >
                        <ArrowDown className="w-3.5 h-3.5" />
                      </button>
                      <button
                        type="button"
                        aria-label={`Remove action ${index + 1}`}
                        className={iconButtonClass}
                        onClick={() => setActions(rule.actions.filter((_, i) => i !== index))}
                      >
                        <Trash2 className="w-3.5 h-3.5" />
                      </button>
                    </div>

                    <p className="mt-1 text-micro leading-snug text-content-muted">
                      {ACTION_OPTIONS.find(o => o.value === action.actionType)?.hint}
                    </p>

                    {URL_ACTIONS.includes(action.actionType) && (
                      <input
                        type="url"
                        aria-label={index === 0 ? 'Webhook URL' : `Webhook URL ${index + 1}`}
                        className={`${smallInputClass} mt-1.5 w-full`}
                        defaultValue={action.actionConfig.url ?? ''}
                        placeholder="https://example.com/hook"
                        onBlur={(e) => patchAction(index, {
                          actionConfig: { ...action.actionConfig, url: e.target.value },
                        })}
                      />
                    )}

                    {MESSAGE_ACTIONS.includes(action.actionType) && (
                      <textarea
                        aria-label={`Message ${index + 1}`}
                        rows={2}
                        className={`${smallInputClass} mt-1.5 w-full resize-y`}
                        defaultValue={action.actionConfig.message ?? ''}
                        placeholder="Namines Flow: {{trigger}} on {{tableName}}"
                        onBlur={(e) => patchAction(index, {
                          actionConfig: { ...action.actionConfig, message: e.target.value },
                        })}
                      />
                    )}

                    {action.actionType === 'Webhook' && (
                      <>
                        <select
                          aria-label={`Method ${index + 1}`}
                          className={`${smallInputClass} mt-1.5 w-full`}
                          value={action.actionConfig.method ?? 'POST'}
                          onChange={(e) => patchAction(index, {
                            actionConfig: { ...action.actionConfig, method: e.target.value as 'POST' | 'PUT' | 'PATCH' },
                          })}
                        >
                          <option value="POST">POST</option>
                          <option value="PUT">PUT</option>
                          <option value="PATCH">PATCH</option>
                        </select>
                        <textarea
                          aria-label={`Body ${index + 1}`}
                          rows={2}
                          className={`${smallInputClass} mt-1.5 w-full resize-y font-mono`}
                          defaultValue={action.actionConfig.body ?? ''}
                          placeholder='Leave empty for the default payload'
                          onBlur={(e) => patchAction(index, {
                            actionConfig: { ...action.actionConfig, body: e.target.value },
                          })}
                        />
                        {/* Başlıklar tek bir metin alanından düzenleniyor.
                            Anahtar/değer için ayrı satır arayüzü kurmak, bu
                            alanın beklenen kullanımına (bir Authorization
                            başlığı) göre fazla ağır kaçardı. */}
                        <textarea
                          aria-label={`Headers ${index + 1}`}
                          rows={2}
                          className={`${smallInputClass} mt-1.5 w-full resize-y font-mono`}
                          defaultValue={headersToText(action.actionConfig.headers)}
                          placeholder={'Authorization: Bearer …\nX-Source: namines'}
                          onBlur={(e) => patchAction(index, {
                            actionConfig: { ...action.actionConfig, headers: textToHeaders(e.target.value) },
                          })}
                        />
                        <p className="mt-0.5 text-micro text-content-subtle">One header per line, as Name: value</p>
                      </>
                    )}

                    {(URL_ACTIONS.includes(action.actionType) || action.actionType === 'Toast') && (
                      <p className="mt-1 text-micro leading-snug text-content-subtle">
                        Variables: <span className="font-mono">{TEMPLATE_VARIABLES}</span>
                      </p>
                    )}
                  </li>
                ))}
              </ul>
            )}
          </section>

          <AutomationRunHistory ruleId={rule.id} />
        </Dialog.Content>
      </Dialog.Portal>
    </Dialog.Root>
  );
}
