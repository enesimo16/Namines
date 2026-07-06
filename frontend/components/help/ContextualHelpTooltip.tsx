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
            className="inline-flex items-center justify-center p-1 rounded-full text-indigo-400 hover:text-indigo-200 hover:bg-indigo-500/10 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-amber-400 cursor-help transition-all ml-1.5 shrink-0"
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
            className="z-[9999] max-w-[280px] bg-[#09111F]/95 border border-[#FFD700]/25 text-white/90 p-3.5 rounded-xl shadow-2xl backdrop-blur-xl animate-in fade-in slide-in-from-bottom-2 duration-150 text-xs leading-relaxed font-sans"
          >
            <div className="flex flex-col gap-1">
              <span className="font-extrabold text-[#FFD700] tracking-wide uppercase text-[10px]">
                {content.title}
              </span>
              <p className="font-medium text-zinc-300">
                {content.description}
              </p>
            </div>
            <Tooltip.Arrow className="fill-[#09111F] stroke-[#FFD700]/25 stroke-1" />
          </Tooltip.Content>
        </Tooltip.Portal>
      </Tooltip.Root>
    </Tooltip.Provider>
  );
}
