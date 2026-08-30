'use client';

import React, { useEffect, useState } from 'react';
import { useTourStore, TourStep } from '../../store/useTourStore';
import { X, ChevronLeft, ChevronRight } from 'lucide-react';

const steps: TourStep[] = [
  {
    target: '#regional-prompt-panel, #schema-info-panel',
    title: 'AI Schema Generation & Revision',
    description: 'Convert natural language descriptions into complete database schemas, or revise selected tables using AI assistance.'
  },
  {
    target: '#react-flow-canvas',
    title: 'Interactive Database Canvas',
    description: 'Drag and drop tables, visually manage relationships, and inspect the database structure in real-time.'
  },
  {
    target: '#canvas-toolbar',
    title: 'Helper Tools & Export Hub',
    description: 'Export your schema as PNG, SQL, or a full-stack project, toggle edit mode, or view automated DBA analysis recommendations.'
  },
  {
    target: '#approve-diagram-btn',
    title: 'Compile & Seed Data',
    description: 'Approve your diagram design to compile, and generate realistic test seed datasets matching your schema structure.'
  }
];

export default function TourOverlay() {
  const { isTourActive, activeStepIndex, hasCompletedTour, startTour, nextStep, prevStep, endTour } = useTourStore();
  const [coords, setCoords] = useState<{ x: number; y: number; width: number; height: number } | null>(null);

  // Auto-start tour if not completed
  useEffect(() => {
    if (!hasCompletedTour) {
      const timer = setTimeout(() => {
        startTour();
      }, 1500);
      return () => clearTimeout(timer);
    }
  }, [hasCompletedTour, startTour]);

  // Handle ESC key to cancel the tour
  useEffect(() => {
    const handleKeyDown = (e: KeyboardEvent) => {
      if (e.key === 'Escape') {
        endTour();
      }
    };
    window.addEventListener('keydown', handleKeyDown);
    return () => window.removeEventListener('keydown', handleKeyDown);
  }, [endTour]);

  // Track target element coordinates
  useEffect(() => {
    if (!isTourActive) {
      setCoords(null);
      return;
    }

    const updateCoords = () => {
      const step = steps[activeStepIndex];
      if (!step) return;

      const element = document.querySelector(step.target);
      if (element) {
        const rect = element.getBoundingClientRect();
        const padding = 8;
        setCoords({
          x: rect.left - padding,
          y: rect.top - padding,
          width: rect.width + padding * 2,
          height: rect.height + padding * 2
        });
      } else {
        setCoords(null);
      }
    };

    updateCoords();
    const timer = setTimeout(updateCoords, 100);

    window.addEventListener('resize', updateCoords);
    window.addEventListener('scroll', updateCoords);

    return () => {
      clearTimeout(timer);
      window.removeEventListener('resize', updateCoords);
      window.removeEventListener('scroll', updateCoords);
    };
  }, [isTourActive, activeStepIndex]);

  if (!isTourActive) return null;

  const currentStep = steps[activeStepIndex];
  if (!currentStep) return null;

  // Calculate dynamic card positioning
  let cardStyle: React.CSSProperties = {
    position: 'fixed',
    zIndex: 9999
  };

  if (coords) {
    if (currentStep.target === '#react-flow-canvas') {
      // Center of the screen
      cardStyle = {
        ...cardStyle,
        left: '50%',
        top: '50%',
        transform: 'translate(-50%, -50%)'
      };
    } else {
      const screenWidth = typeof window !== 'undefined' ? window.innerWidth : 1000;
      const screenHeight = typeof window !== 'undefined' ? window.innerHeight : 800;
      const isRightSide = coords.x + coords.width / 2 > screenWidth / 2;
      const isBottomSide = coords.y + coords.height / 2 > screenHeight / 2;

      let top = isBottomSide ? coords.y - 180 : coords.y;
      let left = coords.x + coords.width + 20;

      // If it overflows on the right, place it on the left
      if (typeof window !== 'undefined' && left + 320 > window.innerWidth) {
        left = coords.x - 320 - 20;
      }

      // If it overflows on both left and right, place it below or above
      if (typeof window !== 'undefined' && (left < 0 || left + 320 > window.innerWidth)) {
        left = Math.max(20, coords.x + coords.width / 2 - 160);
        top = isBottomSide ? coords.y - 180 : coords.y + coords.height + 20;
      }

      cardStyle = {
        ...cardStyle,
        left: `${left}px`,
        top: `${top}px`
      };
    }
  } else {
    // Fallback to center
    cardStyle = {
      ...cardStyle,
      left: '50%',
      top: '50%',
      transform: 'translate(-50%, -50%)'
    };
  }

  const isLastStep = activeStepIndex === steps.length - 1;

  return (
    <>
      {/* Spotlight Backdrop Mask */}
      <svg className="fixed inset-0 w-full h-full pointer-events-none z-[9997]">
        <defs>
          <mask id="tour-spotlight-mask">
            <rect width="100%" height="100%" fill="white" />
            {coords && (
              <rect
                x={coords.x}
                y={coords.y}
                width={coords.width}
                height={coords.height}
                rx={12}
                fill="black"
              />
            )}
          </mask>
        </defs>
        <rect
          width="100%"
          height="100%"
          fill="color-mix(in srgb, var(--color-scrim) 75%, transparent)"
          mask="url(#tour-spotlight-mask)"
          className="pointer-events-auto"
        />
      </svg>

      {/* Tour Dialog Card */}
      <div
        style={cardStyle}
        className="w-[320px] p-5 rounded-[var(--radius-modal)] bg-surface-800 border border-content-primary/12 shadow-[0_20px_60px_color-mix(in srgb, var(--color-scrim) 60%, transparent)] flex flex-col gap-4 text-sans animate-in zoom-in-95 duration-200"
      >
        {/* Header */}
        <div className="flex items-center justify-between">
          <span className="text-[10px] font-bold text-content-muted tracking-widest uppercase font-mono">
            Onboarding Tour ({activeStepIndex + 1} / {steps.length})
          </span>
          <button
            onClick={endTour}
            className="text-content-subtle hover:text-content-primary transition-colors p-0.5 rounded-[var(--radius-control)] hover:bg-white/[0.06] cursor-pointer"
            title="Skip Tour"
          >
            <X className="w-4 h-4" />
          </button>
        </div>

        {/* Content */}
        <div className="space-y-1">
          <h4 className="text-sm font-bold text-content-primary">{currentStep.title}</h4>
          <p className="text-xs text-content-muted leading-relaxed">
            {currentStep.description}
          </p>
        </div>

        {/* Footer Navigation */}
        <div className="flex items-center justify-between mt-1 pt-3 border-t border-content-primary/8">
          <button
            onClick={endTour}
            className="text-[11px] font-semibold text-content-subtle hover:text-content-secondary transition-colors cursor-pointer"
          >
            Skip Tour
          </button>

          <div className="flex items-center gap-2">
            {activeStepIndex > 0 && (
              <button
                onClick={prevStep}
                className="p-1.5 rounded-[var(--radius-control)] bg-surface-700 hover:bg-surface-600 text-content-secondary transition-colors flex items-center justify-center cursor-pointer"
                title="Previous"
              >
                <ChevronLeft className="w-3.5 h-3.5" />
              </button>
            )}
            <button
              onClick={isLastStep ? endTour : nextStep}
              className="px-3.5 py-1.5 rounded-[var(--radius-control)] bg-content-primary hover:bg-content-primary-hover text-surface-900 text-[11px] font-semibold transition-all flex items-center gap-1 cursor-pointer"
            >
              <span>{isLastStep ? 'Complete' : 'Next'}</span>
              {!isLastStep && <ChevronRight className="w-3.5 h-3.5" />}
            </button>
          </div>
        </div>
      </div>
    </>
  );
}
