'use client';

import { useState, useEffect, type ReactNode } from 'react';
import Link from 'next/link';
import { ArrowRight, CheckCircle2, Sparkles, Terminal, Database, KeyRound, Link2 } from 'lucide-react';

interface SchemaPreset {
  title: string;
  badge: string;
  tableName: string;
  prompt: string;
  columns: {
    name: string;
    type: string;
    isPk?: boolean;
    isFk?: boolean;
    constraint?: string;
  }[];
  relationText: string;
}

const PRESETS: SchemaPreset[] = [
  {
    title: 'Stripe Billing & Subscriptions',
    badge: 'Multi-Tenant',
    tableName: 'subscriptions',
    prompt: 'Add multi-tenant Stripe subscriptions with webhook audit logs and plan tiers',
    columns: [
      { name: 'id', type: 'UUID', isPk: true, constraint: 'DEFAULT uuid_generate_v7()' },
      { name: 'org_id', type: 'UUID', isFk: true, constraint: 'FK > organizations(id) ON DELETE RESTRICT' },
      { name: 'stripe_customer_id', type: 'VARCHAR(64)', constraint: 'UNIQUE NOT NULL' },
      { name: 'plan_tier', type: 'VARCHAR(32)', constraint: "CHECK (plan_tier IN ('starter', 'pro', 'team'))" },
      { name: 'current_period_end', type: 'TIMESTAMPTZ', constraint: 'NOT NULL' },
      { name: 'status', type: 'VARCHAR(20)', constraint: "DEFAULT 'active'" },
    ],
    relationText: 'organizations (1) ── (N) subscriptions [Zero-Drift FK Enforced]',
  },
  {
    title: 'AI Vector Memory Ledger',
    badge: 'Vector AST',
    tableName: 'agent_memories',
    prompt: 'Design an AI agent memory ledger with vector embeddings and tenant isolation',
    columns: [
      { name: 'id', type: 'UUID', isPk: true, constraint: 'DEFAULT uuid_generate_v7()' },
      { name: 'session_id', type: 'UUID', isFk: true, constraint: 'FK > chat_sessions(id) ON DELETE CASCADE' },
      { name: 'embedding', type: 'VECTOR(1536)', constraint: 'HNSW INDEX (vector_cosine_ops)' },
      { name: 'token_count', type: 'INTEGER', constraint: 'CHECK (token_count > 0)' },
      { name: 'metadata', type: 'JSONB', constraint: "DEFAULT '{}'::jsonb" },
      { name: 'created_at', type: 'TIMESTAMPTZ', constraint: 'DEFAULT clock_timestamp()' },
    ],
    relationText: 'chat_sessions (1) ── (N) agent_memories [pgvector Cosine HNSW]',
  },
  {
    title: 'Order Checkout & Inventory',
    badge: 'ACID Strict',
    tableName: 'order_reservations',
    prompt: 'Create e-commerce order checkout with inventory reservations and split shipments',
    columns: [
      { name: 'id', type: 'UUID', isPk: true, constraint: 'DEFAULT uuid_generate_v7()' },
      { name: 'order_id', type: 'UUID', isFk: true, constraint: 'FK > orders(id) ON DELETE RESTRICT' },
      { name: 'sku_code', type: 'VARCHAR(48)', constraint: 'REFERENCES inventory(sku)' },
      { name: 'quantity', type: 'INTEGER', constraint: 'CHECK (quantity > 0)' },
      { name: 'expires_at', type: 'TIMESTAMPTZ', constraint: 'NOT NULL' },
      { name: 'lock_state', type: 'VARCHAR(24)', constraint: "DEFAULT 'reserved'" },
    ],
    relationText: 'orders (1) ── (N) order_reservations [Row-Level Lock Safe]',
  },
];

interface DatabasePromptVisualizerProps {
  children?: ReactNode;
}

