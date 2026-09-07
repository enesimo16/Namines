import type { Metadata } from 'next';
import Link from 'next/link';
import {
  Wand2, ArrowRight, ShieldCheck, TerminalSquare, GitCompareArrows,
  FileWarning, LayoutGrid, Users2,
} from 'lucide-react';
import Hero from '../components/landing/Hero';
import ThemeToggleButton from '../components/landing/ThemeToggleButton';

/**
 * Namines'in genel ana sayfası (marketing/tanıtım) — render.com'dan ilham
 * alındı: koyu zemin, tek aksan rengi, büyük başlık + kısa alt metin, hemen
 * altında ürünün GERÇEK görünümünü taklit eden bir önizleme paneli, sade bir
 * özellik ızgarası, üç adımlık akış, tek bir kapanış çağrısı.
 *
 * <b>Bu sayfa artık "workspace" değil.</b> Eskiden kök (`/`) doğrudan AI
 * prompt kutusuydu (D2 mikroservis öncesi tasarım) — kullanıcı /new'e taşındı
 * (bkz. app/new/page.tsx). Kök artık ürünü hiç kullanmamış birine ne olduğunu
 * anlatan tanıtım sayfası; "Start building" CTA'sı asıl işi yapan /new'e
 * gönderiyor.
 *
 * <b>Uydurma veri yok:</b> burada hiçbir müşteri logosu, sayı veya alıntı
 * yok — hiçbiri gerçek değil ve icat edilemez (bkz. WhyNamines.tsx'in aynı
 * ilkesi). Her iddia zaten kodda var olan ve girişsiz demoda denenebilen bir
 * yeteneğe karşılık geliyor.
 */
export const metadata: Metadata = {
  title: 'Namines — AI Database Architecture Builder',
  description: 'Describe a database in plain English. Get a schema a rule engine has already checked, in six real SQL dialects.',
};

const FEATURES = [
  {
    icon: ShieldCheck,
    title: 'The AI proposes. A rule engine decides.',
    body: 'Every generated schema runs through deterministic checks before you see it — missing indexes, orphaned foreign keys, naming drift. The findings come from code, not from a model guessing twice.',
  },
  {
    icon: TerminalSquare,
    title: 'Six engines, real DDL.',
    body: 'PostgreSQL, MySQL, MariaDB, SQL Server, Oracle and SQLite. When a feature cannot survive a move between engines, you are told exactly what would be lost — before the migration, not after.',
  },
  {
    icon: GitCompareArrows,
    title: 'Branch a schema like a codebase.',
    body: 'Propose a change, see the diff, review which linked databases and code paths a column removal would break — all before anything merges.',
  },
  {
    icon: FileWarning,
    title: 'It never writes to your database.',
    body: 'Namines produces the migration and proves what it will do. Running it stays your decision, in your own tooling. Nothing here can drop a column behind your back.',
  },
  {
    icon: LayoutGrid,
    title: 'A hosted admin panel, generated for you.',
    body: 'Namines Desk turns your schema into a deterministic CRUD panel — browse rows, run a read-only SQL console, manage API keys — no code to write or host yourself.',
  },
  {
    icon: Users2,
    title: 'Design together, live.',
    body: 'Multiple people on the same canvas, cursors and edits in real time, so a schema review stops being a screenshot pasted into a chat.',
  },
];

const STEPS = [
  {
    n: '01',
    title: 'Describe it',
    body: 'Write a sentence, drop in a diagram, or point at an OpenAPI/GraphQL schema. Namines asks a few clarifying questions before it commits to a design.',
  },
  {
    n: '02',
    title: 'Watch it get checked',
    body: 'The AI drafts tables and relationships; a deterministic rule engine verifies the result against the target engine — same input, same answer, every time.',
  },
  {
    n: '03',
    title: 'Ship the real thing',
    body: 'Export true DDL for your database, open a live hosted admin panel, or hand the diff to a teammate for review — all from the same schema.',
  },
];

