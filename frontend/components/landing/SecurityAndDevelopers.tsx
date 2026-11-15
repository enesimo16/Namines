'use client';

import Link from 'next/link';
import {
  ArrowRight,
  Cpu,
  FileCheck2,
  FileCode2,
  GitBranch,
  KeyRound,
  KeySquare,
  Lock,
  Server,
  ShieldCheck,
} from 'lucide-react';

const SECURITY_ITEMS = [
  {
    icon: ShieldCheck,
    title: 'Zero SSRF Introspection',
    description:
      'Loopback, RFC-1918 private subnets, and cloud metadata endpoints are strictly blocked before any database probe runs.',
  },
  {
    icon: Lock,
    title: 'Read-Only Gateway Defaults',
    description:
      'Generated REST gateways default to read-only operations. Mutations require scoped cryptographic API keys.',
  },
  {
    icon: FileCheck2,
    title: 'Zero Unreviewed Writes',
    description:
      'Destructive migrations (drops, type narrowing) are blocked until explicitly approved and recorded.',
  },
  {
    icon: KeyRound,
    title: 'Immutable Audit Logs',
    description:
      'Every proposed diff, container test run, and approval is logged with actor identity, timestamp, and IR snapshot.',
  },
  {
    icon: KeySquare,
    title: 'AES-256-GCM Secret Vault',
    description:
      'Connection strings and tokens are encrypted at rest with authenticated AES-256-GCM and decrypted only in memory.',
  },
  {
    icon: Server,
    title: 'Hardened HttpOnly Sessions',
    description:
      'Identity tokens stay in HttpOnly, SameSite=Strict cookies with zero client-side token exposure.',
  },
];

const DEVELOPER_SURFACES = [
  {
    icon: FileCode2,
    tag: 'CANONICAL AST',
    title: 'Schema IR & CLI',
    description:
      'Inspect, diff, and compile ir.json directly from CI pipelines without coupling your schema to a single ORM.',
    codePreview: 'npx namines compile --ir schema.ir.json --dialect postgres',
    cta: 'Open Compiler',
    href: '/compile',
  },
  {
    icon: Cpu,
    tag: 'ZERO-INSTALL',
    title: 'Universal MCP Server',
    description:
      'Give Cursor, Claude Code, and VS Code deterministic schema validation and migration risk tools via stdio.',
    codePreview: 'npx -y @namines/mcp-server --project <workspace>',
    cta: 'Explore Hub & MCP',
    href: '/hub',
  },
  {
    icon: GitBranch,
    tag: 'AUTOMATION',
    title: 'GitHub Bot & Flow',
    description:
      'Run automated 3-way schema merges, pull request impact summaries, and webhook pipelines on every commit.',
    codePreview: 'on: pull_request -> namines.review(diff, target: "main")',
    cta: 'Configure Flow',
    href: '/flow',
  },
];

export default function SecurityAndDevelopers() {
  return (
    <div className="w-full border-t border-surface-500/80 bg-surface-900/40">
      {/* Upper Layer: Security by Default */}
      <section
        id="security"
        className="w-full max-w-[1200px] mx-auto px-4 sm:px-6 lg:px-8 py-16 lg:py-24"
      >
        <div className="max-w-2xl mb-10 lg:mb-12">
          <span className="font-mono text-xs font-medium uppercase tracking-[0.14em] text-accent-text block mb-3">
            Security &amp; Compliance
          </span>
          <h2 className="font-mono text-2xl sm:text-3xl lg:text-4xl font-semibold tracking-tight text-content-primary leading-tight">
            Secure and auditable by default
          </h2>
          <p className="font-sans text-sm sm:text-base text-content-secondary leading-relaxed mt-3">
            Built for teams that govern production data. Guardrails are enforced at the architecture layer, not as optional checkboxes.
          </p>
        </div>

        <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-4 sm:gap-6">
          {SECURITY_ITEMS.map((item) => {
            const Icon = item.icon;
            return (
              <div
                key={item.title}
                className="p-6 rounded-[var(--radius-card)] border border-surface-500 bg-surface-850/90"
              >
                <div className="w-9 h-9 rounded-[var(--radius-control)] border border-surface-500 bg-surface-900 flex items-center justify-center text-accent-text mb-4">
                  <Icon className="w-4 h-4" />
                </div>
                <h3 className="font-mono text-base font-semibold text-content-primary mb-2">
                  {item.title}
                </h3>
                <p className="font-sans text-sm text-content-muted leading-relaxed">
                  {item.description}
                </p>
              </div>
            );
          })}
        </div>
      </section>

      {/* Lower Layer: Build with Namines (DeepL 3-Column Developer Platform Pattern) */}
      <section
        id="builder-grid"
        className="w-full max-w-[1200px] mx-auto px-4 sm:px-6 lg:px-8 pb-16 lg:pb-24"
      >
        <div className="pt-12 lg:pt-16 border-t border-surface-500/70">
          <div className="flex flex-col sm:flex-row sm:items-end justify-between gap-6 mb-10">
            <div className="max-w-2xl">
              <span className="font-mono text-xs font-medium uppercase tracking-[0.14em] text-accent-text block mb-3">
                Developer Platform
              </span>
              <h2 className="font-mono text-2xl sm:text-3xl lg:text-4xl font-semibold tracking-tight text-content-primary leading-tight">
                Integrate into your editor, CLI, and CI
              </h2>
            </div>

            <Link
              href="/hub"
              className="inline-flex items-center gap-2 px-5 py-2.5 min-h-[44px] rounded-[var(--radius-control)] bg-accent hover:bg-accent-hover text-accent-on font-sans text-sm font-medium transition-colors shrink-0 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-focus-ring"
            >
              <span>Explore developer hub</span>
              <ArrowRight className="w-4 h-4" />
            </Link>
          </div>

          <div className="grid grid-cols-1 lg:grid-cols-3 gap-6">
            {DEVELOPER_SURFACES.map((surface) => {
              const Icon = surface.icon;
              return (
                <Link
                  key={surface.title}
                  href={surface.href}
                  className="group flex flex-col justify-between p-6 rounded-[var(--radius-card)] border border-surface-500 bg-surface-850/90 hover:bg-surface-800/80 hover:border-surface-400 transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-focus-ring"
                >
                  <div>
                    <div className="flex items-center justify-between mb-5">
                      <div className="w-9 h-9 rounded-[var(--radius-control)] border border-surface-500 bg-surface-900 flex items-center justify-center text-accent-text">
                        <Icon className="w-4 h-4" />
                      </div>
                      <span className="font-mono text-xs text-content-subtle tracking-wider">
                        {surface.tag}
                      </span>
                    </div>

                    <h3 className="font-mono text-lg font-semibold text-content-primary mb-2">
                      {surface.title}
                    </h3>
                    <p className="font-sans text-sm text-content-muted leading-relaxed mb-5">
                      {surface.description}
                    </p>
                  </div>

                  <div className="space-y-4">
                    <div className="px-3 py-2.5 rounded-[var(--radius-control)] border border-surface-500/80 bg-surface-900 font-mono text-xs text-content-secondary whitespace-normal break-words">
                      {surface.codePreview}
                    </div>

                    <div className="flex items-center justify-between font-mono text-xs text-accent-text group-hover:text-content-primary transition-colors">
                      <span>{surface.cta}</span>
                      <ArrowRight className="w-3.5 h-3.5 transition-transform group-hover:translate-x-0.5" />
                    </div>
                  </div>
                </Link>
              );
            })}
          </div>
        </div>
      </section>
    </div>
  );
}
