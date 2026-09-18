import { describe, it, expect } from 'vitest';
import { decideCanvasBoot } from './canvasBoot';
import type { DatabaseSchema } from '../types/schema';

const schemaWithTables = (name: string): DatabaseSchema => ({
  schemaId: 'sid-1',
  name,
  tables: [{ id: 't_users', name: 'users', columns: [] } as never],
  relations: [],
});

const emptySchema = (name: string): DatabaseSchema => ({
  schemaId: 'sid-2',
  name,
  tables: [],
  relations: [],
});

describe('decideCanvasBoot', () => {
  it('hidrasyon bitmeden HİÇBİR ŞEY yüklemiyor', () => {
    // BULUNMA YERİ: asıl veri kaybı burada oluyordu. Hidrasyon bitmeden
    // `projects` boş bir dizi ve "proje yok" ile "henüz okumadık" ayırt
    // edilemiyor. Boş şema kurulunca otomatik kaydetme üç saniye sonra onu
    // kayıtlı projenin üstüne yazıyordu.
    const decision = decideCanvasBoot({
      hasSchema: false,
      joinedSharedRoom: false,
      hasHydrated: false,
      activeProjectId: 'p1',
      projects: [],
    });

    expect(decision.action).toBe('wait');
  });

  it('yenilemeden sonra aktif projenin şemasını geri yüklüyor', () => {
    const decision = decideCanvasBoot({
      hasSchema: false,
      joinedSharedRoom: false,
      hasHydrated: true,
      activeProjectId: 'p1',
      projects: [{ id: 'p1', schema: schemaWithTables('blog_platform'), nodePositions: { t_users: { x: 10, y: 20 } } }],
    });

    expect(decision).toEqual({
      action: 'restore',
      schema: schemaWithTables('blog_platform'),
      nodePositions: { t_users: { x: 10, y: 20 } },
    });
  });

  it('hiç proje yoksa boş şema kuruyor', () => {
    // İlk kez gelen kullanıcı boş sayfa değil, karşılama tuvali görmeli.
    const decision = decideCanvasBoot({
      hasSchema: false,
      joinedSharedRoom: false,
      hasHydrated: true,
      activeProjectId: null,
      projects: [],
    });

    expect(decision.action).toBe('empty');
  });

  it('aktif proje tablosuzsa boş şema kuruyor', () => {
    const decision = decideCanvasBoot({
      hasSchema: false,
      joinedSharedRoom: false,
      hasHydrated: true,
      activeProjectId: 'p1',
      projects: [{ id: 'p1', schema: emptySchema('bos') }],
    });

    expect(decision.action).toBe('empty');
  });

  it('şema zaten yüklüyse dokunmuyor', () => {
    // Aksi hâlde her render kayıtlı şemayı yeniden yükleyip kullanıcının
    // o anki düzenlemesini geri alırdı.
    const decision = decideCanvasBoot({
      hasSchema: true,
      joinedSharedRoom: false,
      hasHydrated: true,
      activeProjectId: 'p1',
      projects: [{ id: 'p1', schema: schemaWithTables('x') }],
    });

    expect(decision.action).toBe('wait');
  });

  it('paylasilan odaya katilindiysa sema karsi taraftan bekleniyor', () => {
    const decision = decideCanvasBoot({
      hasSchema: false,
      joinedSharedRoom: true,
      hasHydrated: true,
      activeProjectId: 'p1',
      projects: [{ id: 'p1', schema: schemaWithTables('x') }],
    });

    expect(decision.action).toBe('wait');
  });

  it('aktif proje listede yoksa boş şema kuruyor', () => {
    const decision = decideCanvasBoot({
      hasSchema: false,
      joinedSharedRoom: false,
      hasHydrated: true,
      activeProjectId: 'silinmis',
      projects: [{ id: 'p1', schema: schemaWithTables('x') }],
    });

    expect(decision.action).toBe('empty');
  });
});
