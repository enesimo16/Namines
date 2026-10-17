import { create } from 'zustand';
import { persist } from 'zustand/middleware';

export interface TourStep {
  target?: string;
  title: string;
  description: string;
  badge?: string;
}

interface TourState {
  isTourActive: boolean;
  activeGuideId: string;
  activeStepIndex: number;
  hasCompletedTour: boolean;
  startGuide: (guideId: string, initialIndex?: number) => void;
  startTour: () => void;
  nextStep: () => void;
  prevStep: () => void;
  goToStep: (index: number) => void;
  endTour: () => void;
  resetTourStatus: () => void;
}

export const useTourStore = create<TourState>()(
  persist(
    (set) => ({
      isTourActive: false,
      activeGuideId: 'canvas',
      activeStepIndex: 0,
      hasCompletedTour: false,
      startGuide: (guideId: string, initialIndex = 0) =>
        set({ isTourActive: true, activeGuideId: guideId, activeStepIndex: initialIndex }),
      startTour: () => set({ isTourActive: true, activeGuideId: 'canvas', activeStepIndex: 0 }),
      nextStep: () => set((state) => ({ activeStepIndex: state.activeStepIndex + 1 })),
      prevStep: () => set((state) => ({ activeStepIndex: Math.max(0, state.activeStepIndex - 1) })),
      goToStep: (index: number) => set({ activeStepIndex: Math.max(0, index) }),
      endTour: () => set({ isTourActive: false, activeStepIndex: 0, hasCompletedTour: true }),
      resetTourStatus: () => set({ hasCompletedTour: false }),
    }),
    {
      name: 'namines-tour-onboarding',
      partialize: (state) => ({ hasCompletedTour: state.hasCompletedTour }),
    }
  )
);
