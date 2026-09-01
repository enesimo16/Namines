import type { NextConfig } from 'next';

/**
 * Namines Desk — AYRI mikroservis.
 *
 * Ana Namines backend'ine YALNIZCA HTTP ile baglanir; `Namines.Core`'a ya da
 * ana `frontend/` uygulamasina hicbir kod referansi YOKTUR (bkz.
 * third-phase/00-BASLA-BURADAN.md §5). Bu kural bilincli: referans verildigi an
 * bu, klasoru ayrilmis tek bir monolit olur.
 */
const nextConfig: NextConfig = {
  // Kök ACIKCA belirtiliyor: Next aksi halde yukari dogru package-lock.json
  // ariyor ve ev dizinindeki alakasiz bir dosyayi kok saniyordu.
  turbopack: { root: __dirname },
  env: {
    // Tek yapilandirma noktasi. Uretimde gercek API adresi verilir.
    NAMINES_API: process.env.NAMINES_API ?? 'http://localhost:5000',
  },
};

export default nextConfig;
