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

/** Bu olayın tetiklediği, etkin kurallar. */
export function matchRules(event: NaminesFlowEvent, rules: AutomationRule[]): AutomationRule[] {
  const tableIds = eventTableIds(event);
  return rules.filter(
    rule => rule.enabled && rule.triggerType === event.type && tableIds.includes(rule.scopeTableId),
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
