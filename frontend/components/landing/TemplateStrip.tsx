'use client';

import Link from 'next/link';
import { ArrowRight } from 'lucide-react';
import { TEMPLATES, templatesOfSize, type SchemaTemplate } from '../../lib/templates';

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
 *
 * **Hepsi DEĞİL, altı tanesi listeleniyor.** Şablon sayısı 20'ye çıkınca tam
 * liste mobilde ~1300px'lik bir kart duvarına dönüşüyordu ve altındaki "neden
 * biz" bölümü katlanmanın çok altında kalıyordu. İniş sayfasının işi kataloğu
 * göstermek değil, katalogun var olduğunu göstermek; tamamı demoda.
 */

/** Vitrin: her ölçekten örnek — ziyaretçi aralığın kendisini görmeli. */
const FEATURED: SchemaTemplate[] = [
  ...templatesOfSize('large').slice(0, 1),
  ...templatesOfSize('standard').slice(0, 4),
  ...templatesOfSize('mini').slice(0, 1),
].filter(Boolean);

const SIZE_LABEL: Record<SchemaTemplate['size'], string> = {
  mini: 'Quick start',
  standard: 'Full product',
  large: 'Enterprise',
};

export default function TemplateStrip() {
  const totalTables = TEMPLATES.reduce((sum, t) => sum + t.schema.tables.length, 0);

  return (
    <section className="w-full max-w-4xl px-4 mt-14">
      <div className="flex flex-wrap items-end justify-between mb-4 gap-x-4 gap-y-2">
        <div className="min-w-0">
          <h2 className="text-base sm:text-lg font-bold text-content-primary">
            Start from a schema that already works
          </h2>
          <p className="text-xs text-content-muted mt-1">
            {TEMPLATES.length} schemas · {totalTables} tables — every one passes our own checks.
          </p>
        </div>
        <Link
          href="/demo"
          className="flex items-center gap-1.5 text-xs font-semibold text-content-secondary hover:text-content-primary transition-colors shrink-0 min-h-11 sm:min-h-0"
        >
          See all
          <ArrowRight className="w-3.5 h-3.5" />
        </Link>
      </div>

      <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-3">
        {FEATURED.map(tpl => (
          <Link
            key={tpl.key}
            href={`/demo?template=${encodeURIComponent(tpl.key)}`}
            className="glass-panel rounded-xl p-4 flex flex-col gap-2 transition-all hover:bg-white/[0.06]"
          >
            <div className="flex items-baseline justify-between gap-2">
              <span className="text-xs font-semibold text-content-primary">{tpl.label}</span>
              <span className="text-[9px] uppercase tracking-wider font-bold text-content-subtle shrink-0">
                {SIZE_LABEL[tpl.size]}
              </span>
            </div>
            <span className="text-[11px] text-content-muted leading-snug line-clamp-2">
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
