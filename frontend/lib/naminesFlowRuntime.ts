import type { NaminesFlowEvent } from './naminesFlowEventBus';
import type { AutomationRule } from '../store/useAutomationStore';

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
 * yazılabilsin ve koşul desteği (Faz 3) buraya tek noktadan eklenebilsin.
 */

/**
 * Olayın hangi tablo(lar)a ait olduğu.
 *
 * İlişki olaylarının tek bir sahibi yok — hem kaynak hem hedef tabloyu
 * ilgilendiriyorlar, bu yüzden ikisi de döner. Sunucudaki
 * `AutomationRuleMatcher` şu an ilişki tetikleyicilerini yalnızca proje
 * geneli kurallarda eşleştiriyor (diff ilişkileri bir tabloya atfetmiyor);
 * istemci tarafında bu bilgi elimizde olduğu için tabloya bağlı kurallar da
 * eşleşebiliyor.
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
 * Olayın koşullara sunduğu değerler. İstemci tarafında kolon TİPİ elde
 * olmadığı için (`NaminesFlowEvent` yalnızca ad taşıyor) `columnType`
 * koşulları burada eşleşemiyor — sunucu tarafı onları doğru değerlendiriyor.
 */
function contextOf(event: NaminesFlowEvent): { columnName?: string } {
  switch (event.type) {
    case 'ColumnAdded':
    case 'ColumnDeleted':
    case 'ColumnChanged':
      return { columnName: event.columnName };
    default:
      return {};
  }
}

/**
 * Koşulların istemci tarafı karşılığı — sunucudaki
 * `AutomationConditionEvaluator` ile AYNI anlamda olmalı, yoksa aynı kural
 * tarayıcıda toast basarken sunucuda sessiz kalır (ya da tersi) ve kullanıcı
 * hangisinin doğru olduğunu anlayamaz.
 *
 * `tableName` bilgisi olay içinde YOK (olaylar tabloyu id ile taşıyor, adla
 * değil); o koşul burada değerlendirilemediği için kural düşürülmüyor —
 * istemci tarafı yalnızca anlık geri bildirim veriyor ve yanlış susmaktansa
 * fazladan göstermek daha az zararlı.
 */
function conditionsHold(rule: AutomationRule, event: NaminesFlowEvent): boolean {
  if (rule.conditions.length === 0) return true;
  const ctx = contextOf(event);

  return rule.conditions.every(condition => {
    if (condition.field !== 'columnName') return true;
    const actual = ctx.columnName;
    if (actual === undefined) return false;

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
export function matchRules(event: NaminesFlowEvent, rules: AutomationRule[]): AutomationRule[] {
  const tableIds = eventTableIds(event);
  return rules.filter(rule =>
    rule.enabled &&
    rule.triggerType === event.type &&
    // Boş kapsam = proje geneli: tablo eşleşmesi aranmıyor.
    (rule.scopeTableId === '' || tableIds.includes(rule.scopeTableId)) &&
    conditionsHold(rule, event),
  );
}

/** Kullanıcıya gösterilecek toast metni. */
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
