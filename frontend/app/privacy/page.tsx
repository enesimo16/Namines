import type { Metadata } from 'next';
import Link from 'next/link';
import { ArrowLeft, ShieldCheck, Lock, EyeOff, FileText, CheckCircle2 } from 'lucide-react';
import Footer from '../../components/layout/Footer';

export const metadata: Metadata = {
  title: 'Privacy Policy — Namines',
  description: 'How Namines handles data, protects privacy, and safeguards your database architectures.',
};

export default function PrivacyPage() {
  return (
    <div className="min-h-screen bg-surface-900 text-content-primary flex flex-col justify-between">
      <div className="w-full max-w-4xl mx-auto px-4 sm:px-6 lg:px-8 py-12 sm:py-20">
        {/* Back link */}
        <Link
          href="/"
          className="inline-flex items-center gap-2 text-xs font-mono text-content-muted hover:text-content-primary mb-8 transition-colors"
        >
          <ArrowLeft className="w-3.5 h-3.5" />
          <span>Back to Namines</span>
        </Link>

        {/* Header */}
        <div className="border-b border-surface-500/80 pb-8 mb-10">
          <div className="inline-flex items-center gap-2 px-3 py-1 rounded-full bg-surface-800 border border-surface-500 text-micro font-mono text-accent-text mb-4">
            <ShieldCheck className="w-3.5 h-3.5" />
            <span>GDPR &amp; CCPA Compliant</span>
          </div>
          <h1 className="text-3xl sm:text-4xl font-bold tracking-tight text-content-primary mb-3">
            Privacy Policy
          </h1>
          <p className="text-xs font-mono text-content-subtle">
            Last updated: September 7, 2026 &bull; Version 2.4
          </p>
        </div>

        {/* Content Body */}
        <div className="space-y-12 text-sm text-content-secondary leading-relaxed">
          {/* Section 1: Overview */}
          <section className="space-y-4">
            <h2 className="text-xl font-bold text-content-primary tracking-tight">
              1. Architecture &amp; Data Philosophy
            </h2>
            <p>
              Namines is built for developers and infrastructure teams who take data privacy seriously. Our core architectural invariant is simple: <strong className="text-content-primary font-semibold">we manage database structures and lifecycles, not your production customer data</strong>.
            </p>
            <p>
              When you use our interactive canvas, AI copilot, or migration linter, Namines processes database schemas (DDL, AST definitions, table names, and column types). We never read, persist, or replicate the application data rows stored inside your databases.
            </p>
          </section>

          {/* Section 2: What We Collect */}
          <section className="space-y-4">
            <h2 className="text-xl font-bold text-content-primary tracking-tight">
              2. Information We Collect
            </h2>
            <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
              <div className="rounded-[var(--radius-card)] border border-surface-500 bg-surface-800/60 p-5 space-y-2">
                <div className="flex items-center gap-2 text-content-primary font-semibold text-xs">
                  <CheckCircle2 className="w-4 h-4 text-accent" />
                  <span>Account &amp; Workspace Data</span>
                </div>
                <p className="text-xs text-content-muted leading-relaxed">
                  Your registered email address, hashed authentication credentials (salted with Argon2id), organization membership, and project names.
                </p>
              </div>

              <div className="rounded-[var(--radius-card)] border border-surface-500 bg-surface-800/60 p-5 space-y-2">
                <div className="flex items-center gap-2 text-content-primary font-semibold text-xs">
                  <FileText className="w-4 h-4 text-accent" />
                  <span>Schema DDL &amp; IR Definitions</span>
                </div>
                <p className="text-xs text-content-muted leading-relaxed">
                  Table structures, column definitions, index layouts, foreign keys, and migration scripts required to compile deterministic SQL and ERD graphs.
                </p>
              </div>
            </div>
          </section>

          {/* Section 3: What We Never Collect */}
          <section className="space-y-4">
            <h2 className="text-xl font-bold text-content-primary tracking-tight">
              3. Information We Never Access or Retain
            </h2>
            <div className="rounded-[var(--radius-card)] border border-surface-500 bg-surface-800/40 p-6 space-y-4">
              <div className="flex items-start gap-3">
                <EyeOff className="w-5 h-5 text-accent-text shrink-0 mt-0.5" />
                <div className="space-y-1">
                  <h3 className="text-xs font-bold text-content-primary uppercase tracking-wider">
                    Zero Customer Row Data
                  </h3>
                  <p className="text-xs text-content-muted leading-relaxed">
                    Namines never queries or ingests actual table rows, customer records, payment cards, or PII stored in your operational databases. Introspection queries query strictly system metadata catalogs (such as PostgreSQL <code className="font-mono text-accent-text">information_schema</code> and <code className="font-mono text-accent-text">pg_catalog</code>).
                  </p>
                </div>
              </div>

              <div className="flex items-start gap-3">
                <Lock className="w-5 h-5 text-accent-text shrink-0 mt-0.5" />
                <div className="space-y-1">
                  <h3 className="text-xs font-bold text-content-primary uppercase tracking-wider">
                    Zero Plain-Text Credentials
                  </h3>
                  <p className="text-xs text-content-muted leading-relaxed">
                    Connection strings and BYOK API keys are encrypted at rest using authenticated <strong className="text-content-primary font-semibold">AES-256-GCM</strong>. Decryption keys reside exclusively in secure memory enclaves during active test container execution and are never logged to disk or console output.
                  </p>
                </div>
              </div>
            </div>
          </section>

          {/* Section 4: Ephemeral Sandboxes */}
          <section className="space-y-4">
            <h2 className="text-xl font-bold text-content-primary tracking-tight">
              4. Ephemeral Test Containers &amp; Retention
            </h2>
            <p>
              When you execute DDL validation or dry-run migration tests (&ldquo;Run Tests&rdquo;), Namines spins up isolated, ephemeral database engines (PostgreSQL, MySQL, SQLite, or MSSQL) using native container orchestration without mounting the host socket.
            </p>
            <p>
              Once validation completes, the ephemeral container and its volatile storage volume are <strong className="text-content-primary font-semibold">immediately terminated and wiped</strong>. No schema artifacts or execution traces remain on test runners beyond the immediate diagnostic run log.
            </p>
          </section>

          {/* Section 5: Cookies & Storage */}
          <section id="cookies" className="space-y-4">
            <h2 className="text-xl font-bold text-content-primary tracking-tight">
              5. Cookies &amp; Local Preferences
            </h2>
            <p>
              Namines uses strictly necessary first-party cookies for session authentication:
            </p>
            <ul className="list-disc pl-5 space-y-2 text-xs text-content-muted">
              <li>
                <strong className="text-content-primary">Authentication Cookie:</strong> Stored with <code className="font-mono text-accent-text">HttpOnly</code>, <code className="font-mono text-accent-text">SameSite=Strict</code>, and <code className="font-mono text-accent-text">Secure</code> flags to prevent cross-site scripting (XSS) extraction.
              </li>
              <li>
                <strong className="text-content-primary">Theme Preference:</strong> Stored locally in your browser&apos;s <code className="font-mono text-accent-text">localStorage</code> to remember your Dark/Light display mode without transmitting tracking tokens.
              </li>
            </ul>
            <p>
              We do not employ third-party advertising cookies, cross-site profiling trackers, or invasive data brokers.
            </p>
          </section>

          {/* Section 6: User Rights & Contact */}
          <section className="space-y-4 border-t border-surface-500/80 pt-8">
            <h2 className="text-xl font-bold text-content-primary tracking-tight">
              6. Your Rights &amp; Contact
            </h2>
            <p>
              Under GDPR, CCPA, and global privacy frameworks, you retain full rights to inspect, export, or permanently delete your account, project metadata, and schema histories at any time from your Namines settings.
            </p>
            <p className="text-xs text-content-muted">
              For privacy inquiries, data deletion requests, or Data Processing Addendum (DPA) execution, please contact our Data Protection Officer at:{' '}
              <a
                href="mailto:privacy@namines.com"
                className="text-accent-text hover:underline font-mono"
              >
                privacy@namines.com
              </a>
            </p>
          </section>
        </div>
      </div>

      <Footer />
    </div>
  );
}
