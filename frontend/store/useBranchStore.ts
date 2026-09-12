import { create } from 'zustand';
import type { SchemaColumn, SchemaTable } from '../types/schema';

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
    });

interface BranchState {
  compareBranchName: string | null;
  isDiffMode: boolean;
  isConflictModalOpen: boolean;
  mergeSourceBranch: string | null; // birleşen dal (örn: feature/siparisler)
  mergeTargetBranch: string | null; // üzerine birleşilen dal (örn: main)
  conflicts: MergeConflictItem[];

  setCompareBranchName: (name: string | null) => void;
  setIsDiffMode: (active: boolean) => void;
  setIsConflictModalOpen: (open: boolean) => void;
  startMergeSession: (source: string, target: string, conflicts: MergeConflictItem[]) => void;
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

  setCompareBranchName: (name) => set({ compareBranchName: name }),
  setIsDiffMode: (active) => set({ isDiffMode: active }),
  setIsConflictModalOpen: (open) => set({ isConflictModalOpen: open }),
  
  startMergeSession: (source, target, conflicts) => set({
    mergeSourceBranch: source,
    mergeTargetBranch: target,
    conflicts,
    isConflictModalOpen: true
  }),

  updateConflictChoice: (id, choice) => set((state) => ({
    conflicts: state.conflicts.map(c => c.id === id ? { ...c, selectedChoice: choice } : c)
  })),

  resetMergeSession: () => set({
    mergeSourceBranch: null,
    mergeTargetBranch: null,
    conflicts: [],
    isConflictModalOpen: false
  })
}));
