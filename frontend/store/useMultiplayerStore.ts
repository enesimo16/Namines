import { create } from 'zustand';

export interface CursorPosition {
  userName: string;
  x: number;
  y: number;
  color: string;
}

interface MultiplayerState {
  roomId: string | null;
  userName: string | null;
  isConnected: boolean;
  isOffline: boolean;
  cursors: Record<string, CursorPosition>;
  setRoomInfo: (roomId: string | null, userName: string | null) => void;
  setIsConnected: (isConnected: boolean) => void;
  setIsOffline: (isOffline: boolean) => void;
  updateCursor: (connectionId: string, position: CursorPosition) => void;
  removeCursor: (connectionId: string) => void;
  clearCursors: () => void;
}

export const useMultiplayerStore = create<MultiplayerState>((set) => ({
  roomId: null,
  userName: null,
  isConnected: false,
  isOffline: false,
  cursors: {},
  setRoomInfo: (roomId, userName) => set({ roomId, userName }),
  setIsConnected: (isConnected) => set({ isConnected }),
  setIsOffline: (isOffline) => set({ isOffline }),
  updateCursor: (connectionId, position) => set((state) => ({
    cursors: { ...state.cursors, [connectionId]: position }
  })),
  removeCursor: (connectionId) => set((state) => {
    const next = { ...state.cursors };
    delete next[connectionId];
    return { cursors: next };
  }),
  clearCursors: () => set({ cursors: {} }),
}));