export default function DatabasePromptVisualizer({ children }: DatabasePromptVisualizerProps) {
  const [activeIdx, setActiveIdx] = useState(0);
  const [displayedText, setDisplayedText] = useState('');
  const [isTyping, setIsTyping] = useState(true);

  const activePreset = PRESETS[activeIdx] ?? PRESETS[0]!;

  // Smooth typewriter effect for active prompt
  useEffect(() => {
    const fullText = activePreset.prompt;
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
      }
    }, 28);

    return () => clearInterval(typingInterval);
  }, [activeIdx]);

  return (
    <div className="relative w-full py-2">
      {/* Background Ambient Radial Glow */}
      <div
        aria-hidden="true"
        className="pointer-events-none absolute -top-10 right-1/4 w-[500px] h-[500px] rounded-full opacity-15 blur-[120px]"
        style={{ background: 'var(--namines-teal-glow)' }}
      />

      {/* Main Responsive Grid: Headline (Left) & Studio Preview (Right) */}
      <div className="grid grid-cols-1 lg:grid-cols-12 gap-8 lg:gap-8 xl:gap-12 items-center w-full relative z-10">
        
        {/* Left Column — Provided via children (Headline, text, CTAs) */}
        {children}

        {/* Right Column — Clean, Focused Studio Interactive Card */}
        <div className="lg:col-span-6 xl:col-span-7 w-full relative z-20">
          <div className="w-full rounded-[var(--radius-modal)] border border-surface-500/70 bg-surface-900/95 shadow-2xl backdrop-blur-xl overflow-hidden transition-all duration-300 hover:border-surface-400">
            
            {/* 1. Terminal / Studio Window Header */}
            <div className="flex items-center justify-between px-4 py-3 bg-surface-850/90 border-b border-surface-600/70">
              <div className="flex items-center gap-2">
                <div className="flex items-center gap-1.5">
                  <div className="w-2.5 h-2.5 rounded-full bg-surface-600" />
                  <div className="w-2.5 h-2.5 rounded-full bg-surface-600" />
                  <div className="w-2.5 h-2.5 rounded-full bg-surface-600" />
                </div>
                <div className="h-4 w-px bg-surface-600 mx-1.5" />
                <div className="flex items-center gap-1.5 text-xs font-mono text-content-secondary">
                  <Terminal className="w-3.5 h-3.5 text-accent-text" />
                  <span className="font-semibold text-content-primary">studio</span>
                  <span className="text-content-subtle">/</span>
                  <span className="text-content-muted">{activePreset.tableName}.sql</span>
                </div>
              </div>

              <div className="flex items-center gap-2 font-mono text-[11px]">
                <span className="inline-flex items-center gap-1.5 px-2 py-0.5 rounded-[var(--radius-control)] bg-accent/15 text-accent-text font-bold">
                  <span className="w-1.5 h-1.5 rounded-full bg-accent animate-pulse" />
                  PostgreSQL 17 AST
                </span>
              </div>
            </div>

            {/* 2. Live Typewriter Prompt Area */}
            <div className="p-4 sm:p-5 border-b border-surface-600/60 bg-surface-900/60">
              <div className="flex items-start gap-2.5 mb-2.5">
                <div className="w-6 h-6 rounded-[var(--radius-control)] bg-surface-800 border border-surface-600/60 flex items-center justify-center shrink-0 mt-0.5">
                  <Sparkles className="w-3.5 h-3.5 text-accent-text" />
                </div>
                <div className="flex-1 min-h-[46px] font-mono text-xs sm:text-sm text-content-primary leading-relaxed">
                  <span>{displayedText}</span>
                  <span className="inline-block w-2 h-4 ml-0.5 bg-accent animate-pulse align-middle" />
                </div>
              </div>

              {/* Preset Selector Chips */}
              <div className="flex flex-wrap items-center gap-1.5 pt-2">
                <span className="text-[11px] font-mono text-content-subtle mr-1">Try preset:</span>
                {PRESETS.map((preset, idx) => (
                  <button
                    key={preset.tableName}
                    type="button"
                    onClick={() => setActiveIdx(idx)}
                    className={`tap-44 px-2.5 py-1 rounded-[var(--radius-control)] text-xs font-mono transition-all cursor-pointer ${
                      activeIdx === idx
                        ? 'bg-accent text-accent-on font-bold shadow-sm'
                        : 'bg-surface-800 text-content-secondary hover:text-content-primary hover:bg-surface-700 border border-surface-600/50'
                    }`}
                  >
                    {idx === 0 ? 'Stripe Billing' : idx === 1 ? 'AI Vector Memory' : 'Order Checkout'}
                  </button>
                ))}
              </div>
            </div>

            {/* 3. Generated Schema Table Preview (Clean, Shopify/Stripe Quality) */}
            <div className="p-4 sm:p-5 bg-surface-850/40">
              <div className="rounded-[var(--radius-card)] border border-surface-500/70 bg-surface-900/90 overflow-hidden shadow-inner">
                
                {/* Table Header Bar */}
                <div className="flex items-center justify-between px-3.5 py-2.5 bg-surface-800/80 border-b border-surface-600/60 font-mono text-xs">
                  <div className="flex items-center gap-2">
                    <Database className="w-3.5 h-3.5 text-accent-text" />
                    <span className="font-bold text-content-primary">{activePreset.tableName}</span>
                  </div>
                  <div className="flex items-center gap-2 text-[11px]">
                    <span className="text-content-subtle">{activePreset.columns.length} columns</span>
                    <span className="px-1.5 py-0.5 rounded-[var(--radius-control)] bg-surface-700 text-content-secondary font-bold">
                      {activePreset.badge}
                    </span>
                  </div>
                </div>

                {/* Columns List */}
                <div className="divide-y divide-surface-700/50 font-mono text-xs">
                  {activePreset.columns.map((col) => (
                    <div
                      key={col.name}
                      className="px-3.5 py-2 flex items-center justify-between gap-2 hover:bg-surface-800/40 transition-colors"
                    >
                      <div className="flex items-center gap-2 min-w-0">
                        {col.isPk ? (
                          <span className="px-1 py-0.2 rounded-[var(--radius-control)] bg-accent/20 text-accent-text text-[10px] font-bold shrink-0">
                            PK
                          </span>
                        ) : col.isFk ? (
                          <span className="px-1 py-0.2 rounded-[var(--radius-control)] bg-surface-700 text-content-secondary text-[10px] font-bold shrink-0">
                            FK
                          </span>
                        ) : (
                          <span className="w-4" />
                        )}
                        <span className="font-semibold text-content-primary truncate">{col.name}</span>
                      </div>

                      <div className="flex items-center gap-3 text-[11px] shrink-0">
                        <span className="text-accent-text font-medium">{col.type}</span>
                        {col.constraint && (
                          <span className="text-content-muted hidden sm:inline max-w-[200px] truncate text-[10px]">
                            {col.constraint}
                          </span>
                        )}
                      </div>
                    </div>
                  ))}
                </div>

                {/* Relational Link Footer */}
                <div className="px-3.5 py-2 bg-surface-800/60 border-t border-surface-600/50 flex items-center gap-2 font-mono text-[11px] text-content-secondary">
                  <Link2 className="w-3.5 h-3.5 text-accent-text shrink-0" />
                  <span className="truncate">{activePreset.relationText}</span>
                </div>
              </div>
            </div>

            {/* 4. Real-Time Verification Status & Studio CTA */}
            <div className="px-4 sm:px-5 py-3.5 bg-surface-900 border-t border-surface-600/60 flex flex-col sm:flex-row items-start sm:items-center justify-between gap-3 font-mono text-xs">
              <div className="flex items-center gap-2">
                <CheckCircle2 className="w-4 h-4 text-success-text shrink-0" />
                <span className="text-content-secondary">
                  <span className="font-bold text-content-primary">Verified Zero-Drift</span>
                  <span className="text-content-muted"> &middot; 24 rules passed</span>
                </span>
              </div>

              <Link
                href="/new"
                className="inline-flex items-center gap-1.5 px-3.5 py-1.5 rounded-[var(--radius-control)] bg-accent hover:bg-accent-hover text-accent-on font-bold transition-all shadow-sm cursor-pointer hover:scale-[1.02] active:scale-95"
              >
                <span>Try in Studio</span>
                <ArrowRight className="w-3.5 h-3.5" />
              </Link>
            </div>

          </div>
        </div>

      </div>
    </div>
  );
}
