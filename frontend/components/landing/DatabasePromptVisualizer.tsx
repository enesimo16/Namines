'use client';

import { useState, useEffect, type ReactNode } from 'react';
import Link from 'next/link';
import { ArrowRight, CheckCircle2, Database, Sparkles, Terminal } from 'lucide-react';

const PROMPT_SUGGESTIONS = [
  'Add multi-tenant billing with stripe subscriptions and webhook audit logs',
  'Design an AI agent memory ledger with vector embeddings and tenant isolation',
  'Create e-commerce order checkout with inventory reservations and split shipments',
];

interface DatabasePromptVisualizerProps {
  children?: ReactNode;
}

export default function DatabasePromptVisualizer({ children }: DatabasePromptVisualizerProps) {
  const [activePromptIndex, setActivePromptIndex] = useState(0);
  const [displayedText, setDisplayedText] = useState('');
  const [isTyping, setIsTyping] = useState(true);
  const [activeTableHighlight, setActiveTableHighlight] = useState<'subscriptions' | 'vector_memory' | 'reservations'>('subscriptions');

  // Typewriter effect cycling through prompts
  useEffect(() => {
    const fullText = PROMPT_SUGGESTIONS[activePromptIndex] ?? '';
    let charIndex = 0;
    setDisplayedText('');
    setIsTyping(true);

    const typingInterval = setInterval(() => {
      if (charIndex < fullText.length) {
        setDisplayedText(fullText.slice(0, charIndex + 1));
        charIndex++;
      } else {
        clearInterval(typingInterval);
        setIsTyping(false);

        // Map prompt to emerging table
        if (activePromptIndex === 0) setActiveTableHighlight('subscriptions');
        else if (activePromptIndex === 1) setActiveTableHighlight('vector_memory');
        else setActiveTableHighlight('reservations');

        // Wait before transitioning to next prompt
        const nextTimeout = setTimeout(() => {
          setActivePromptIndex((prev) => (prev + 1) % PROMPT_SUGGESTIONS.length);
        }, 5000);

        return () => clearTimeout(nextTimeout);
      }
    }, 34);

    return () => clearInterval(typingInterval);
  }, [activePromptIndex]);

  const handleSelectPrompt = (index: number) => {
    setActivePromptIndex(index);
  };

  return (
    <div className="relative w-full min-h-[620px] sm:min-h-[680px] lg:min-h-[720px] flex items-center overflow-visible">
      {/* Background Soft Su Yeşili Radial Glow */}
      <div
        aria-hidden="true"
        className="pointer-events-none absolute -inset-20 rounded-full opacity-20 blur-3xl"
        style={{
          background: 'radial-gradient(circle, var(--namines-teal-glow) 0%, transparent 70%)',
        }}
      />

      {/* Layer 1: Full-Width Expansive Faded Architectural Database Network */}
      {/* Spans from behind the text on the left, across center, to far right */}
      <div
        className="pointer-events-none absolute inset-0 z-0 opacity-40 transition-opacity duration-700 hover:opacity-60 overflow-hidden"
        aria-hidden="true"
      >
        {/* Dynamic Curved SVG Foreign Key Relation Lines */}
        <svg className="absolute inset-0 w-full h-full" fill="none" viewBox="0 0 1200 700" preserveAspectRatio="none">
          {/* organizations -> users */}
          <path
            d="M 170 110 C 260 110, 360 80, 440 80"
            stroke="var(--namines-teal-vibrant)"
            strokeWidth="1.5"
            strokeDasharray="4 4"
            className="opacity-50"
          />
          {/* organizations -> api_keys */}
          <path
            d="M 120 160 C 120 280, 80 440, 110 520"
            stroke="var(--namines-teal-vibrant)"
            strokeWidth="1.5"
            strokeDasharray="4 4"
            className="opacity-40"
          />
          {/* users -> orders */}
          <path
            d="M 520 160 C 520 280, 480 440, 520 520"
            stroke="var(--namines-teal-vibrant)"
            strokeWidth="1.5"
            strokeDasharray="4 4"
            className="opacity-45"
          />
          {/* orders -> payments */}
          <path
            d="M 640 560 C 740 560, 860 560, 940 540"
            stroke="var(--namines-teal-vibrant)"
            strokeWidth="1.5"
            strokeDasharray="4 4"
            className="opacity-40"
          />
          {/* users/organizations -> Emerging Table (subscriptions / vector_memory) */}
          <path
            d="M 580 90 C 700 90, 780 110, 920 110"
            stroke="var(--namines-teal-vibrant)"
            strokeWidth={isTyping ? 1.5 : 2.5}
            strokeDasharray={isTyping ? '4 4' : 'none'}
            className={isTyping ? 'opacity-30 transition-all duration-500' : 'opacity-95 transition-all duration-500'}
          />
        </svg>

        {/* Table 1: organizations (Far Left — softly behind the headline area) */}
        <div className="absolute top-8 left-0 sm:left-2 w-44 sm:w-48 rounded-[var(--radius-card)] border border-surface-500/50 bg-surface-850/80 p-3 shadow-lg backdrop-blur-sm">
          <div className="flex items-center justify-between border-b border-surface-600/60 pb-1.5 mb-1.5">
            <div className="flex items-center gap-1.5">
              <Database className="w-3 h-3 text-accent" />
              <span className="font-mono text-[11px] font-bold text-content-primary">organizations</span>
            </div>
            <span className="font-mono text-[10px] text-content-subtle">4 cols</span>
          </div>
          <div className="space-y-1 font-mono text-[10px] text-content-secondary">
            <div className="flex justify-between items-center">
              <span>id</span>
              <span className="text-accent-text font-bold px-1 py-0.2 rounded-[var(--radius-control)] bg-accent/15">
                PK
              </span>
            </div>
            <div className="flex justify-between text-content-muted">
              <span>slug</span>
              <span>VARCHAR</span>
            </div>
            <div className="flex justify-between text-content-muted">
              <span>plan</span>
              <span>VARCHAR</span>
            </div>
          </div>
        </div>

        {/* Table 2: api_keys (Lower Left — softly behind/below buttons) */}
        <div className="absolute bottom-8 left-2 sm:left-8 w-44 sm:w-48 rounded-[var(--radius-card)] border border-surface-500/50 bg-surface-850/80 p-3 shadow-lg backdrop-blur-sm hidden sm:block">
          <div className="flex items-center justify-between border-b border-surface-600/60 pb-1.5 mb-1.5">
            <div className="flex items-center gap-1.5">
              <Database className="w-3 h-3 text-accent" />
              <span className="font-mono text-[11px] font-bold text-content-primary">api_keys</span>
            </div>
            <span className="font-mono text-[10px] text-content-subtle">3 cols</span>
          </div>
          <div className="space-y-1 font-mono text-[10px] text-content-secondary">
            <div className="flex justify-between items-center">
              <span>id</span>
              <span className="text-accent-text font-bold px-1 py-0.2 rounded-[var(--radius-control)] bg-accent/15">
                PK
              </span>
            </div>
            <div className="flex justify-between text-accent-text">
              <span>org_id</span>
              <span>FK</span>
            </div>
            <div className="flex justify-between text-content-muted">
              <span>key_hash</span>
              <span>VARCHAR</span>
            </div>
          </div>
        </div>

        {/* Table 3: users (Center-Top — in the gap between headline and prompt) */}
        <div className="absolute top-4 left-[38%] sm:left-[42%] w-52 sm:w-56 rounded-[var(--radius-card)] border border-surface-500/60 bg-surface-850/85 p-3.5 shadow-xl backdrop-blur-md hidden lg:block">
          <div className="flex items-center justify-between border-b border-surface-600/60 pb-1.5 mb-2">
            <div className="flex items-center gap-2">
              <Database className="w-3.5 h-3.5 text-accent" />
              <span className="font-mono text-xs font-bold text-content-primary">users</span>
            </div>
            <span className="font-mono text-[10px] text-content-subtle">5 cols</span>
          </div>
          <div className="space-y-1.5 font-mono text-[11px] text-content-secondary">
            <div className="flex justify-between items-center">
              <span>id</span>
              <span className="text-accent-text font-bold text-[10px] px-1 py-0.5 rounded-[var(--radius-control)] bg-accent/15">
                PK
              </span>
            </div>
            <div className="flex justify-between text-accent-text items-center">
              <span>org_id</span>
              <span className="text-[10px] font-bold">FK &gt; orgs</span>
            </div>
            <div className="flex justify-between text-content-muted">
              <span>email</span>
              <span className="text-[10px]">VARCHAR</span>
            </div>
            <div className="flex justify-between text-content-muted">
              <span>role</span>
              <span className="text-[10px]">VARCHAR</span>
            </div>
          </div>
        </div>

        {/* Table 4: orders (Center-Bottom) */}
        <div className="absolute bottom-6 left-[36%] sm:left-[42%] w-52 sm:w-56 rounded-[var(--radius-card)] border border-surface-500/60 bg-surface-850/85 p-3.5 shadow-xl backdrop-blur-md hidden lg:block">
          <div className="flex items-center justify-between border-b border-surface-600/60 pb-1.5 mb-2">
            <div className="flex items-center gap-2">
              <Database className="w-3.5 h-3.5 text-accent" />
              <span className="font-mono text-xs font-bold text-content-primary">orders</span>
            </div>
            <span className="font-mono text-[10px] text-content-subtle">4 cols</span>
          </div>
          <div className="space-y-1.5 font-mono text-[11px] text-content-secondary">
            <div className="flex justify-between items-center">
              <span>id</span>
              <span className="text-accent-text font-bold text-[10px] px-1 py-0.5 rounded-[var(--radius-control)] bg-accent/15">
                PK
              </span>
            </div>
            <div className="flex justify-between text-accent-text items-center">
              <span>user_id</span>
              <span className="text-[10px] font-bold">FK &gt; users</span>
            </div>
            <div className="flex justify-between text-content-muted">
              <span>total</span>
              <span className="text-[10px]">DECIMAL</span>
            </div>
            <div className="flex justify-between text-content-muted">
              <span>status</span>
              <span className="text-[10px]">VARCHAR</span>
            </div>
          </div>
        </div>

        {/* Table 5: payments (Lower Right — below prompt) */}
        <div className="absolute bottom-8 right-0 sm:right-6 w-48 sm:w-52 rounded-[var(--radius-card)] border border-surface-500/60 bg-surface-850/85 p-3 shadow-lg backdrop-blur-md hidden sm:block">
          <div className="flex items-center justify-between border-b border-surface-600/60 pb-1.5 mb-1.5">
            <div className="flex items-center gap-1.5">
              <Database className="w-3 h-3 text-accent" />
              <span className="font-mono text-[11px] font-bold text-content-primary">payments</span>
            </div>
            <span className="font-mono text-[10px] text-content-subtle">4 cols</span>
          </div>
          <div className="space-y-1 font-mono text-[10px] text-content-secondary">
            <div className="flex justify-between items-center">
              <span>id</span>
              <span className="text-accent-text font-bold px-1 py-0.2 rounded-[var(--radius-control)] bg-accent/15">
                PK
              </span>
            </div>
            <div className="flex justify-between text-accent-text items-center">
              <span>order_id</span>
              <span>FK &gt; orders</span>
            </div>
            <div className="flex justify-between text-content-muted">
              <span>amount</span>
              <span>DECIMAL</span>
            </div>
            <div className="flex justify-between text-content-muted">
              <span>currency</span>
              <span>VARCHAR</span>
            </div>
          </div>
        </div>

        {/* Table 6: Emerging Table (Top Right — Lights Up and Materializes Dynamically) */}
        <div
          className={`absolute top-4 right-0 sm:right-4 w-60 sm:w-64 rounded-[var(--radius-card)] border p-4 shadow-2xl backdrop-blur-lg transition-all duration-700 ${
            isTyping
              ? 'border-surface-600/50 bg-surface-850/50 opacity-35 scale-95'
              : 'border-accent bg-surface-800/95 opacity-100 scale-100 shadow-accent/25'
          }`}
        >
          <div className="flex items-center justify-between border-b border-surface-600/70 pb-2 mb-2.5">
            <div className="flex items-center gap-2">
              <Sparkles className="w-4 h-4 text-accent" />
              <span className="font-mono text-xs font-bold text-content-primary">
                {activeTableHighlight}
              </span>
            </div>
            <span className="font-mono text-[10px] px-2 py-0.5 rounded-[var(--radius-control)] bg-accent/20 text-accent-text font-bold">
              EMERGED
            </span>
          </div>
          <div className="space-y-2 font-mono text-[11px] text-content-secondary">
            <div className="flex justify-between items-center">
              <span>id</span>
              <span className="text-accent-text font-bold text-[10px] px-1 py-0.5 rounded-[var(--radius-control)] bg-accent/15">
                PK
              </span>
            </div>
            <div className="flex justify-between text-accent-text items-center">
              <span>{activeTableHighlight === 'reservations' ? 'order_id' : 'org_id'}</span>
              <span className="text-[10px] font-bold">FK</span>
            </div>
            <div className="flex justify-between text-content-primary font-semibold">
              <span>
                {activeTableHighlight === 'subscriptions'
                  ? 'stripe_sub_id'
                  : activeTableHighlight === 'vector_memory'
                  ? 'embedding_1536'
                  : 'warehouse_id'}
              </span>
              <span className="text-[10px] text-accent-text">
                {activeTableHighlight === 'vector_memory' ? 'VECTOR' : 'VARCHAR'}
              </span>
            </div>
            <div className="flex justify-between text-content-muted">
              <span>{activeTableHighlight === 'subscriptions' ? 'current_period_end' : 'status'}</span>
              <span className="text-[10px]">{activeTableHighlight === 'subscriptions' ? 'TIMESTAMP' : 'VARCHAR'}</span>
            </div>
          </div>
        </div>
      </div>

      {/* Layer 2: Main Grid containing Left Column Text + Right Column Floating Prompt */}
      <div className="grid grid-cols-1 lg:grid-cols-12 gap-8 lg:gap-10 items-center w-full relative z-10">
        {/* Left Column — Typographic Headline & CTAs (Provided via children) */}
        {children}

        {/* Right Column — Floating Interactive Minimal AI Prompt Bar */}
        <div className="lg:col-span-7 w-full relative z-20 my-auto">
          <div className="rounded-[var(--radius-modal)] border border-surface-500/70 bg-surface-900/90 p-5 sm:p-6 shadow-2xl backdrop-blur-2xl transition-all max-w-xl mx-auto">
            {/* Top Bar inside Prompt */}
            <div className="flex items-center justify-between border-b border-surface-600/60 pb-3 mb-3">
              <div className="flex items-center gap-2">
                <div className="flex items-center justify-center w-6 h-6 rounded-[var(--radius-control)] bg-accent/20 text-accent">
                  <Terminal className="w-3.5 h-3.5" />
                </div>
                <span className="font-mono text-xs font-bold tracking-wider text-content-primary uppercase">
                  AI SCHEMA ENGINE
                </span>
              </div>
              <div className="flex items-center gap-2">
                <span className="flex h-2 w-2 relative">
                  <span className="animate-ping absolute inline-flex h-full w-full rounded-full bg-accent opacity-75"></span>
                  <span className="relative inline-flex rounded-full h-2 w-2 bg-accent"></span>
                </span>
                <span className="font-mono text-[11px] text-content-subtle">PostgreSQL 17 AST</span>
              </div>
            </div>

            {/* Active Live Typewriter Prompt Area */}
            <div className="min-h-[58px] sm:min-h-[64px] text-sm sm:text-base font-mono font-medium text-content-primary leading-relaxed flex items-center">
              <p>
                <span>{displayedText}</span>
                <span className="inline-block w-2.5 h-4.5 ml-1 bg-accent animate-pulse align-middle" />
              </p>
            </div>

            {/* Quick Clickable Presets */}
            <div className="flex flex-wrap items-center gap-1.5 pt-3 mt-2 border-t border-surface-600/50">
              <span className="text-[11px] font-mono text-content-subtle mr-1">Presets:</span>
              {PROMPT_SUGGESTIONS.map((_, idx) => (
                <button
                  key={idx}
                  type="button"
                  onClick={() => handleSelectPrompt(idx)}
                  className={`rounded-[var(--radius-control)] px-3 py-1 text-xs font-mono transition-all cursor-pointer ${
                    activePromptIndex === idx
                      ? 'bg-accent text-accent-on font-bold shadow-sm'
                      : 'bg-surface-700/60 text-content-muted hover:text-content-primary hover:bg-surface-700'
                  }`}
                >
                  {idx === 0 ? '+ Billing & Subscriptions' : idx === 1 ? '+ AI Vector Memory' : '+ Order Reservations'}
                </button>
              ))}
            </div>

            {/* Bottom Live Verification Status */}
            <div className="mt-3.5 pt-3 border-t border-surface-600/50 flex flex-col sm:flex-row items-start sm:items-center justify-between gap-3">
              <div className="flex items-center gap-2 text-xs font-mono">
                <CheckCircle2 className="w-4 h-4 text-success-text shrink-0" />
                <span className="text-content-secondary">
                  {isTyping ? (
                    <span className="text-content-muted">Evaluating relational constraints...</span>
                  ) : (
                    <>
                      <span className="font-bold text-content-primary">Verified zero-drift AST</span>
                      <span className="text-content-muted"> &middot; 24 rules passed</span>
                    </>
                  )}
                </span>
              </div>

              <Link
                href="/new"
                className="inline-flex items-center justify-center gap-1.5 rounded-[var(--radius-control)] bg-accent hover:bg-accent-hover text-accent-on font-bold px-4 py-1.5 text-xs transition-all shadow-md cursor-pointer"
              >
                <span>Build in Studio</span>
                <ArrowRight className="w-3.5 h-3.5" />
              </Link>
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}
