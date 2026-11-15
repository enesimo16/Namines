'use client';

import { useEffect, useState } from 'react';
import Link from 'next/link';
import { ArrowDown, ArrowRight, CheckCircle2 } from 'lucide-react';
import Footer from '../../components/layout/Footer';
import TalkToSalesModal from '../../components/landing/TalkToSalesModal';
import {
  IsometricIrPipeline,
  IsometricPixelEmblem,
  type PixelEmblemKind,
} from '../../components/landing/IsometricVisuals';

interface SolutionBlock {
  id: PixelEmblemKind;
  eyebrow: string;
  title: string;
  description: string;
  bullets: string[];
  ctaLabel: string;
  ctaHref: string;
}

const SOLUTION_BLOCKS: SolutionBlock[] = [
  {
    id: 'ci-cd',
    eyebrow: 'Use Case · CI/CD Governance',
    title: 'Zero-Drift CI/CD',
    description:
      'Catch destructive drops, table locks, and broken application queries before a migration merges. Every pull request runs deterministic impact analysis and ephemeral Docker DDL verification.',
    bullets: [
      '3-way schema merge across feature branches',
      'Automated Safe / Warning / Breaking risk scoring',
      'Static code scan highlighting affected API & ORM files',
    ],
    ctaLabel: 'Open PR Change Review',
    ctaHref: '/review',
  },
  {
    id: 'modernize',
    eyebrow: 'Use Case · Legacy Systems',
    title: 'Legacy DB Modernization',
    description:
      'Introspect existing PostgreSQL, MySQL, SQL Server, Oracle, MariaDB, or SQLite databases into canonical IR without persisting credentials, then generate typed SDKs, docs, and clean migrations.',
    bullets: [
      'Zero-persistence memory-only metadata discovery',
      'SSRF-hardened host & private-network guardrails',
      'Instant Mermaid ER diagrams & Data Dictionary',
    ],
    ctaLabel: 'Launch Schema Architect',
    ctaHref: '/new',
  },
  {
    id: 'ai-layer',
    eyebrow: 'Use Case · Coding Agents',
    title: 'AI Agent Data Layer',
    description:
      'Give Claude Code, Cursor, and VS Code agents deterministic database guardrails through the Universal MCP Server instead of letting LLMs guess raw ALTER TABLE statements.',
    bullets: [
      'Zero-install stdio MCP via npx -y @namines/mcp',
      'Explainable impact reports grounded in deterministic diffs',
      'Scoped Gateway REST API keys with full audit trail',
    ],
    ctaLabel: 'Read MCP Documentation',
    ctaHref: '/docs#cli-mcp',
  },
  {
    id: 'multi-engine',
    eyebrow: 'Use Case · Cross-Engine Portability',
    title: 'Multi-Engine Migration',
    description:
      'Model your schema once in canonical ir.json and compile verified DDL for six SQL dialects alongside 19 ORM, SDK, and admin-panel targets.',
    bullets: [
      'Golden-file verified DDL for 6 SQL engines',
      'Restrictive NO ACTION fallback when engines differ',
      'Prisma, Drizzle, EF Core, TypeScript SDK, and Next.js eject',
    ],
    ctaLabel: 'Open Multi-Engine Compiler',
    ctaHref: '/compile',
  },
  {
    id: 'teams',
    eyebrow: 'Teams · Platform Engineering',
    title: 'Backend & Platform Teams',
    description:
      'Standardize how product squads propose, review, and ship schema changes across shared repositories with branch isolation and real-time collaborative canvas.',
    bullets: [
      'Server-authoritative schema branches',
      'Shared Schema Hub templates and forking',
      'Role-based access across Desk, Vault, Ground, and Flow',
    ],
    ctaLabel: 'Explore Schema Hub',
    ctaHref: '/hub',
  },
  {
    id: 'governance',
    eyebrow: 'Trust · Compliance',
    title: 'Team Governance & Audit',
    description:
      'Keep an immutable audit trail of who approved each schema change, which API key queried production, and how backups are encrypted and restored.',
    bullets: [
      'Opt-in Safe-risk auto-approval with ChangeRequestAuditLog',
      'Encrypted connection & snapshot storage in Vault',
      'Full security whitepaper & threat model',
    ],
    ctaLabel: 'Read Security Whitepaper',
    ctaHref: '/security',
  },
];

