'use client';

import { useState } from 'react';
import Link from 'next/link';
import { ArrowRight, Mail } from 'lucide-react';
import RotatingHeadline from './RotatingHeadline';
import DatabasePromptVisualizer from './DatabasePromptVisualizer';
import TalkToSalesModal from './TalkToSalesModal';

export default function Hero() {
  const [isSalesOpen, setIsSalesOpen] = useState(false);
  return (
    <div className="relative w-full overflow-hidden bg-surface-900">
      {/* Ambient su yeşili / turquoise glow in top-left */}
      <div
        aria-hidden="true"
        className="pointer-events-none absolute -top-24 -left-24 w-[600px] h-[600px] rounded-full opacity-20 blur-[130px]"
        style={{ backgroundColor: 'var(--namines-teal-glow)' }}
      />

      {/* Full-width Edge-to-Edge Ocean Wave (Namines signature fluid wave across bottom of Hero) */}
      <div
        aria-hidden="true"
        className="pointer-events-none absolute bottom-0 left-0 w-full h-48 sm:h-64 overflow-hidden z-0"
      >
        <div className="absolute left-1/2 w-[4000px] h-[4000px] -ml-[2000px] rounded-[43%] wave1" />
        <div className="absolute left-1/2 w-[4000px] h-[4000px] -ml-[2000px] rounded-[43%] wave2" />
        <div className="absolute left-1/2 w-[4000px] h-[4000px] -ml-[2000px] rounded-[43%] wave3" />
        {/* Soft upward fade to melt seamlessly into surface */}
        <div
          className="absolute inset-0 pointer-events-none"
          style={{
            background: 'linear-gradient(to top, var(--surface-900) 0%, transparent 65%)',
          }}
        />
      </div>

      {/* Background Architectural Grid Pattern (Render.com style) */}
      <div
        aria-hidden="true"
        className="pointer-events-none absolute inset-y-0 right-0 w-full lg:w-3/5 opacity-20 overflow-hidden"
        style={{
          backgroundImage:
            'linear-gradient(to right, var(--color-line-solid) 1px, transparent 1px), linear-gradient(to bottom, var(--color-line-solid) 1px, transparent 1px)',
          backgroundSize: '56px 56px',
          maskImage: 'radial-gradient(ellipse at 70% 50%, black 40%, transparent 80%)',
          WebkitMaskImage: 'radial-gradient(ellipse at 70% 50%, black 40%, transparent 80%)',
        }}
      />

      <section className="relative z-10 w-full max-w-[var(--w-app)] mx-auto px-4 sm:px-6 lg:px-8 pt-16 sm:pt-24 pb-20 sm:pb-32">
        <DatabasePromptVisualizer>
          {/* Left Column — Typographic Headline & CTAs (Balanced for 13"-16" laptops & ultrawide) */}
          <div className="lg:col-span-6 xl:col-span-5 flex flex-col items-start text-left z-10">
            <h1 className="text-3xl sm:text-4xl lg:text-[2.35rem] xl:text-[3.25rem] 2xl:text-[3.6rem] font-bold tracking-tight text-content-primary leading-[1.14] mb-5 sm:mb-6">
              <span>Your fastest path to a schema for</span>
              <span
                className="block mt-1 sm:mt-2 min-h-[1.25em] text-2xl sm:text-3xl lg:text-[1.95rem] xl:text-[2.75rem] 2xl:text-[3.1rem] font-extrabold whitespace-nowrap overflow-hidden text-ellipsis"
                style={{ color: 'var(--namines-teal-vibrant)' }}
              >
                <RotatingHeadline />
              </span>
            </h1>

            <p className="text-sm sm:text-base text-content-secondary max-w-[46ch] mb-6 sm:mb-8 leading-relaxed">
              Describe what you&apos;re building in plain English. Get back a schema a
              deterministic rule engine has already checked — real DDL, six SQL dialects,
              no black box in between.
            </p>

            {/* Render.com-style Primary & Secondary Buttons */}
            <div className="flex flex-col sm:flex-row items-center gap-3.5 w-full sm:w-auto">
              <Link
                href="/new"
                className="w-full sm:w-auto inline-flex items-center justify-center gap-2 font-bold py-3 px-6 rounded-[var(--radius-control)] bg-accent hover:bg-accent-hover text-accent-on transition-all text-xs uppercase tracking-wider shadow-lg shadow-accent/15 hover:scale-[1.02] active:scale-95"
              >
                <span>Start for free</span>
                <ArrowRight className="w-3.5 h-3.5" />
              </Link>

              <button
                type="button"
                onClick={() => setIsSalesOpen(true)}
                className="w-full sm:w-auto inline-flex items-center justify-center gap-2 font-semibold py-3 px-6 rounded-[var(--radius-control)] bg-surface-800/80 hover:bg-surface-700 border border-surface-400 text-content-primary transition-all text-xs uppercase tracking-wider cursor-pointer active:scale-95 shadow-sm"
              >
                <Mail className="w-3.5 h-3.5 text-accent-text" />
                <span>Talk to sales</span>
              </button>
            </div>
          </div>
        </DatabasePromptVisualizer>
      </section>

      {/* Shopify-Style Interactive Animated Enterprise Sales Modal */}
      <TalkToSalesModal isOpen={isSalesOpen} onClose={() => setIsSalesOpen(false)} />
    </div>
  );
}
