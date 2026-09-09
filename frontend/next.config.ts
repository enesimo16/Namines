import type { NextConfig } from "next";

const nextConfig: NextConfig = {
  output: 'standalone',

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
};

export default nextConfig;