export default function SolutionsPage() {
  const [isSalesOpen, setIsSalesOpen] = useState(false);
  const [focusedId, setFocusedId] = useState<string | null>(null);

  useEffect(() => {
    const hash = window.location.hash.replace(/^#/, '');
    if (hash && SOLUTION_BLOCKS.some((b) => b.id === hash)) {
      setFocusedId(hash);
    }
    // Hash'li URL ile (/solutions#ci-cd) gelindiğinde tarayıcının sayfa ortasına atlamasını engelle; en üstten başlat.
    window.scrollTo({ top: 0, behavior: 'instant' as ScrollBehavior });
    const raf = requestAnimationFrame(() => {
      window.scrollTo({ top: 0, behavior: 'instant' as ScrollBehavior });
    });
    return () => cancelAnimationFrame(raf);
  }, []);

  const focusedBlock = focusedId
    ? SOLUTION_BLOCKS.find((b) => b.id === focusedId) ?? null
    : null;

  return (
    <div className="min-h-screen bg-surface-900 text-content-primary flex flex-col">
      <main className="flex-1">
        {/* Hero with 3D Isometric Stack */}
        <section className="border-b border-surface-500/60">
          <div className="w-full max-w-[var(--w-app)] mx-auto px-4 sm:px-6 lg:px-8 py-14 lg:py-20 grid grid-cols-1 lg:grid-cols-[1.2fr_0.8fr] gap-10 items-center">
            <div className="max-w-3xl space-y-5">
              {focusedBlock && (
                <button
                  type="button"
                  onClick={() => {
                    document
                      .getElementById(focusedBlock.id)
                      ?.scrollIntoView({ behavior: 'smooth', block: 'start' });
                  }}
                  className="inline-flex items-center gap-2 px-3 py-1.5 rounded-full bg-accent/12 border border-accent/40 text-[12px] font-medium text-accent-text hover:bg-accent/20 transition-colors cursor-pointer"
                >
                  <span>{focusedBlock.title}</span>
                  <ArrowDown className="w-3.5 h-3.5" />
                </button>
              )}
              <h1 className="text-3xl sm:text-4xl lg:text-5xl font-bold tracking-tight leading-[1.12]">
                Safe database lifecycles for solo builders and platform teams.
              </h1>
              <p className="text-base sm:text-lg text-content-secondary leading-relaxed">
                Whether you are modernizing a legacy SQL Server instance, wiring MCP into Cursor, or enforcing zero-drift PR gates across fifty microservices, Namines keeps every schema change deterministic.
              </p>
              <div className="flex flex-wrap items-center gap-3 pt-2">
                <Link
                  href="/new"
                  className="tap-44 inline-flex items-center gap-2 px-5 py-2.5 rounded-[var(--radius-control)] bg-accent hover:bg-accent-hover text-accent-on text-[14px] font-semibold transition-colors"
                >
                  <span>Try Schema Architect</span>
                  <ArrowRight className="w-4 h-4" />
                </Link>
                <button
                  type="button"
                  onClick={() => setIsSalesOpen(true)}
                  className="tap-44 inline-flex items-center px-5 py-2.5 rounded-[var(--radius-control)] border border-surface-500 bg-surface-800/60 hover:bg-surface-800 text-[14px] font-semibold text-content-primary transition-colors cursor-pointer"
                >
                  Talk to Sales
                </button>
              </div>
            </div>

            <div className="hidden lg:block">
              <IsometricIrPipeline />
            </div>
          </div>
        </section>

        {/* Anchored Solution Grid with Pixel-Art Isometric Emblems */}
        <section className="w-full max-w-[var(--w-app)] mx-auto px-4 sm:px-6 lg:px-8 py-14 lg:py-18">
          <div className="grid grid-cols-1 md:grid-cols-2 gap-6 lg:gap-8">
            {SOLUTION_BLOCKS.map((block) => (
              <article
                key={block.id}
                id={block.id}
                className={`scroll-mt-24 rounded-[var(--radius-card)] bg-surface-800/35 border p-6 sm:p-8 flex flex-col justify-between gap-6 transition-colors ${
                  focusedId === block.id
                    ? 'border-accent ring-2 ring-accent/25'
                    : 'border-surface-500/80 hover:border-accent/60'
                }`}
              >
                <div className="space-y-4">
                  <div className="flex items-center justify-between gap-3">
                    <span className="text-[11px] font-mono uppercase tracking-wider text-accent-text">
                      {block.eyebrow}
                    </span>
                    <IsometricPixelEmblem kind={block.id} />
                  </div>
                  <h2 className="text-xl sm:text-2xl font-bold text-content-primary">
                    {block.title}
                  </h2>
                  <p className="text-[14px] text-content-secondary leading-relaxed">
                    {block.description}
                  </p>
                  <ul className="space-y-2 pt-1">
                    {block.bullets.map((b) => (
                      <li
                        key={b}
                        className="flex items-start gap-2.5 text-[13px] text-content-secondary"
                      >
                        <CheckCircle2 className="w-4 h-4 text-accent-text shrink-0 mt-0.5" />
                        <span>{b}</span>
                      </li>
                    ))}
                  </ul>
                </div>

                <div className="pt-4 border-t border-surface-500/60">
                  <Link
                    href={block.ctaHref}
                    className="inline-flex items-center gap-2 text-[13px] font-semibold text-accent-text hover:underline"
                  >
                    <span>{block.ctaLabel}</span>
                    <ArrowRight className="w-3.5 h-3.5" />
                  </Link>
                </div>
              </article>
            ))}
          </div>
        </section>
      </main>

      <Footer />

      <TalkToSalesModal
        isOpen={isSalesOpen}
        onClose={() => setIsSalesOpen(false)}
      />
    </div>
  );
}
