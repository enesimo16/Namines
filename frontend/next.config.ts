import type { NextConfig } from "next";

const nextConfig: NextConfig = {
  output: 'standalone',

  // Geliştirmede Next'in yuvarlak "N" durum göstergesi sol üstte duruyor ve uygulamanın
  // kendi logosunun/gezinmesinin üstüne biniyordu (ekran görüntülerinde "iç içe" görünen
  // ilk şey). Yalnızca `next dev`'de var, üretimi etkilemez. Derleme/çalışma HATA katmanı
  // devre dışı bırakılmıyor — yalnızca bu rozet.
  devIndicators: false,

  compiler: {
    // Üretim derlemesinde `console.log` çağrıları siliniyor.
    //
    // İki gerekçe: (1) hata ayıklama çıktısı kullanıcının konsolunda yer alır ve
    // bazen istemeden veri sızdırır; (2) silinmesini geliştiricinin hatırlamasına
    // bırakmak, zamanla biriken bir borç üretir.
    //
    // `console.error` ve `console.warn` KORUNUYOR: gerçek arıza sinyalleri ve
    // hata izleme araçları onları topluyor. Sessize almak, üretimdeki bir
    // sorunu görünmez yapardı.
    removeConsole: process.env.NODE_ENV === 'production'
      ? { exclude: ['error', 'warn'] }
      : false,
  },

  async rewrites() {
    const backendUrl = process.env.BACKEND_API_URL || 'https://130-162-254-97.sslip.io';
    const deskUrl = process.env.DESK_ORIGIN_URL || 'https://desk.130-162-254-97.sslip.io';
    return [
      // PostHog Ters Proxy (Reverse Proxy) — Reklam engelleyicilere (ad-blocker) takılmadan
      // ve Safari ITP kısıtlamalarına uğramadan analitik ve feature flag isteklerini güvenle iletir.
      {
        source: '/ingest/static/:path*',
        destination: 'https://eu-assets.i.posthog.com/static/:path*',
      },
      {
        source: '/ingest/:path*',
        destination: 'https://eu.i.posthog.com/:path*',
      },
      {
        source: '/ingest/decide',
        destination: 'https://eu.i.posthog.com/decide',
      },
      {
        source: '/api/:path*',
        destination: `${backendUrl}/api/:path*`,
      },
      {
        source: '/hubs/:path*',
        destination: `${backendUrl}/hubs/:path*`,
      },
      {
        source: '/desk',
        destination: `${deskUrl}/desk`,
      },
      {
        source: '/desk/:path*',
        destination: `${deskUrl}/desk/:path*`,
      },
    ];
  },
};

export default nextConfig;
