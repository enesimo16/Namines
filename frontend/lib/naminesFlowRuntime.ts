import type { NaminesFlowEvent } from './naminesFlowEventBus';
import type { AutomationRule } from '../store/useAutomationStore';
import type { FlowTemplateContext } from './naminesFlowTemplate';

/**
 * Namines Flow'un İSTEMCİ TARAFI çalışma zamanı — saf eşleştirme mantığı.
 *
 * `naminesFlowEventBus` yalnızca olayları yayınlar; bu modül o olayları
 * kullanıcının tanımladığı kurallarla eşleştirir. Tasarımın "anlık tepki"
 * yarısı buradan çalışır (toast + canvas'ta tetiklenme animasyonu); webhook /
 * DBA / seed gibi sunucu tarafı aksiyonlar bu yoldan GEÇMEZ — onlar
 * senkronizasyon döngüsünde sunucunun kendi diff'inden tetiklenir.
 *
 * React'ten bağımsız saf fonksiyonlar olarak duruyor ki birim testi
 * yazılabilsin.
 */

/**
 * Olayın hangi tablo(lar)a ait olduğu.
 *
 * İlişki olaylarının tek bir sahibi yok — hem kaynak hem hedef tabloyu
 * ilgilendiriyorlar, bu yüzden ikisi de döner.
 */
export function eventTableIds(event: NaminesFlowEvent): string[] {
  switch (event.type) {
    case 'RelationAdded':
    case 'RelationDeleted':
      return [event.sourceTableId, event.targetTableId];
    default:
      return [event.tableId];
  }
}

/**
 * Olayın koşullara ve şablonlara sunduğu değerler.
 *
 * <b>`resolveTableName` neden dışarıdan geliyor:</b> olaylar tabloyu ID ile
 * taşıyor, ADLA değil. Ad çözülmeden `tableName` koşulu istemcide hiç
 * değerlendirilemiyordu ve sunucuyla ayrışıyordu; şema deposu bu eşlemeyi
 * bildiği için çağıran taraf sağlıyor, bu fonksiyon saf kalıyor.
 *
 * `columnType` HÂLÂ yok: olay yalnızca kolon adını taşıyor. Aşağıdaki
 * `conditionsHold` bunu bilerek "düşürmüyor" — bkz. oradaki açıklama.
 */
export function buildContext(
  event: NaminesFlowEvent,
  resolveTableName?: (tableId: string) => string | undefined,
): FlowTemplateContext {
  const tableName = resolveTableName
    ? eventTableIds(event).map(resolveTableName).find(Boolean)
    : undefined;

  switch (event.type) {
    case 'ColumnAdded':
    case 'ColumnDeleted':
    case 'ColumnChanged':
      return { tableName, columnName: event.columnName };
    default:
      return { tableName };
  }
}

/**
 * Koşulların istemci tarafı karşılığı — sunucudaki
 * `AutomationConditionEvaluator` ile AYNI anlamda olmalı, yoksa aynı kural
 * tarayıcıda toast basarken sunucuda sessiz kalır (ya da tersi).
 *
 * <b>Değerlendirilemeyen koşul kuralı DÜŞÜRMÜYOR.</b> `columnType` olay içinde
 * taşınmıyor; o koşulu "eşleşmedi" saymak, sunucunun tetikleyeceği bir kuralda
 * istemcinin sessiz kalması demek olurdu. Fazladan bildirim göstermek, hiç
 * göstermemekten daha az zararlı — sunucu tarafı aksiyonlar zaten doğru
 * değerlendiriliyor.
 */
function conditionsHold(rule: AutomationRule, context: FlowTemplateContext): boolean {
  if (rule.conditions.length === 0) return true;

  return rule.conditions.every(condition => {
    const actual =
      condition.field === 'tableName' ? context.tableName
      : condition.field === 'columnName' ? context.columnName
      : context.columnType;

    // İstemcide bilinmeyen alan: kural düşürülmüyor (yukarıdaki gerekçe).
    if (actual === undefined) return true;

    const a = actual.toLowerCase();
    const b = (condition.value ?? '').toLowerCase();
    switch (condition.op) {
      case 'equals': return a === b;
      case 'notEquals': return a !== b;
      case 'contains': return a.includes(b);
      case 'startsWith': return a.startsWith(b);
      case 'endsWith': return a.endsWith(b);
      default: return false;
    }
  });
}

/** Bu olayın tetiklediği, etkin kurallar. */
export function matchRules(
  event: NaminesFlowEvent,
  rules: AutomationRule[],
  context: FlowTemplateContext = buildContext(event),
): AutomationRule[] {
  const tableIds = eventTableIds(event);
  const isRelation = event.type === 'RelationAdded' || event.type === 'RelationDeleted';

  return rules.filter(rule =>
    rule.enabled &&
    rule.triggerType === event.type &&
    // İlişki olayları bir tabloya atfedilemiyor; sunucudaki eşleştirici de
    // onları YALNIZCA proje geneli kurallarda değerlendiriyor. İstemcinin
    // kaynak/hedef tabloya bağlı kuralları da eşleştirmesi, aynı kuralın
    // tarayıcıda tetiklenip sunucuda sessiz kalmasına yol açıyordu.
    (isRelation
      ? rule.scopeTableId === ''
      : rule.scopeTableId === '' || tableIds.includes(rule.scopeTableId)) &&
    conditionsHold(rule, context),
  );
}

/** Kullanıcı bir mesaj yazmadıysa gösterilecek varsayılan toast metni. */
export function toastMessageFor(event: NaminesFlowEvent): string {
  switch (event.type) {
    case 'TableAdded':
      return `Namines Flow: table "${event.tableName}" added`;
    case 'TableDeleted':
      return `Namines Flow: table "${event.tableName}" deleted`;
    case 'ColumnAdded':
      return `Namines Flow: column "${event.columnName}" added`;
    case 'ColumnDeleted':
      return `Namines Flow: column "${event.columnName}" deleted`;
    case 'ColumnChanged':
      return `Namines Flow: column "${event.columnName}" changed`;
    case 'RelationAdded':
      return 'Namines Flow: relation added';
    case 'RelationDeleted':
      return 'Namines Flow: relation deleted';
  }
}
