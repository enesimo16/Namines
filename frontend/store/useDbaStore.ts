import { create } from 'zustand';
import { DbaIssue } from '../hooks/useAIDba';

interface DbaState {
  issues: DbaIssue[];
  score: number;
  assessment: string;
  isAnalyzing: boolean;
  isPanelOpen: boolean;
  selectedTableFilter: string | null;
  setDbaResults: (results: { issues: DbaIssue[]; score: number; assessment: string }) => void;
  setIsAnalyzing: (isAnalyzing: boolean) => void;
  setIsPanelOpen: (isOpen: boolean) => void;
  setSelectedTableFilter: (tableName: string | null) => void;
}

export const useDbaStore = create<DbaState>((set) => ({
  issues: [],
  score: 100,
  assessment: 'Şema sağlık kontrolü bekleniyor...',
  isAnalyzing: false,
  isPanelOpen: false,
  selectedTableFilter: null,
  setDbaResults: (results) => set({ ...results }),
  setIsAnalyzing: (isAnalyzing) => set({ isAnalyzing }),
  setIsPanelOpen: (isPanelOpen) => set({ isPanelOpen }),
  setSelectedTableFilter: (selectedTableFilter) => set({ selectedTableFilter })
}));
