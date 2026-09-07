import { describe, it, expect } from 'vitest';
import {
  fieldKind, displayColumn, primaryKey, isEditable,
  insertableColumns, editableColumns, normalizeValue, formatCell,
  type DeskColumn, type DeskTable,
} from './schema';

/**
 * `lib/schema.ts` — Desk'in tüm ürününün üstüne kurulu olduğu deterministik
 * katman (third-phase/00-BASLA-BURADAN.md §1: "Deterministik olabilmesinin
 * sebebi"). Bu dosyada AI yok; aynı girdi HER ZAMAN aynı çıktıyı üretmeli —
 * o yüzden testler de saf girdi/çıktı, mock/network yok.
 */

function col(overrides: Partial<DeskColumn> = {}): DeskColumn {
  return {
    name: 'col', type: 'text', length: null,
    isPK: false, isFK: false, isNullable: true, references: null,
    ...overrides,
  };
}

function table(columns: DeskColumn[], overrides: Partial<DeskTable> = {}): DeskTable {
  return { name: 'table', canWrite: true, columns, ...overrides };
}

describe('fieldKind', () => {
  it('FK her zaman reference döner — tip ne olursa olsun', () => {
    expect(fieldKind(col({ type: 'integer', references: { table: 'customers', column: 'id' } })))
      .toBe('reference');
  });

  it.each([
    ['boolean', 'boolean'], ['bool', 'boolean'], ['bit', 'boolean'],
    ['timestamp', 'datetime'], ['timestamp without time zone', 'datetime'], ['datetime', 'datetime'],
    ['date', 'date'],
    ['int', 'number'], ['int4', 'number'], ['bigint', 'number'], ['serial', 'number'],
    ['numeric', 'number'], ['decimal', 'number'], ['float', 'number'], ['double precision', 'number'],
    ['real', 'number'], ['money', 'number'],
    ['text', 'textarea'], ['json', 'textarea'], ['jsonb', 'textarea'], ['xml', 'textarea'], ['clob', 'textarea'],
  ] as const)('%s → %s', (type, expected) => {
    expect(fieldKind(col({ type }))).toBe(expected);
  });

  it('uzunluğu 255den fazla varchar → textarea (metin tipi ama uzun)', () => {
    expect(fieldKind(col({ type: 'varchar', length: 500 }))).toBe('textarea');
  });

  it('uzunluğu 255 veya altı varchar → text', () => {
    expect(fieldKind(col({ type: 'varchar', length: 255 }))).toBe('text');
    expect(fieldKind(col({ type: 'varchar', length: 50 }))).toBe('text');
  });

  it('tanınmayan tip → text (en genel olana düşer, yanlış bileşen göstermez)', () => {
    expect(fieldKind(col({ type: 'some_exotic_engine_type' }))).toBe('text');
  });

  it('length null ve tip metin değilse text kalır', () => {
    expect(fieldKind(col({ type: 'char', length: null }))).toBe('text');
  });
});

describe('displayColumn', () => {
  it('tercih edilen adlardan ilk eşleşeni seçer (sıra: name > title > ... > email)', () => {
    const t = table([
      col({ name: 'id', isPK: true }),
      col({ name: 'email' }),
      col({ name: 'name' }),
    ]);
    expect(displayColumn(t)).toBe('name');
  });

  it('büyük/küçük harf duyarsız eşleşir', () => {
    const t = table([col({ name: 'id', isPK: true }), col({ name: 'Full_Name' })]);
    expect(displayColumn(t)).toBe('Full_Name');
  });

  it('tercih edilen ad yoksa PK-olmayan ilk metin (fieldKind: "text") kolonuna düşer', () => {
    // Not: DB tipi 'text' ⇒ fieldKind 'textarea' üretir (uzun metin), 'text'
    // DEĞİL — bu yüzden burada kısa bir 'varchar' kullanılıyor (bkz. fieldKind testleri).
    const t = table([
      col({ name: 'id', isPK: true, type: 'int' }),
      col({ name: 'age', type: 'int' }),
      col({ name: 'note', type: 'varchar', length: 100 }),
    ]);
    expect(displayColumn(t)).toBe('note');
  });

  it('metin kolonu da yoksa birincil anahtara düşer', () => {
    const t = table([col({ name: 'id', isPK: true, type: 'int' }), col({ name: 'amount', type: 'int' })]);
    expect(displayColumn(t)).toBe('id');
  });

  it('hiçbir aday yoksa ilk kolona düşer', () => {
    // Bileşik PK → primaryKey() null döner, o zaman ilk kolona düşülür.
    const t = table([
      col({ name: 'a', isPK: true, type: 'int' }),
      col({ name: 'b', isPK: true, type: 'int' }),
    ]);
    expect(displayColumn(t)).toBe('a');
  });
});

