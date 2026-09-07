'use client';

import { useEffect, useState } from 'react';

/**
 * Başlığın renkli, dönen son parçası (render.com'un "for [data pipelines /
 * HIPAA apps / ...]" efektinden ilhamla).
 *
 * <b>Uydurma kategori yok:</b> bu isimler rastgele seçilmedi — hepsi
 * `lib/templates.ts`'teki GERÇEK, demoda (girişsiz) denenebilen şablonların
 * karşılığı (E-Commerce, SaaS Platform, CRM & Sales, Multi-vendor
 * Marketplace, Healthcare/EMR, Banking & Ledger). Ziyaretçi "e-ticaret
 * platformları" gördüğünde, `/demo`'ya gidip GERÇEKTEN öyle bir şema bulabilir.
 */
const WORDS = [
  'e-commerce platforms',
  'SaaS products',
  'CRM systems',
  'marketplaces',
  'healthcare records',
  'banking ledgers',
];

const INTERVAL_MS = 2600;

export default function RotatingHeadline({ light = false }: { light?: boolean }) {
  const [index, setIndex] = useState(0);
  // Vurgu ilk boyamada YOK: sunucu tarafında render edilen ilk kelime,
  // hidrasyon bitmeden animasyonsuz görünsün diye — aksi hâlde SSR/CSR
  // arasında bir "flash" olurdu.
  const [mounted, setMounted] = useState(false);

  useEffect(() => {
    setMounted(true);
    const id = setInterval(() => {
      setIndex(i => (i + 1) % WORDS.length);
    }, INTERVAL_MS);
    return () => clearInterval(id);
  }, []);

  return (
    <span
      className="inline-block bg-clip-text text-transparent transition-opacity duration-300"
      style={{
        backgroundImage: light
          ? 'linear-gradient(90deg, var(--accent), var(--accent-hover))'
          : 'linear-gradient(90deg, var(--accent-hover), var(--accent-text))',
        opacity: mounted ? 1 : 0.9,
      }}
      // Ekran okuyucu her 2.6 sn'de bir kelimeyi tekrar duyurmasın — tek
      // seferlik, sabit bir etiket yeterli.
      aria-label="a variety of real-world schemas"
    >
      {WORDS[index]}
    </span>
  );
}
