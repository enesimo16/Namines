import { create } from 'zustand';
import type { DatabaseSchema, SchemaColumn, SchemaTable } from '../types/schema';

/**
 * Birlestirme cakismasi.
 *
 * **AYRIMLI BIRLESIM (discriminated union), ve bu bir suslemeden fazlasi:**
 * `sourceValue`/`targetValue` cakismanin TURUNE gore farkli sey tasiyor --
 * tablo eklendi/silindi ise tablonun kendisi, ad degistiyse metin, kolon
 * degisikliginde kolon. Onceden ikisi de `any` idi ve `ConflictResolverModal`
 * her dalda "burada tablo gelir" varsayimiyla okuyordu; varsayim yanlis olsa
 * derleyici susardı ve birlestirme YANLIS sema uretirdi.
 *
 * Simdi `item.type` kontrolu degerin tipini de daraltiyor: modal bir dalda
 * yanlis sekli okursa DERLENMEZ.
 */
interface MergeConflictBase {
  /** Eslesen key (or. tableId ya da tableId-columnId-durum). */
  id: string;
  tableName: string;
  columnName?: string;
  /** Kullanicinin secimi; varsayilan olarak bir tarafi isaret eder. */
  selectedChoice: 'source' | 'target';
  /**
   * Sunucunun kategorisi (github/F4). STRING olarak taşınıyor — enum'u
   * istemcide kopyalamak bu projede bir kez kırıldı.
   */
  kind?: string;
  /** Elle seçimle çözülemez; birleştirme uygulanamaz. */
  blocking?: boolean;
  /** Sunucunun insan diliyle açıklaması; kullanıcıya olduğu gibi gösterilir. */
  explanation?: string;
  /**
   * Çakışmanın ait olduğu tablo/kolon kimlikleri.
   *
   * **Neden ayrı alanlar:** uygulama yolu bunları `id`'yi `-` ile PARÇALAYARAK
   * okuyordu (`tableId-colId-durum`). Kimlikler GUID olduğunda — ki sunucudan
   * gelenler öyle — parçalama GUID'in ilk bölümünü tablo kimliği sanıyor ve
   * yanlış tabloyu buluyor. Kimliği taşımak, onu tahmin etmekten güvenli.
   */
  tableId?: string;
  columnId?: string;
}

export type MergeConflictItem =
  | (MergeConflictBase & {
      type: 'table_added' | 'table_deleted';
      sourceValue: SchemaTable | null;
      targetValue: SchemaTable | null;
    })
  | (MergeConflictBase & {
      type: 'table_name';
      sourceValue: string;
      targetValue: string;
    })
  | (MergeConflictBase & {
      type: 'column_added' | 'column_deleted' | 'column_modified';
      sourceValue: SchemaColumn | null;
      targetValue: SchemaColumn | null;
    })
  /**
   * Arayüzün zengin gösterimi olmayan çakışma türü (ör. ad çakışması, ya da
   * sunucunun sonradan eklediği bir kategori).
   *
   * **Neden var: tanınmayan bir çakışma DÜŞÜRÜLMEMELİ.** Listeden sessizce
   * çıkan bir çakışma, kullanıcının onu çözdüğünü sanarak bozuk bir şema
   * üretmesi demek. Bilinmeyen tür genel bir satır olarak, sunucunun
   * açıklamasıyla gösterilir.
   */
  | (MergeConflictBase & {
      type: 'unknown';
      sourceValue: unknown;
      targetValue: unknown;
    });

interface BranchState {
  compareBranchName: string | null;
  isDiffMode: boolean;
  isConflictModalOpen: boolean;
  mergeSourceBranch: string | null; // birleşen dal (örn: feature/siparisler)
  mergeTargetBranch: string | null; // üzerine birleşilen dal (örn: main)
  conflicts: MergeConflictItem[];
  /** Sorulmadan uygulanan degisikliklerin ozeti; kullaniciya gosterilir. */
  autoMerged: string[];
  /**
   * Sunucunun OTOMATIK kararlari uygulanmis semasi.
   *
   * **Uygulamanin tabani bu olmak zorunda.** Onceden birlestirme, aktif dalin
   * semasindan baslayip yalnizca cakisma secimlerini uyguluyordu; uc yollu
   * akista farklarin cogu hic cakisma olarak gosterilmiyor, dolayisiyla
   * "otomatik birlesti" denen her sey sessizce KAYBOLUYORDU.
   */
  serverMerged: DatabaseSchema | null;

  setCompareBranchName: (name: string | null) => void;
  setIsDiffMode: (active: boolean) => void;
  setIsConflictModalOpen: (open: boolean) => void;
  startMergeSession: (source: string, target: string, conflicts: MergeConflictItem[], autoMerged?: string[], serverMerged?: DatabaseSchema | null) => void;
  updateConflictChoice: (id: string, choice: 'source' | 'target') => void;
  resetMergeSession: () => void;
}

export const useBranchStore = create<BranchState>((set) => ({
  compareBranchName: null,
  isDiffMode: false,
  isConflictModalOpen: false,
  mergeSourceBranch: null,
  mergeTargetBranch: null,
  conflicts: [],
  autoMerged: [],
  serverMerged: null,

  setCompareBranchName: (name) => set({ compareBranchName: name }),
  setIsDiffMode: (active) => set({ isDiffMode: active }),
  setIsConflictModalOpen: (open) => set({ isConflictModalOpen: open }),
  
  startMergeSession: (source, target, conflicts, autoMerged = [], serverMerged = null) => set({
    mergeSourceBranch: source,
    mergeTargetBranch: target,
    conflicts,
    autoMerged,
    serverMerged,
    isConflictModalOpen: true
  }),

  updateConflictChoice: (id, choice) => set((state) => ({
    conflicts: state.conflicts.map(c => c.id === id ? { ...c, selectedChoice: choice } : c)
  })),

  resetMergeSession: () => set({
    mergeSourceBranch: null,
    mergeTargetBranch: null,
    conflicts: [],
    autoMerged: [],
    serverMerged: null,
    isConflictModalOpen: false
  })
}));
