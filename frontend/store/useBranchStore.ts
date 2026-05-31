import { create } from 'zustand';

interface MergeConflictItem {
  id: string; // Eşleşen key (örn: tableId veya tableId-columnId)
  type: 'table_name' | 'table_added' | 'table_deleted' | 'column_added' | 'column_deleted' | 'column_modified';
  tableName: string;
  columnName?: string;
  sourceValue: any; // Mevcut (aktif) dalın değeri
  targetValue: any; // Hedef (birleştirilen) dalın değeri
  selectedChoice: 'source' | 'target'; // Kullanıcının seçimi
}

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
