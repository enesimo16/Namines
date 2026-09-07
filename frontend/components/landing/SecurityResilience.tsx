'use client';

import {
  ShieldCheck,
  Lock,
  FileCheck2,
  KeyRound,
  KeySquare,
  Server,
} from 'lucide-react';

const SECURITY_ITEMS = [
  {
    icon: ShieldCheck,
    title: 'Zero SSRF & Private Introspection',
    description:
      'Inspect production or staging databases safely. Loopback, RFC-1918 private subnets, and cloud metadata endpoints are strictly blocked to prevent Server-Side Request Forgery.',
  },
  {
    icon: Lock,
    title: 'Read-Only Gateway Guardrails',
    description:
      'The generated data gateway defaults to immutable read-only operations. Write actions (create, update, delete) require dedicated cryptographic API keys with explicit table scopes.',
  },
  {
    icon: FileCheck2,
    title: 'Zero Unreviewed Writes & Safe Policies',
    description:
      'Automated risk classification categorizes all migrations into Safe, Warning, and Destructive. Irreversible alterations (drops, type shrinking) cannot run without explicit signed audit approval.',
  },
  {
    icon: KeyRound,
    title: 'Immutable ChangeRequest Audit Logs',
    description:
      'Full traceability for every schema modification. Every proposed diff, test container run, and approval is logged with actor identification, timestamp, and canonical AST snapshots.',
  },
  {
    icon: KeySquare,
    title: 'AES-256-GCM Secret Encryption',
    description:
      'Database connection strings, certificates, and API tokens are encrypted at rest with authenticated AES-256-GCM encryption. Decryption happens strictly in-memory during execution.',
  },
  {
    icon: Server,
    title: 'Hardened JWT & HttpOnly Sessions',
    description:
      'Identity tokens are stored exclusively in HttpOnly, SameSite=Strict cookies with secure flags. Zero client-side token leakage protects against XSS, clickjacking, and CSRF attacks.',
  },
];

export default function SecurityResilience() {
  return (
    <section className="relative z-10 w-full max-w-[var(--w-app)] mx-auto px-4 sm:px-6 lg:px-8 py-20 sm:py-28 border-t border-surface-500/80">
      {/* Section Header */}
      <div className="text-center mb-16 max-w-2xl mx-auto">
        <span className="font-mono text-xs font-bold tracking-widest uppercase text-accent-text block mb-3">
          Security &amp; Compliance
        </span>
        <h2 className="text-3xl sm:text-4xl lg:text-5xl font-bold tracking-tight text-content-primary mb-4">
          Stay secure and resilient by default
        </h2>
        <p className="text-xs sm:text-sm text-content-muted leading-relaxed">
          Zero vendor lock-in, zero accidental data loss, and zero unvetted writes. We build safeguards at the protocol layer so your team can move fast without breaking production.
        </p>
      </div>

      {/* 6-Grid Security Cards */}
      <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-6">
        {SECURITY_ITEMS.map((item) => (
          <div
            key={item.title}
            className="rounded-[var(--radius-card)] border border-surface-500 bg-surface-900/90 p-6 flex flex-col gap-4 shadow-lg hover:border-surface-400 transition-colors"
          >
            <div className="w-10 h-10 rounded-[var(--radius-control)] bg-surface-800 flex items-center justify-center border border-surface-500 text-accent-text shrink-0">
              <item.icon className="w-5 h-5" />
            </div>
            <div>
              <h3 className="text-sm sm:text-base font-bold text-content-primary mb-2">
                {item.title}
              </h3>
              <p className="text-xs text-content-muted leading-relaxed">
                {item.description}
              </p>
            </div>
          </div>
        ))}
      </div>
    </section>
  );
}
