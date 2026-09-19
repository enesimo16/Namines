import { defineConfig } from 'vitest/config';
import path from 'node:path';

/**
 * Frontend birim testleri (FE-001 / TD-001 / B-15).
 *
 * **Neden varsayılan `node` ortamı, jsdom değil:** Test edilen şey zustand
 * store'larının MANTIĞI — indirim hesabı değil ama aynı cinsten: girdi →
 * durum geçişi. jsdom, testin ihtiyaç duymadığı bir tarayıcı taklidi kurup
 * her koşuya saniye ekliyor. DOM'a gerçekten dokunan iki API (`setTimeout`
 * zamanlayıcıları ve `requestAnimationFrame`) `test/setup.ts`'te asgari
 * biçimde karşılanıyor.
 *
 * Bileşen testleri (React Testing Library, ".test.tsx" dosyaları) jsdom
 * GEREKTİRİR — bu vitest sürümünde `environmentMatchGlobs` etkisiz kaldığı
 * için her `.test.tsx` dosyası kendi `// @vitest-environment jsdom`
 * yorumuyla ortamını açıkça seçiyor (bkz. LaunchPanel.test.tsx).
 */
export default defineConfig({
  resolve: {
    alias: { '@': path.resolve(import.meta.dirname, '.') },
  },
  test: {
    environment: 'node',
    include: [
      'store/**/*.test.ts',
      'lib/**/*.test.ts',
      'utils/**/*.test.ts',
      'hooks/**/*.test.tsx',
      'components/**/*.test.tsx',
    ],
    setupFiles: ['./test/setup.ts', './test/setup-dom.ts'],
    // Next.js derlemesi ve `.next` çıktısı testlerin ilgi alanı değil.
    exclude: ['node_modules/**', '.next/**'],
  },
});
