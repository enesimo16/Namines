import { defineConfig } from 'vitest/config';

/**
 * Frontend birim testleri (FE-001 / TD-001 / B-15).
 *
 * **Neden `node` ortamı, jsdom değil:** Test edilen şey zustand store'larının
 * MANTIĞI — indirim hesabı değil ama aynı cinsten: girdi → durum geçişi.
 * jsdom, testin ihtiyaç duymadığı bir tarayıcı taklidi kurup her koşuya saniye
 * ekliyor. DOM'a gerçekten dokunan iki API (`setTimeout` zamanlayıcıları ve
 * `requestAnimationFrame`) `test/setup.ts`'te asgari biçimde karşılanıyor.
 *
 * Bileşen testleri (React Testing Library) BU KAPSAMDA DEĞİL: onlar jsdom
 * gerektiriyor ve ayrı bir karar. Burada kapatılan açık, store'ların hiç
 * testi olmamasıydı.
 */
export default defineConfig({
  test: {
    environment: 'node',
    include: ['store/**/*.test.ts', 'lib/**/*.test.ts', 'utils/**/*.test.ts'],
    setupFiles: ['./test/setup.ts'],
    // Next.js derlemesi ve `.next` çıktısı testlerin ilgi alanı değil.
    exclude: ['node_modules/**', '.next/**'],
  },
});
