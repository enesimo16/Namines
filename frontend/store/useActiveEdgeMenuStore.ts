import { create } from 'zustand';

interface ActiveEdgeMenuState {
  activeEdgeId: string | null;
  setActiveEdgeId: (id: string | null) => void;
  close: () => void;
}

export const useActiveEdgeMenuStore = create<ActiveEdgeMenuState>((set) => ({
  activeEdgeId: null,
  setActiveEdgeId: (id) => set({ activeEdgeId: id }),
  close: () => set({ activeEdgeId: null }),
}));
