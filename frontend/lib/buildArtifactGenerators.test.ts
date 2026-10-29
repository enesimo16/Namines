import { describe, it, expect } from 'vitest';
import { generateMermaidByType } from './buildArtifactGenerators';
import type { DatabaseSchema } from '../types/schema';

describe('generateMermaidByType ER', () => {
  const schema = {
    schemaId: 's',
    name: 'S',
    tables: [
      {
        id: 't1',
        name: 'order items',
        columns: [
          { id: 'c1', name: 'order id', type: 'varchar(255)', isPK: true },
          { id: 'c2', name: 'qty', type: 'decimal(10, 2)', isPK: false },
        ],
      },
      { id: 't2', name: '2fa-codes', columns: [{ id: 'c3', name: 'id', type: 'int', isPK: true }] },
    ],
    relations: [{ id: 'r1', sourceTableId: 't1', targetTableId: 't2' }],
  } as unknown as DatabaseSchema;

  it('boşluk, parantez ve tire içeren adları Mermaid için geçerli kimliğe çevirir', () => {
    const out = generateMermaidByType(schema, 'ER');
    expect(out).toContain('order_items {');
    expect(out).toContain('varchar order_id PK');
    expect(out).toContain('decimal qty');
    expect(out).toContain('_2fa_codes {');
    expect(out).toContain('order_items }|--|| _2fa_codes : references');
    expect(out).not.toMatch(/[( ]255\)/);
  });
});
