'use client';

import React from 'react';
import * as Tooltip from '@radix-ui/react-tooltip';
import { HelpCircle } from 'lucide-react';
import { HelpItem } from '../../lib/helpContent';

interface ContextualHelpTooltipProps {
  content: HelpItem;
}

export default function ContextualHelpTooltip({ content }: ContextualHelpTooltipProps) {
  return (
    <Tooltip.Provider delayDuration={200}>
      <Tooltip.Root>
        <Tooltip.Trigger asChild>
          <button
            type="button"
            className="inline-flex items-center justify-center p-1 rounded-full text-content-subtle hover:text-content-secondary hover:bg-white/[0.06] focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-focus-ring cursor-help transition-all ml-1.5 shrink-0"
            aria-label={`More information about ${content.title}`}
          >
            <HelpCircle className="w-3.5 h-3.5" />
          </button>
        </Tooltip.Trigger>
        <Tooltip.Portal>
          <Tooltip.Content
            side="top"
            align="center"
            sideOffset={6}
            className="z-[9999] max-w-[280px] bg-surface-800 border border-content-primary/12 text-content-primary p-3.5 rounded-[var(--radius-card)] shadow-[0_20px_60px_color-mix(in srgb, var(--color-scrim) 60%, transparent)] animate-in fade-in slide-in-from-bottom-2 duration-150 text-xs leading-relaxed font-sans"
          >
            <div className="flex flex-col gap-1">
              <span className="font-bold text-content-primary tracking-wide uppercase text-[10px]">
                {content.title}
              </span>
              <p className="font-medium text-content-muted">
                {content.description}
              </p>
            </div>
            <Tooltip.Arrow className="fill-surface-800 stroke-content-primary/12 stroke-1" />
          </Tooltip.Content>
        </Tooltip.Portal>
      </Tooltip.Root>
    </Tooltip.Provider>
  );
}
