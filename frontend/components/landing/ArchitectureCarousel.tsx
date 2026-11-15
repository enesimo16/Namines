'use client';

import Link from 'next/link';
import {
  ArrowRight,
  Building2,
  Cpu,
  Landmark,
  ShoppingCart,
  Stethoscope,
  Truck,
} from 'lucide-react';

interface ArchitectureStory {
  id: string;
  domain: string;
  title: string;
  metrics: string;
  summary: string;
  href: string;
  icon: typeof ShoppingCart;
}

const ARCHITECTURE_STORIES: ArchitectureStory[] = [
  {
    id: 'ecommerce',
    domain: 'RETAIL & COMMERCE',
    title: 'E-Commerce Core',
    metrics: '14 tables · 22 FKs · Stock Locks',
    summary:
      'Orders, inventory reservations, multi-currency pricing, and idempotent payment ledgers with strict 3NF constraints.',
    href: '/demo',
    icon: ShoppingCart,
  },
  {
    id: 'saas',
    domain: 'B2B PLATFORMS',
    title: 'Multi-Tenant SaaS',
    metrics: '16 tables · RLS · UUIDv7',
    summary:
      'Organization workspaces, RBAC membership scoping, row-level isolation policies, and metered usage quotas.',
    href: '/demo',
    icon: Building2,
  },
  {
    id: 'fintech',
    domain: 'FINANCE & BILLING',
    title: 'Double-Entry Ledger',
    metrics: '11 tables · Immutable Audit',
    summary:
      'Append-only journal entries, balance check constraints, reconciliation batches, and strict delete restrictions.',
    href: '/demo',
    icon: Landmark,
  },
  {
    id: 'ai-rag',
    domain: 'AI & VECTOR SEARCH',
    title: 'RAG Knowledge Store',
    metrics: '9 tables · pgvector · HNSW',
    summary:
      'Document chunks, tenant-scoped vector embeddings, ingestion jobs, and prompt evaluation traces.',
    href: '/demo',
    icon: Cpu,
  },
  {
    id: 'healthcare',
    domain: 'HEALTHCARE',
    title: 'Clinical Scheduling',
    metrics: '18 tables · PII Masking',
    summary:
      'Patient encounters, practitioner availability, encrypted audit trails, and gateway PII field masking.',
    href: '/demo',
    icon: Stethoscope,
  },
  {
    id: 'logistics',
    domain: 'SUPPLY CHAIN',
    title: 'Fleet & Dispatch',
    metrics: '15 tables · Time-Series',
    summary:
      'Warehouse manifests, route checkpoints, carrier handoffs, and partitioned telemetry logs.',
    href: '/demo',
    icon: Truck,
  },
];

export default function ArchitectureCarousel() {
  return (
    <section className="w-full max-w-[1200px] mx-auto px-4 sm:px-6 lg:px-8 py-16 lg:py-24">
      <div className="flex flex-col sm:flex-row sm:items-end justify-between gap-6 mb-10 lg:mb-12">
        <div className="max-w-2xl">
          <span className="font-mono text-xs font-medium uppercase tracking-[0.14em] text-accent-text block mb-3">
            Blueprints Catalog
          </span>
          <h2 className="font-mono text-2xl sm:text-3xl lg:text-4xl font-semibold tracking-tight text-content-primary leading-tight">
            Production architectures ready to fork
          </h2>
          <p className="font-sans text-sm sm:text-base text-content-secondary leading-relaxed mt-3">
            Start from verified domain models instead of blank tables. Inspect relations, customize constraints, and compile to your dialect.
          </p>
        </div>

        <Link
          href="/demo"
          className="inline-flex items-center gap-1.5 px-4 py-2.5 min-h-[44px] rounded-[var(--radius-control)] border border-surface-500 bg-surface-850 hover:bg-surface-800 hover:border-surface-400 font-mono text-xs font-medium text-content-primary transition-colors shrink-0 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-focus-ring"
        >
          <span>All 65+ blueprints</span>
          <ArrowRight className="w-3.5 h-3.5" />
        </Link>
      </div>

      <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-6">
        {ARCHITECTURE_STORIES.map((story) => {
          const Icon = story.icon;
          return (
            <Link
              key={story.id}
              href={story.href}
              className="group flex flex-col justify-between p-6 rounded-[var(--radius-card)] border border-surface-500 bg-surface-850/90 hover:bg-surface-800/80 hover:border-surface-400 transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-focus-ring"
            >
              <div>
                <div className="flex items-center justify-between mb-5">
                  <span className="font-mono text-xs text-content-subtle tracking-wider">
                    {story.domain}
                  </span>
                  <div className="w-8 h-8 rounded-[var(--radius-control)] border border-surface-500 bg-surface-900 flex items-center justify-center text-accent-text">
                    <Icon className="w-4 h-4" />
                  </div>
                </div>

                <h3 className="font-mono text-lg font-semibold text-content-primary mb-1.5">
                  {story.title}
                </h3>
                <p className="font-mono text-xs text-accent-text mb-4">
                  {story.metrics}
                </p>
                <p className="font-sans text-sm text-content-muted leading-relaxed">
                  {story.summary}
                </p>
              </div>

              <div className="mt-6 pt-4 border-t border-surface-500/60 flex items-center justify-between font-mono text-xs text-content-secondary group-hover:text-content-primary transition-colors">
                <span>Inspect schema</span>
                <ArrowRight className="w-3.5 h-3.5 transition-transform group-hover:translate-x-0.5" />
              </div>
            </Link>
          );
        })}
      </div>
    </section>
  );
}
