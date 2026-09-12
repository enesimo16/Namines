import { beforeEach, describe, expect, it } from 'vitest';
import { useBranchStore } from './useBranchStore';

/**
 * `useBranchStore` — dal karşılaştırma ve birleştirme çakışması oturumu
 * (FE-001 / B-15).
 *
 * **Neden önemli:** Çakışma çözümü, kullanıcının HANGİ sürümü tutacağına
 * karar verdiği yer. Seçimin yanlış kayda yazılması ya da oturum
 * sıfırlanmadan yeni bir birleştirmeye girilmesi, önceki kararların sessizce
 * taşınması demek — yani kullanıcının seçmediği bir şemayı onaylaması.
 */
describe('useBranchStore', () => {
  // Gerçek `MergeConflictItem` şekli: selectedChoice ZORUNLU ve varsayılan
  // olarak bir tarafı işaret ediyor — "henüz seçilmedi" diye bir durum yok.
  const conflicts = [
    {
      id: 'c1', type: 'table_name' as const, tableName: 'users',
      sourceValue: 'users', targetValue: 'members', selectedChoice: 'target' as const,
    },
    {
      id: 'c2', type: 'column_added' as const, tableName: 'orders', columnName: 'email',
      sourceValue: 'email', targetValue: null, selectedChoice: 'target' as const,
    },
  ];

  beforeEach(() => {
    useBranchStore.getState().resetMergeSession();
    useBranchStore.setState({ compareBranchName: null, isDiffMode: false, isConflictModalOpen: false });
  });

  it('karşılaştırma dalı ayarlanır ve temizlenir', () => {
    useBranchStore.getState().setCompareBranchName('feature/x');
    expect(useBranchStore.getState().compareBranchName).toBe('feature/x');

    useBranchStore.getState().setCompareBranchName(null);
    expect(useBranchStore.getState().compareBranchName).toBeNull();
  });

  it('diff modu ve çakışma penceresi bağımsız açılıp kapanır', () => {
    useBranchStore.getState().setIsDiffMode(true);
    useBranchStore.getState().setIsConflictModalOpen(true);

    expect(useBranchStore.getState().isDiffMode).toBe(true);
    expect(useBranchStore.getState().isConflictModalOpen).toBe(true);

    useBranchStore.getState().setIsDiffMode(false);

    // Biri kapanınca diğeri KAPANMAMALI: aynı bayrağa bağlanmış olsalardı
    // diff'ten çıkmak çözülmemiş çakışma penceresini de kapatırdı.
    expect(useBranchStore.getState().isConflictModalOpen).toBe(true);
  });

  it('birleştirme oturumu kaynak, hedef ve çakışmalarla başlar', () => {
    useBranchStore.getState().startMergeSession('feature/x', 'main', conflicts);

    const state = useBranchStore.getState();
    expect(state.conflicts).toHaveLength(2);
    expect(state.conflicts.map(c => c.id)).toEqual(['c1', 'c2']);
    // Hangi daldan hangi dala birleştirildiği de kaydedilmeli: çakışma
    // penceresi kullanıcıya "hangisini tutuyorum" derken bunu gösteriyor.
    expect(state.mergeSourceBranch).toBe('feature/x');
    expect(state.mergeTargetBranch).toBe('main');
  });

  it('çakışma seçimi YALNIZCA hedef kaydı değiştirir', () => {
    useBranchStore.getState().startMergeSession('feature/x', 'main', conflicts);

    useBranchStore.getState().updateConflictChoice('c2', 'source');

    const state = useBranchStore.getState();
    expect(state.conflicts.find(c => c.id === 'c2')!.selectedChoice).toBe('source');
    // Diğer çakışmanın seçimi kirlenmemeli.
    expect(state.conflicts.find(c => c.id === 'c1')!.selectedChoice).not.toBe('source');
  });

  it('bilinmeyen çakışma kimliği hiçbir şeyi değiştirmez', () => {
    useBranchStore.getState().startMergeSession('feature/x', 'main', conflicts);
    const before = JSON.stringify(useBranchStore.getState().conflicts);

    useBranchStore.getState().updateConflictChoice('yok', 'target');

    expect(JSON.stringify(useBranchStore.getState().conflicts)).toBe(before);
  });

  /**
   * Sıfırlama ÇAKIŞMALARI DA temizlemeli. Kalırsa, bir sonraki birleştirme
   * önceki oturumun kararlarıyla başlar ve kullanıcı hiç görmediği seçimleri
   * onaylar.
   */
  it('resetMergeSession çakışmaları temizler', () => {
    useBranchStore.getState().startMergeSession('feature/x', 'main', conflicts);
    useBranchStore.getState().updateConflictChoice('c1', 'target');

    useBranchStore.getState().resetMergeSession();

    expect(useBranchStore.getState().conflicts).toHaveLength(0);
  });
});
