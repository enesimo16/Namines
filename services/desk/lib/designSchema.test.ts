import { describe, it, expect } from 'vitest';
import { parseDesignTables, parseNodePositions } from './designSchema';

/**
 * `lib/designSchema.ts` — `CloudProject.SchemaJson`/`NodePositionsJson`'u
 * Canvas için ayrıştırır. Ana uygulamanın `DatabaseSchema` tipi bilinçli
 * olarak KOPYALANMIYOR (mikroservis sınırı), bu yüzden ayrıştırıcı hem
 * camelCase (ana frontend) hem PascalCase (.NET varsayılanı) alan adlarını
 * kabul etmek zorunda — testler bunu iki yönden de doğruluyor.
 */

describe('parseDesignTables', () => {
  it('null girdide boş dizi döner — uydurmaz', () => {
    expect(parseDesignTables(null)).toEqual([]);
  });

  it('bozuk JSONda boş dizi döner, atmaz', () => {
    expect(parseDesignTables('{not valid json')).toEqual([]);
  });

  it('camelCase alan adlarını okur', () => {
    const json = JSON.stringify({ tables: [{ id: 't1', name: 'customers' }] });
    expect(parseDesignTables(json)).toEqual([{ id: 't1', name: 'customers' }]);
  });

  it('PascalCase alan adlarını da okur (.NET tarafı)', () => {
    const json = JSON.stringify({ Tables: [{ Id: 't1', Name: 'customers' }] });
    expect(parseDesignTables(json)).toEqual([{ id: 't1', name: 'customers' }]);
  });

  it('tables dizi değilse boş dizi döner', () => {
    expect(parseDesignTables(JSON.stringify({ tables: 'nope' }))).toEqual([]);
  });

  it('id veya name eksik/yanlış tipteki tabloları eler, geçerlileri tutar', () => {
    const json = JSON.stringify({
      tables: [
        { id: 't1', name: 'ok' },
        { id: 't2' },              // name eksik
        { name: 'no-id' },          // id eksik
        { id: 123, name: 'bad' },   // id yanlış tip
      ],
    });
    expect(parseDesignTables(json)).toEqual([{ id: 't1', name: 'ok' }]);
  });

  it('boş tables dizisinde boş dizi döner', () => {
    expect(parseDesignTables(JSON.stringify({ tables: [] }))).toEqual([]);
  });
});

describe('parseNodePositions', () => {
  it('null girdide boş nesne döner', () => {
    expect(parseNodePositions(null)).toEqual({});
  });

  it('bozuk JSONda boş nesne döner, atmaz', () => {
    expect(parseNodePositions('{not valid')).toEqual({});
  });

  it('camelCase {x,y} okur', () => {
    const json = JSON.stringify({ t1: { x: 10, y: 20 } });
    expect(parseNodePositions(json)).toEqual({ t1: { x: 10, y: 20 } });
  });

  it('PascalCase {X,Y} okur', () => {
    const json = JSON.stringify({ t1: { X: 10, Y: 20 } });
    expect(parseNodePositions(json)).toEqual({ t1: { x: 10, y: 20 } });
  });

  it('x/y sayı değilse o girdiyi atlar, diğerlerini korur', () => {
    const json = JSON.stringify({
      t1: { x: 10, y: 20 },
      t2: { x: 'oops', y: 20 },
    });
    expect(parseNodePositions(json)).toEqual({ t1: { x: 10, y: 20 } });
  });

  it('birden çok tabloyu birlikte ayrıştırır', () => {
    const json = JSON.stringify({
      t1: { x: 0, y: 0 },
      t2: { x: 300, y: 260 },
    });
    expect(parseNodePositions(json)).toEqual({
      t1: { x: 0, y: 0 },
      t2: { x: 300, y: 260 },
    });
  });
});
