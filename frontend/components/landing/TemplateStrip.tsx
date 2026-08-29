'use client';

import Link from 'next/link';
import { ArrowRight } from 'lucide-react';
import { TEMPLATES } from '../../lib/templates';

/**
 * İniş sayfasındaki şablon galerisi.
 *
 * **Neden buraya taşındı:** galeri zaten vardı, ama yalnızca canvas'ın içindeki
 * bir modaldan açılıyordu — yani onu görmek için önce şema üretmek, yani önce
 * hesap açmak gerekiyordu. Ürüne bakmaya gelen biri, ürünün ne ürettiğine dair
 * tek somut örneği göremeden ayrılıyordu.
 *
 * **Kartlar demoya gidiyor, canvas'a değil.** Canvas'ta şablon yüklemek mevcut
 * çalışmanın üstüne yazıyor (Replace); tanımadığı bir ürüne ilk tıklamasında
 * ziyaretçiyi böyle bir karara zorlamak yanlış olurdu. Demo salt okunur.
 */
export default function TemplateStrip() {
  return (
    <section className="w-full max-w-4xl px-4 mt-14">
      <div className="flex items-end justify-between mb-4 gap-4">
        <div>
          <h2 className="text-lg font-bold text-content-primary">Start from a schema that already works</h2>
          <p className="text-xs text-content-muted mt-1">
            Open any of these and watch the checks run — no account needed.
          </p>
        </div>
        <Link
          href="/demo"
          className="hidden sm:flex items-center gap-1.5 text-xs font-semibold text-content-secondary hover:text-content-primary transition-colors shrink-0"
        >
          Live demo
          <ArrowRight className="w-3.5 h-3.5" />
        </Link>
      </div>

      <div className="grid grid-cols-2 lg:grid-cols-5 gap-3">
        {TEMPLATES.map(tpl => (
          <Link
            key={tpl.key}
            href={`/demo?template=${encodeURIComponent(tpl.key)}`}
            className="glass-panel rounded-xl p-4 flex flex-col gap-2 transition-all hover:bg-white/[0.06]"
          >
            <span className="text-xs font-semibold text-content-primary">{tpl.label}</span>
            <span className="text-[10px] text-content-muted leading-snug line-clamp-2">
              {tpl.description}
            </span>
            <span className="text-[10px] text-content-subtle font-medium mt-auto pt-1">
              {tpl.schema.tables.length} tables · {tpl.schema.relations.length} relations
            </span>
          </Link>
        ))}
      </div>
    </section>
  );
}
