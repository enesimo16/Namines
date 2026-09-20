import { describe, it, expect } from 'vitest';
import { NAMINES_FLOW_TEMPLATES } from './naminesFlowTemplates';

/**
 * Şablonlar kullanıcının sisteme ilk girişi. Bozuk bir şablon, çalışmayan bir
 * kural kurup kullanıcıyı özelliğin bozuk olduğuna inandırır — o yüzden
 * geçerlilikleri burada korunuyor.
 */
describe('NAMINES_FLOW_TEMPLATES', () => {
  it('id ler benzersiz', () => {
    const ids = NAMINES_FLOW_TEMPLATES.map(t => t.id);
    expect(new Set(ids).size).toBe(ids.length);
  });

  it('her sablonun en az bir aksiyonu var', () => {
    // Aksiyonsuz bir şablon tetiklenir ama hiçbir şey yapmaz.
    for (const t of NAMINES_FLOW_TEMPLATES) {
      expect(t.actions.length).toBeGreaterThan(0);
    }
  });

  it('iliski tetikleyicisi kullanan sablon tabloya BAGLI olamaz', () => {
    // İlişki olayları bir tabloya atfedilemiyor; tabloya bağlı bir kuralda
    // asla tetiklenmezlerdi.
    for (const t of NAMINES_FLOW_TEMPLATES) {
      if (t.triggerType === 'RelationAdded' || t.triggerType === 'RelationDeleted') {
        expect(t.requiresTable).toBe(false);
      }
    }
  });

  it('kolon kosulu yalnizca kolon tetikleyicilerinde kullaniliyor', () => {
    // Tablo olayında kolon adı yok; böyle bir koşul kuralı sessizce ölü yapar.
    const columnTriggers = ['ColumnAdded', 'ColumnDeleted', 'ColumnChanged'];
    for (const t of NAMINES_FLOW_TEMPLATES) {
      const usesColumn = t.conditions.some(c => c.field === 'columnName' || c.field === 'columnType');
      if (usesColumn) expect(columnTriggers).toContain(t.triggerType);
    }
  });

  it('her sablonun adi var', () => {
    // Adsız kural listede yalnızca tablo adıyla görünür; şablonla kurulan
    // kuralların ayırt edilebilir olması gerekiyor.
    for (const t of NAMINES_FLOW_TEMPLATES) {
      expect(t.name.trim()).not.toBe('');
    }
  });

  it('koşul ve çoklu aksiyon en az bir sablonda gosteriliyor', () => {
    // Şablonların bir işlevi de bu iki özelliğin VARLIĞINI duyurmak.
    expect(NAMINES_FLOW_TEMPLATES.some(t => t.conditions.length > 0)).toBe(true);
    expect(NAMINES_FLOW_TEMPLATES.some(t => t.actions.length > 1)).toBe(true);
  });
});
