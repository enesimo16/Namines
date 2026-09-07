import { defineConfig } from 'vitest/config';

/**
 * Namines Desk'in KENDİ test yapılandırması — mikroservis sınırı burada da
 * geçerli (third-phase §5): ana `frontend/`'in test altyapısı KOPYALANMIYOR,
 * Desk kendi `vitest.config.ts`'ine sahip.
 *
 * `environment: 'node'` yeterli: test edilen katman (`lib/*.ts`) saf
 * fonksiyonlar + `fetch` — DOM'a ihtiyaç yok. Bileşen testleri eklenirse
 * (React Testing Library) o zaman `jsdom`'a geçilir.
 */
export default defineConfig({
  test: {
    environment: 'node',
    include: ['**/*.test.ts'],
    exclude: ['node_modules', '.next'],
  },
});
