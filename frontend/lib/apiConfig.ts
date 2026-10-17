/**
 * API adresinin TEK kaynağı.
 *
 * NEXT_PUBLIC_API_URL build sırasında bundle'a gömülür; runtime'da değiştirilemez.
 * Docker ile build ediliyorsa --build-arg olarak geçilmeli.
 *
 * Adresi buraya hardcode ETME: 'http://localhost:5000' yazan her yer deploy'da
 * kullanıcının tarayıcısından kendi makinesine istek atmaya çalışır ve özellik bozulur.
 */

const rawOrigin = (
  (typeof process !== 'undefined' && process.env.NEXT_PUBLIC_API_URL) || ''
).replace(/\/+$/, '');

const isLocalDev = /^https?:\/\/(localhost|127\.0\.0\.1)(:\d+)?$/i.test(rawOrigin);

export const API_ORIGIN: string = isLocalDev ? rawOrigin : '';

/** REST uçlarının kökü. Üretimde '/api' (same-origin proxy), yerelde 'http://localhost:5000/api' */
export const API_BASE_URL = API_ORIGIN ? `${API_ORIGIN}/api` : '/api';

/** SignalR canvas hub adresi (HTTPS/WSS). */
export const CANVAS_HUB_URL = isLocalDev
  ? `${rawOrigin}/hubs/canvas`
  : 'https://130-162-254-97.sslip.io/hubs/canvas';

/**
 * Sunucunun döndürdüğü göreli yolu (ör. '/api/docker/download/{id}') tam URL'e çevirir.
 * Zaten mutlak bir URL verilirse olduğu gibi bırakır.
 */
export const toAbsoluteApiUrl = (path: string): string =>
  /^https?:\/\//i.test(path) ? path : `${API_ORIGIN}${path.startsWith('/') ? path : `/${path}`}`;
