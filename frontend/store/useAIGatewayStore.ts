import { create } from 'zustand';

interface AIGatewayState {
  isOpen: boolean;
  featureName: string;
  openGateway: (featureName: string) => void;
  closeGateway: () => void;
}

export const useAIGatewayStore = create<AIGatewayState>((set) => ({
  isOpen: false,
  featureName: '',
  openGateway: (featureName) => set({ isOpen: true, featureName }),
  closeGateway: () => set({ isOpen: false, featureName: '' }),
}));
