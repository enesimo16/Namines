'use client';

import { FileWarning, GitCompareArrows, ShieldCheck, TerminalSquare } from 'lucide-react';

/**
 * "Neden biz" bölümü.
 *
 * **Neden gerekli:** ürünün en savunulabilir farkı — üretimi yapan AI'ın
 * çıktısını deterministik bir kural motorunun ayrıca kanıtlaması — hiçbir yerde
 * yazmıyordu. Ziyaretçi için Namines, "prompt yazınca şema çizen" onlarca
 * araçtan biri gibi görünüyordu; ayırt eden şey görünmez olduğu sürece yoktur.
 *
 * **Her madde koddaki gerçek bir yeteneğe karşılık geliyor** ve iddiaların
 * hepsi demoda (girişsiz) denenebiliyor. Var olmayan bir özelliği buraya
 * yazmak, ürünü ilk temasta yalancı çıkarırdı — bu sayfanın amacı tam tersi.
 */

const POINTS = [
  {
    icon: ShieldCheck,
    title: 'The AI proposes. A rule engine decides.',
    body:
      'Every generated schema goes through deterministic checks before you see it. ' +
      'The findings are not written by a model — they come from code that produces ' +
      'the same answer every time.',
  },
  {
    icon: TerminalSquare,
    title: 'Six engines, real DDL.',
    body:
      'PostgreSQL, MySQL, MariaDB, SQL Server, Oracle and SQLite. When a feature ' +
      'cannot survive a move between engines, you are told exactly what would be ' +
      'lost — before the migration, not after.',
  },
  {
    icon: GitCompareArrows,
    title: 'It never writes to your database.',
    body:
      'Namines produces the migration and proves what it will do. Running it stays ' +
      'your decision, in your own tooling. Nothing here can drop a column behind your back.',
  },
  {
    icon: FileWarning,
    title: 'Breaking changes surface before the merge.',
    body:
      'Branch a schema, review the diff, see which linked databases and which code ' +
      'paths a removal would break. The impact analysis runs on rules, not guesses.',
  },
];

export default function WhyNamines() {
  return (
    <section className="w-full max-w-[var(--w-app)] px-4 sm:px-6 lg:px-8 mt-16">
      <div className="text-center mb-8">
        <h2 className="text-xl sm:text-2xl font-bold text-content-primary mb-2">
          AI generates. The rule engine proves.
        </h2>
        <p className="text-sm text-content-muted max-w-xl mx-auto leading-relaxed">
          Anything can draw you a diagram. The hard part is being sure the thing it drew
          actually runs, on your engine, without losing data.
        </p>
      </div>

      <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
        {POINTS.map(point => (
          <div
            key={point.title}
            className="glass-panel rounded-2xl p-5 flex flex-col gap-3"
          >
            <point.icon className="w-5 h-5 text-content-primary shrink-0" />
            <div>
              <h3 className="text-sm font-semibold text-content-primary mb-1.5">{point.title}</h3>
              <p className="text-xs text-content-muted leading-relaxed">{point.body}</p>
            </div>
          </div>
        ))}
      </div>
    </section>
  );
}
