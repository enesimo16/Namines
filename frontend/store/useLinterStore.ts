import { create } from 'zustand';
import { LintResult } from '../types/linter';

interface LinterState {
  result: LintResult | null;
  isLinting: boolean;
  setResult: (result: LintResult | null) => void;
  setIsLinting: (isLinting: boolean) => void;
}

export const useLinterStore = create<LinterState>((set) => ({
  result: null,
  isLinting: false,
  setResult: (result) => set({ result }),
  setIsLinting: (isLinting) => set({ isLinting })
}));