describe('primaryKey', () => {
  it('tek PK varsa adını döner', () => {
    expect(primaryKey(table([col({ name: 'id', isPK: true })]))).toBe('id');
  });

  it('bileşik PK (2+) varsa null döner — third-phase §9un bilinçli kararı', () => {
    const t = table([col({ name: 'a', isPK: true }), col({ name: 'b', isPK: true })]);
    expect(primaryKey(t)).toBeNull();
  });

  it('PK yoksa null döner', () => {
    expect(primaryKey(table([col({ name: 'x' })]))).toBeNull();
  });
});

describe('isEditable', () => {
  it('yazma izni VE tek kolonlu PK varsa true', () => {
    const t = table([col({ name: 'id', isPK: true })], { canWrite: true });
    expect(isEditable(t)).toBe(true);
  });

  it('yazma izni yoksa false (PK tek olsa bile)', () => {
    const t = table([col({ name: 'id', isPK: true })], { canWrite: false });
    expect(isEditable(t)).toBe(false);
  });

  it('bileşik PK varsa false (yazma izni olsa bile) — yanlış satırı güncelleme riski', () => {
    const t = table([col({ name: 'a', isPK: true }), col({ name: 'b', isPK: true })], { canWrite: true });
    expect(isEditable(t)).toBe(false);
  });
});

describe('insertableColumns', () => {
  it('otomatik artan (serial/identity) PK ekleme formundan çıkarılır', () => {
    const t = table([
      col({ name: 'id', isPK: true, type: 'serial' }),
      col({ name: 'name', type: 'text' }),
    ]);
    expect(insertableColumns(t).map(c => c.name)).toEqual(['name']);
  });

  it('sayısal tipli PK de (identity bilgisi taşımasa bile) çıkarılır', () => {
    const t = table([col({ name: 'id', isPK: true, type: 'int' }), col({ name: 'name' })]);
    expect(insertableColumns(t).map(c => c.name)).toEqual(['name']);
  });

  it('metin tipli PK (otomatik artan olmayan, ör. UUID) formda KALIR', () => {
    const t = table([col({ name: 'id', isPK: true, type: 'uuid' }), col({ name: 'name' })]);
    expect(insertableColumns(t).map(c => c.name)).toEqual(['id', 'name']);
  });

  it('PK olmayan kolonlar her zaman kalır', () => {
    const t = table([col({ name: 'a' }), col({ name: 'b' })]);
    expect(insertableColumns(t)).toHaveLength(2);
  });
});

describe('editableColumns', () => {
  it('anahtar kolonu düzenleme formundan çıkarır', () => {
    const t = table([col({ name: 'id', isPK: true }), col({ name: 'name' })]);
    expect(editableColumns(t).map(c => c.name)).toEqual(['name']);
  });

  it('bileşik PK durumunda (primaryKey() null) hiçbir kolon çıkarılmaz', () => {
    const t = table([col({ name: 'a', isPK: true }), col({ name: 'b', isPK: true })]);
    expect(editableColumns(t).map(c => c.name)).toEqual(['a', 'b']);
  });
});

describe('normalizeValue', () => {
  it('nullable kolonda boş metin → null (NULL ile boş metin AYNI ŞEY DEĞİL)', () => {
    expect(normalizeValue(col({ isNullable: true }), '')).toBeNull();
  });

  it('NOT NULL kolonda boş metin → boş metin olarak KALIR (geçerli bir değer)', () => {
    expect(normalizeValue(col({ isNullable: false }), '')).toBe('');
  });

  it('boş olmayan değer hiçbir durumda dönüştürülmez', () => {
    expect(normalizeValue(col({ isNullable: true }), 'hello')).toBe('hello');
    expect(normalizeValue(col({ isNullable: false }), 'hello')).toBe('hello');
  });
});

describe('formatCell', () => {
  it('null/undefined → em-dash', () => {
    expect(formatCell(null)).toBe('—');
    expect(formatCell(undefined)).toBe('—');
  });

  it('boolean → onay/çarpı işareti', () => {
    expect(formatCell(true)).toBe('✓');
    expect(formatCell(false)).toBe('✗');
  });

  it('diğer değerler String()e çevrilir', () => {
    expect(formatCell(42)).toBe('42');
    expect(formatCell('hello')).toBe('hello');
  });
});
