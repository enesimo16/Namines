/**
 * Backend adresi — tek yer.
 *
 * Tarayici ve sunucu AYRI adres kullanir:
 * - Tarayici (`API_BASE`): build'de gomulur. Bos ise same-origin goreli `/api/...`
 *   cagrilir (uretimde Caddy `/api`yi backend'e yonlendirir).
 * - Sunucu (`serverApiBase()`): RUNTIME'da okunur. `next.config` `env`'iyle gomulmez;
 *   aksi halde konteynerin calisma zamani degeri (ic ag adresi) yok sayilir ve
 *   handoff degisimi build'deki genel adrese giderdi.
 */

/** Sondaki `/` ve `/api` kirpilir: cagrilar zaten `/api/...` ile baslar (`/api/api` olmasin). */
export function normalizeApiBase(raw: string): string {
  return raw.trim().replace(/\/+$/, '').replace(/\/api$/i, '');
}

/** Tarayici tarafi taban adres; `next.config.ts` build'de `NEXT_PUBLIC_NAMINES_API_BASE`i set eder. */
export const API_BASE: string = process.env.NEXT_PUBLIC_NAMINES_API_BASE ?? 'http://localhost:5000';

/** Yalnizca sunucu kodu icin — runtime'da okur (bkz. dosya basligi). */
export function serverApiBase(): string {
  return normalizeApiBase(
    process.env.NAMINES_API_INTERNAL ?? process.env.NAMINES_API ?? 'http://localhost:5000',
  );
}

/** `next.config.ts`'in `basePath`i; `public/` varliklari bu on ekle istenmeli (ham <img> basePath'i eklemez). */
export const BASE_PATH: string = process.env.NEXT_PUBLIC_DESK_BASE_PATH ?? '';

export function assetUrl(path: string): string {
  return `${BASE_PATH}${path}`;
}
