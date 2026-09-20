import { describe, it, expect } from 'vitest';
import { renderFlowTemplate, type FlowTemplateContext } from './naminesFlowTemplate';

/**
 * Sunucudaki AutomationTemplateTests ile AYNI davranışı koruyor. İki uygulama
 * ayrışırsa aynı kural, bildirimde bir şey gösterip webhook'ta başka bir şey
 * gönderir.
 */
const AT = new Date('2026-09-20T08:30:00Z');
const ctx = { tableName: 'orders', columnName: 'user_id', columnType: 'INT' };
const render = (t: string, c: FlowTemplateContext = ctx) => renderFlowTemplate(t, 'ColumnDeleted', c, 'Shop', AT);

describe('renderFlowTemplate', () => {
  it('butun degiskenleri dolduruyor', () => {
    expect(render('{{trigger}} {{tableName}} {{columnName}} {{columnType}} {{projectName}}'))
      .toBe('ColumnDeleted orders user_id INT Shop');
  });

  it('degeri olmayan degisken bos stringe cevriliyor', () => {
    expect(render('[{{columnName}}]', { tableName: 'orders' })).toBe('[]');
  });

  it('bilinmeyen degisken OLDUGU GIBI kaliyor', () => {
    expect(render('x {{nope}} y')).toBe('x {{nope}} y');
  });

  it('kapanmamis yer tutucu metni bozmuyor', () => {
    expect(render('a {{tableName')).toBe('a {{tableName');
  });

  it('bos sablon bos string donuyor', () => {
    expect(renderFlowTemplate(undefined, 'TableAdded', ctx, 'p', AT)).toBe('');
    expect(renderFlowTemplate('', 'TableAdded', ctx, 'p', AT)).toBe('');
  });

  it('sablon icindeki degerler YENIDEN islenmiyor', () => {
    // Tablo adı "{{projectName}}" olsaydı, ikinci bir geçiş onu proje adına
    // çevirirdi — kullanıcı kontrolündeki bir değerle değer enjeksiyonu.
    expect(render('{{tableName}}', { tableName: '{{projectName}}' })).toBe('{{projectName}}');
  });

  it('zaman damgasi ISO-8601', () => {
    expect(render('{{timestamp}}')).toBe('2026-09-20T08:30:00.000Z');
  });
});
