import { create } from 'zustand';
import { persist } from 'zustand/middleware';

export interface TourStep {
  target: string;
  title: string;
  description: string;
}

interface TourState {
  isTourActive: boolean;
  activeStepIndex: number;
  hasCompletedTour: boolean;
  startTour: () => void;
  nextStep: () => void;
  prevStep: () => void;
  endTour: () => void;
  resetTourStatus: () => void;
}

export const useTourStore = create<TourState>()(
  persist(
    (set) => ({
      isTourActive: false,
      activeStepIndex: 0,
      hasCompletedTour: false,
      startTour: () => set({ isTourActive: true, activeStepIndex: 0 }),
      nextStep: () => set((state) => ({ activeStepIndex: state.activeStepIndex + 1 })),
      prevStep: () => set((state) => ({ activeStepIndex: Math.max(0, state.activeStepIndex - 1) })),
      endTour: () => set({ isTourActive: false, activeStepIndex: 0, hasCompletedTour: true }),
      resetTourStatus: () => set({ hasCompletedTour: false }),
    }),
    {
      name: 'namines-tour-onboarding',
    }
  )
);