export default function HomePage() {
  return (
    <div className="relative flex flex-col items-center overflow-x-hidden">
      <Hero />

      {/* ───────────────────────── Features ───────────────────────── */}
      <section id="features" className="relative z-10 w-full max-w-[var(--w-app)] px-4 sm:px-6 lg:px-8 mb-16 sm:mb-24 scroll-mt-20">
        <div className="text-center mb-10">
          <h2 className="text-xl sm:text-2xl font-bold text-content-primary mb-2">
            Everything past the first draft
          </h2>
          <p className="text-sm text-content-muted max-w-xl mx-auto leading-relaxed">
            Anything can draw you a diagram. The hard part is being sure it actually
            runs, on your engine, without losing data — and staying usable once
            the team grows past one person.
          </p>
        </div>

        <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-3">
          {FEATURES.map(f => (
            <div key={f.title} className="glass-panel rounded-[var(--radius-modal)] p-5 flex flex-col gap-3">
              <f.icon className="w-5 h-5 text-content-primary shrink-0" />
              <div>
                <h3 className="text-sm font-semibold text-content-primary mb-1.5">{f.title}</h3>
                <p className="text-xs text-content-muted leading-relaxed">{f.body}</p>
              </div>
            </div>
          ))}
        </div>
      </section>

      {/* ─────────────────────── How it works ─────────────────────── */}
      <section id="how-it-works" className="relative z-10 w-full max-w-[var(--w-app)] px-4 sm:px-6 lg:px-8 mb-16 sm:mb-24 scroll-mt-20">
        <div className="text-center mb-10">
          <h2 className="text-xl sm:text-2xl font-bold text-content-primary mb-2">
            From a sentence to a running schema
          </h2>
        </div>

        <div className="grid grid-cols-1 sm:grid-cols-3 gap-6 sm:gap-4">
          {STEPS.map((s, i) => (
            <div key={s.n} className="relative flex flex-col gap-2 px-1">
              <span className="font-mono text-3xl text-content-primary/15 font-bold">{s.n}</span>
              <h3 className="text-sm font-semibold text-content-primary">{s.title}</h3>
              <p className="text-xs text-content-muted leading-relaxed">{s.body}</p>
              {i < STEPS.length - 1 && (
                <ArrowRight className="hidden sm:block absolute -right-6 top-2 w-4 h-4 text-content-primary/20" />
              )}
            </div>
          ))}
        </div>
      </section>

      {/* ───────────────────────── Closing CTA ───────────────────────── */}
      <section className="relative z-10 w-full max-w-[var(--w-app)] px-4 sm:px-6 lg:px-8 mb-16 sm:mb-20">
        <div className="glass-panel rounded-[var(--radius-modal)] p-8 sm:p-12 flex flex-col items-center text-center gap-5">
          <h2 className="text-xl sm:text-2xl font-bold text-content-primary max-w-[28ch]">
            Describe your next database. See it checked before you trust it.
          </h2>
          <Link
            href="/new"
            className="inline-flex items-center justify-center gap-2 bg-content-primary hover:bg-content-secondary text-surface-900 font-semibold py-3 px-6 rounded-[var(--radius-card)] transition-all duration-200 text-sm"
          >
            <Wand2 className="w-4 h-4" />
            Start building — it&apos;s free
          </Link>
        </div>
      </section>

      {/* ────────────────────────── Footer ────────────────────────── */}
      <footer className="relative z-10 w-full border-t border-content-primary/10 mt-auto">
        <div className="w-full max-w-[var(--w-app)] mx-auto px-4 sm:px-6 lg:px-8 py-8 flex flex-col sm:flex-row items-center justify-between gap-4">
          <span className="font-mono font-bold tracking-widest text-xs text-content-muted">
            NAMINES
          </span>
          <nav aria-label="Footer" className="flex items-center gap-5 text-xs text-content-muted">
            <Link href="/new" className="hover:text-content-primary transition-colors">Build</Link>
            <Link href="/demo" className="hover:text-content-primary transition-colors">Demo</Link>
          </nav>
          <span className="text-[11px] text-content-subtle">© {new Date().getFullYear()} Namines</span>
        </div>
      </footer>

      <ThemeToggleButton />
    </div>
  );
}
