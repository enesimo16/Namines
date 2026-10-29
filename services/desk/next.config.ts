import type { NextConfig } from 'next';

/**
 * Namines Desk — AYRI mikroservis.
 *
 * Ana Namines backend'ine YALNIZCA HTTP ile baglanir; `Namines.Core`'a ya da
 * ana `frontend/` uygulamasina hicbir kod referansi YOKTUR (bkz.
 * third-phase/00-BASLA-BURADAN.md §5). Bu kural bilincli: referans verildigi an
 * bu, klasoru ayrilmis tek bir monolit olur.
 */
function clientApiBase(): string {
  const raw =
    process.env.NAMINES_PUBLIC_API ??
    process.env.NAMINES_API ??
    (process.env.NODE_ENV === 'production' ? '' : 'http://localhost:5000');
  return raw.trim().replace(/\/+$/, '').replace(/\/api$/i, '');
}

const nextConfig: NextConfig = {
  // Kök ACIKCA belirtiliyor: Next aksi halde yukari dogru package-lock.json
  // ariyor ve ev dizinindeki alakasiz bir dosyayi kok saniyordu.
  turbopack: { root: __dirname },
  ...(process.env.DESK_BASE_PATH ? { basePath: process.env.DESK_BASE_PATH } : {}),
  env: {
    // TARAYICI icin API tabani (build-time). Sunucu kodu NAMINES_API_INTERNAL /
    // NAMINES_API'yi RUNTIME'da kendisi okur (lib/config.ts) — burada gomulmez.
    // Bos deger = same-origin goreli `/api/...`; sondaki `/api` kirpilir.
    NEXT_PUBLIC_NAMINES_API_BASE: clientApiBase(),
    NEXT_PUBLIC_DESK_BASE_PATH: process.env.DESK_BASE_PATH ?? '',
    // Ana Namines UYGULAMASI (backend API DEGIL) — "Ana uygulamada bir proje
    // olusturun" gibi baglantilar buraya gider. NAMINES_API'yle KARISTIRILMASIN:
    // biri API sunucusu (JSON doner), diger tarayicida acilan uygulama.
    NAMINES_FRONTEND: process.env.NAMINES_FRONTEND ?? 'http://localhost:3000',
    DESK_BASE_PATH: process.env.DESK_BASE_PATH ?? '',
  },
};

export default nextConfig;
