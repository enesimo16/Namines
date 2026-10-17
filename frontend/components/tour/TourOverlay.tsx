'use client';

import React, { useEffect, useState, useRef, useCallback } from 'react';
import { useTourStore } from '../../store/useTourStore';
import { useDbaStore } from '../../store/useDbaStore';
import { GUIDES } from './guideData';
import { useLocale } from '../../lib/i18n';
import { X, ChevronLeft, ChevronRight } from 'lucide-react';

interface Coords {
  x: number;
  y: number;
  width: number;
  height: number;
}

type Placement = 'bottom' | 'top' | 'right' | 'left' | 'center';

export default function TourOverlay() {
  const {
    isTourActive,
    activeGuideId,
    activeStepIndex,
    nextStep,
    prevStep,
    goToStep,
    endTour,
  } = useTourStore();

  const locale = useLocale();
  const [coords, setCoords] = useState<Coords | null>(null);
  const [placement, setPlacement] = useState<Placement>('center');
  const [arrowOffset, setArrowOffset] = useState<number>(20);
  const cardRef = useRef<HTMLDivElement>(null);

  const guide = GUIDES[activeGuideId] || GUIDES.new || GUIDES.canvas;
  const steps = guide.steps;
  const currentStep = steps[activeStepIndex] || steps[0];

  // Set data-tour-active on document.body while tour is running
  useEffect(() => {
    if (isTourActive) {
      document.body.setAttribute('data-tour-active', 'true');
      if (activeGuideId === 'dba') {
        useDbaStore.getState().setIsPanelOpen(true);
      } else if (activeGuideId === 'migration') {
        window.dispatchEvent(new CustomEvent('namines:open-migration'));
      }
    } else {
      document.body.removeAttribute('data-tour-active');
    }
    return () => {
      document.body.removeAttribute('data-tour-active');
    };
  }, [isTourActive, activeGuideId]);

  // Handle ESC and Arrow keys for tour navigation
  useEffect(() => {
    if (!isTourActive) return;
    const handleKeyDown = (e: KeyboardEvent) => {
      if (e.key === 'Escape') {
        endTour();
      } else if (e.key === 'ArrowRight') {
        if (activeStepIndex < steps.length - 1) nextStep();
        else endTour();
      } else if (e.key === 'ArrowLeft') {
        if (activeStepIndex > 0) prevStep();
      }
    };
    window.addEventListener('keydown', handleKeyDown);
    return () => window.removeEventListener('keydown', handleKeyDown);
  }, [isTourActive, activeStepIndex, steps.length, endTour, nextStep, prevStep]);

  // Measure target DOM element coordinates (Pure measurement, no scrollIntoView!)
  const updateTargetPosition = useCallback(() => {
    if (!isTourActive || !currentStep) {
      setCoords(null);
      return;
    }

    const targetSelector = currentStep.target;
    if (!targetSelector) {
      setCoords(null);
      setPlacement('center');
      return;
    }

    const selectors = targetSelector.split(',').map(s => s.trim());
    let element: HTMLElement | null = null;
    for (const sel of selectors) {
      element = document.querySelector<HTMLElement>(sel);
      if (element) break;
    }

    if (!element) {
      setCoords(null);
      setPlacement('center');
      return;
    }

    const rect = element.getBoundingClientRect();
    const pad = 6;
    const calculatedCoords: Coords = {
      x: Math.max(0, rect.left - pad),
      y: Math.max(0, rect.top - pad),
      width: rect.width + pad * 2,
      height: rect.height + pad * 2,
    };
    setCoords(calculatedCoords);

    const winW = window.innerWidth;
    const winH = window.innerHeight;
    const cardW = Math.min(380, winW - 32);
    const cardH = 280;

    const spaceBelow = winH - (calculatedCoords.y + calculatedCoords.height);
    const spaceAbove = calculatedCoords.y;
    const spaceRight = winW - (calculatedCoords.x + calculatedCoords.width);
    const spaceLeft = calculatedCoords.x;

    let chosenPlacement: Placement = 'bottom';
    if (spaceBelow >= cardH + 20) {
      chosenPlacement = 'bottom';
    } else if (spaceAbove >= cardH + 20) {
      chosenPlacement = 'top';
    } else if (spaceRight >= cardW + 20) {
      chosenPlacement = 'right';
    } else if (spaceLeft >= cardW + 20) {
      chosenPlacement = 'left';
    } else {
      chosenPlacement = spaceBelow > spaceAbove ? 'bottom' : 'top';
    }
    setPlacement(chosenPlacement);

    if (chosenPlacement === 'bottom' || chosenPlacement === 'top') {
      const cardLeft = Math.max(16, Math.min(calculatedCoords.x + calculatedCoords.width / 2 - cardW / 2, winW - cardW - 16));
      const targetCenterX = calculatedCoords.x + calculatedCoords.width / 2;
      setArrowOffset(Math.max(24, Math.min(targetCenterX - cardLeft, cardW - 24)));
    } else {
      const cardTop = Math.max(16, Math.min(calculatedCoords.y + calculatedCoords.height / 2 - cardH / 2, winH - cardH - 16));
      const targetCenterY = calculatedCoords.y + calculatedCoords.height / 2;
      setArrowOffset(Math.max(24, Math.min(targetCenterY - cardTop, cardH - 24)));
    }
  }, [isTourActive, currentStep]);

  // Execute interactive actions (autoAction / interactiveButton)
  const executeAction = useCallback((actionStr: string) => {
    if (!actionStr) return;

    if (actionStr === 'open-toolkit') {
      window.dispatchEvent(new CustomEvent('namines:open-toolkit'));
    } else if (actionStr === 'open-export-flyout') {
      window.dispatchEvent(new CustomEvent('namines:open-export-flyout'));
    } else if (actionStr === 'open-migration') {
      window.dispatchEvent(new CustomEvent('namines:open-migration'));
    } else if (actionStr === 'open-dba') {
      useDbaStore.getState().setIsPanelOpen(true);
    } else if (actionStr === 'close-toolkit') {
      window.dispatchEvent(new CustomEvent('namines:close-toolkit'));
    } else if (actionStr === 'toggle-toolkit') {
      const fab = document.querySelector<HTMLElement>('#canvas-toolkit-fab');
      if (fab) fab.click();
      else window.dispatchEvent(new CustomEvent('namines:open-toolkit'));
    } else if (actionStr.startsWith('switch-tab:')) {
      const tabSel = actionStr.replace('switch-tab:', '');
      document.querySelector<HTMLElement>(tabSel)?.click();
    } else if (actionStr.startsWith('click:')) {
      const sel = actionStr.replace('click:', '');
      document.querySelector<HTMLElement>(sel)?.click();
    }

    requestAnimationFrame(updateTargetPosition);
    setTimeout(updateTargetPosition, 80);
  }, [updateTargetPosition]);

  // ON STEP CHANGE ONLY: scrollIntoView ONCE, then position!
  useEffect(() => {
    if (!isTourActive || !currentStep) return;

    if (currentStep.tabSelector) {
      document.querySelector<HTMLElement>(currentStep.tabSelector)?.click();
    }

    if (currentStep.autoAction) {
      executeAction(currentStep.autoAction);
    }

    if (currentStep.target) {
      const selectors = currentStep.target.split(',').map(s => s.trim());
      for (const sel of selectors) {
        const el = document.querySelector<HTMLElement>(sel);
        if (el) {
          el.scrollIntoView({ behavior: 'smooth', block: 'center', inline: 'center' });
          break;
        }
      }
    }

    updateTargetPosition();
    const t1 = setTimeout(updateTargetPosition, 120);
    const t2 = setTimeout(updateTargetPosition, 300);

    return () => {
      clearTimeout(t1);
      clearTimeout(t2);
      if (currentStep.cleanupAction === 'close-toolkit') {
        window.dispatchEvent(new CustomEvent('namines:close-toolkit'));
      }
    };
  }, [isTourActive, activeStepIndex, currentStep, executeAction, updateTargetPosition]);

  // RAF throttled scroll & resize listener
  useEffect(() => {
    if (!isTourActive) return;
    let rafId: number;
    const onScrollOrResize = () => {
      cancelAnimationFrame(rafId);
      rafId = requestAnimationFrame(updateTargetPosition);
    };

    window.addEventListener('resize', onScrollOrResize);
    window.addEventListener('scroll', onScrollOrResize, { passive: true });
    return () => {
      cancelAnimationFrame(rafId);
      window.removeEventListener('resize', onScrollOrResize);
      window.removeEventListener('scroll', onScrollOrResize);
    };
  }, [isTourActive, updateTargetPosition]);

  const handleNext = () => {
    if (currentStep.cleanupAction === 'close-toolkit') {
      window.dispatchEvent(new CustomEvent('namines:close-toolkit'));
    }
    if (activeStepIndex < steps.length - 1) {
      nextStep();
    } else {
      endTour();
    }
  };

  const handlePrev = () => {
    if (currentStep.cleanupAction === 'close-toolkit') {
      window.dispatchEvent(new CustomEvent('namines:close-toolkit'));
    }
    prevStep();
  };

  const handleEnd = () => {
    window.dispatchEvent(new CustomEvent('namines:close-toolkit'));
    endTour();
  };

  if (!isTourActive || !currentStep) return null;

  const isLastStep = activeStepIndex === steps.length - 1;
  const langKey = locale === 'tr' ? 'tr' : 'en';

  const badgeText = currentStep.badge ? currentStep.badge[langKey] : null;
  const categoryText = guide.category[langKey];
  const titleText = currentStep.title[langKey];
  const descriptionText = currentStep.description[langKey];
  const directiveText = currentStep.directive ? currentStep.directive[langKey] : null;
  const interactiveBtn = currentStep.interactiveButton;

  // Compute Card CSS Position
  let cardStyle: React.CSSProperties = {
    position: 'fixed',
    zIndex: 350,
  };

  if (coords && placement !== 'center') {
    const winW = typeof window !== 'undefined' ? window.innerWidth : 1200;
    const winH = typeof window !== 'undefined' ? window.innerHeight : 800;
    const cardW = Math.min(380, winW - 32);
    const cardH = 280;

    let left = 20;
    let top = 20;

    if (placement === 'bottom') {
      top = coords.y + coords.height + 14;
      left = Math.max(16, Math.min(coords.x + coords.width / 2 - cardW / 2, winW - cardW - 16));
    } else if (placement === 'top') {
      top = Math.max(16, coords.y - cardH - 14);
      left = Math.max(16, Math.min(coords.x + coords.width / 2 - cardW / 2, winW - cardW - 16));
    } else if (placement === 'right') {
      left = coords.x + coords.width + 14;
      top = Math.max(16, Math.min(coords.y + coords.height / 2 - cardH / 2, winH - cardH - 16));
    } else if (placement === 'left') {
      left = Math.max(16, coords.x - cardW - 14);
      top = Math.max(16, Math.min(coords.y + coords.height / 2 - cardH / 2, winH - cardH - 16));
    }

    left = Math.max(16, Math.min(left, winW - cardW - 16));
    top = Math.max(16, Math.min(top, winH - cardH - 16));

    cardStyle = {
      ...cardStyle,
      left: `${left}px`,
      top: `${top}px`,
      width: `${cardW}px`,
    };
  } else {
    cardStyle = {
      ...cardStyle,
      left: '50%',
      top: '50%',
      transform: 'translate(-50%, -50%)',
      width: 'min(380px, calc(100vw - 32px))',
    };
  }

  return (
    <>
      {/* ── High-Performance Hardware-Accelerated SVG Mask Scrim ──────────── */}
      {coords ? (
        <>
          <svg
            className="fixed inset-0 w-full h-full pointer-events-none z-[340]"
            aria-hidden="true"
          >
            <defs>
              <mask id="tour-spotlight-mask">
                <rect width="100%" height="100%" fill="white" />
                <rect
                  x={coords.x}
                  y={coords.y}
                  width={coords.width}
                  height={coords.height}
                  rx="8"
                  fill="black"
                />
              </mask>
            </defs>
            <rect
              width="100%"
              height="100%"
              fill="var(--color-scrim)"
              fillOpacity="0.74"
              mask="url(#tour-spotlight-mask)"
            />
          </svg>

          {/* Transparent Backdrop Click-Interceptor */}
          <div
            className="fixed inset-0 z-[339] cursor-pointer"
            onClick={handleEnd}
            aria-label="Click outside to exit guide"
          />

          {/* Crisp Glowing Border around Target Element */}
          <div
            style={{
              position: 'fixed',
              top: coords.y,
              left: coords.x,
              width: coords.width,
              height: coords.height,
              borderRadius: '8px',
              border: '2px solid var(--accent-text)',
              boxShadow: '0 0 20px color-mix(in srgb, var(--accent) 45%, transparent)',
              pointerEvents: 'none',
              zIndex: 341,
              transition: 'all 0.15s ease-out',
            }}
          />
        </>
      ) : (
        /* Full-screen Dark Scrim for non-targeted overview steps */
        <div
          className="fixed inset-0 bg-scrim/75 backdrop-blur-[2px] z-[340] cursor-pointer"
          onClick={handleEnd}
        />
      )}

      {/* ── Shopify Polaris Clean Popover Card (No Clutter Icons) ─────────── */}
      <div
        ref={cardRef}
        style={cardStyle}
        role="dialog"
        aria-modal="true"
        aria-label={titleText}
        className="relative p-5 rounded-[var(--radius-modal)] bg-surface-800/98 backdrop-blur-2xl border border-line-strong shadow-2xl flex flex-col gap-3 text-sans animate-in zoom-in-95 duration-150"
      >
        {/* Directional Pointer Arrow (Beak) pointing at target */}
        {coords && placement !== 'center' && (
          <div
            className="pointer-events-none absolute z-10"
            style={
              placement === 'bottom'
                ? { top: '-8px', left: `${arrowOffset - 8}px` }
                : placement === 'top'
                ? { bottom: '-8px', left: `${arrowOffset - 8}px` }
                : placement === 'right'
                ? { left: '-8px', top: `${arrowOffset - 8}px` }
                : { right: '-8px', top: `${arrowOffset - 8}px` }
            }
          >
            {placement === 'bottom' && (
              <div className="w-0 h-0 border-x-[8px] border-x-transparent border-b-[8px] border-b-surface-800" />
            )}
            {placement === 'top' && (
              <div className="w-0 h-0 border-x-[8px] border-x-transparent border-t-[8px] border-t-surface-800" />
            )}
            {placement === 'right' && (
              <div className="w-0 h-0 border-y-[8px] border-y-transparent border-r-[8px] border-r-surface-800" />
            )}
            {placement === 'left' && (
              <div className="w-0 h-0 border-y-[8px] border-y-transparent border-l-[8px] border-l-surface-800" />
            )}
          </div>
        )}

        {/* Card Header: Step Index & Category Tag (Clean, No Clutter Icons) */}
        <div className="flex items-center justify-between gap-2 pb-2 border-b border-line">
          <div className="flex items-center gap-2 min-w-0">
            <span className="inline-flex items-center px-2 py-0.5 rounded-full text-[10px] font-mono font-bold bg-accent-subtle/80 text-accent-text border border-accent/25 tracking-wide uppercase shrink-0">
              {badgeText || categoryText}
            </span>
            <span className="text-content-muted text-[10px] font-mono">•</span>
            <span className="text-[11px] font-mono font-bold text-content-secondary whitespace-nowrap">
              {locale === 'tr'
                ? `${activeStepIndex + 1} / ${steps.length}`
                : `${activeStepIndex + 1} / ${steps.length}`}
            </span>
          </div>

          <button
            onClick={handleEnd}
            className="tap-44 text-content-muted hover:text-content-primary transition-colors p-1 rounded-full hover:bg-surface-700 cursor-pointer shrink-0"
            title={locale === 'tr' ? 'Rehberi Kapat' : 'Close Guide'}
            aria-label="Close"
          >
            <X className="w-4 h-4" />
          </button>
        </div>

        {/* Actionable Directive Box (High Contrast, Clean Text) */}
        {directiveText && (
          <div className="p-2.5 rounded-[var(--radius-control)] bg-accent-subtle/40 border border-accent/30 text-xs font-semibold text-content-primary shadow-xs leading-snug">
            {directiveText}
          </div>
        )}

        {/* Title & Educational Details */}
        <div className="space-y-1">
          <h4 className="text-sm font-bold text-content-primary leading-snug">
            {titleText}
          </h4>
          <p className="text-xs text-content-secondary leading-relaxed font-normal">
            {descriptionText}
          </p>
        </div>

        {/* Interactive Action Button (e.g. Try Now / Switch Tab / Open Toolkit) */}
        {interactiveBtn && (
          <button
            type="button"
            onClick={() => executeAction(interactiveBtn.action)}
            className="tap-44 w-full flex items-center justify-center gap-1.5 py-1.5 px-3 rounded-[var(--radius-control)] bg-accent/15 hover:bg-accent/25 border border-accent/35 text-accent-text text-xs font-bold transition-all cursor-pointer shadow-xs hover:border-accent/60"
          >
            <span>{interactiveBtn[langKey]}</span>
          </button>
        )}

        {/* Clickable Progress Dots */}
        <div className="flex items-center justify-center gap-1.5 py-0.5">
          {steps.map((_, idx) => (
            <button
              key={idx}
              type="button"
              onClick={() => goToStep(idx)}
              className={`h-1.5 rounded-full transition-all cursor-pointer ${
                idx === activeStepIndex
                  ? 'w-5 bg-accent-text shadow-xs'
                  : 'w-1.5 bg-line-strong hover:bg-content-muted'
              }`}
              title={`Step ${idx + 1}`}
              aria-label={`Step ${idx + 1}`}
            />
          ))}
        </div>

        {/* Footer Navigation Buttons */}
        <div className="flex items-center justify-between pt-2.5 border-t border-line">
          <button
            onClick={handleEnd}
            className="text-[11px] font-semibold text-content-muted hover:text-content-primary transition-colors cursor-pointer"
          >
            {locale === 'tr' ? 'Kapat' : 'Skip'}
          </button>

          <div className="flex items-center gap-2">
            {activeStepIndex > 0 && (
              <button
                type="button"
                onClick={handlePrev}
                className="tap-44 px-3 py-1.5 rounded-[var(--radius-control)] bg-surface-700 hover:bg-surface-600 text-content-secondary hover:text-content-primary text-xs font-semibold transition-colors flex items-center gap-1 cursor-pointer"
                title={locale === 'tr' ? 'Önceki adım' : 'Previous step'}
              >
                <ChevronLeft className="w-3.5 h-3.5" />
                <span>{locale === 'tr' ? 'Geri' : 'Back'}</span>
              </button>
            )}

            <button
              type="button"
              onClick={handleNext}
              className="tap-44 px-4 py-1.5 rounded-[var(--radius-control)] bg-accent hover:bg-accent-hover text-accent-on text-xs font-bold transition-all flex items-center gap-1 cursor-pointer shadow-sm hover:shadow-md"
            >
              <span>
                {isLastStep
                  ? locale === 'tr'
                    ? 'Tamamla'
                    : 'Done'
                  : locale === 'tr'
                  ? 'Sonraki Adım'
                  : 'Next Step'}
              </span>
              {!isLastStep && <ChevronRight className="w-3.5 h-3.5" />}
            </button>
          </div>
        </div>
      </div>
    </>
  );
}
