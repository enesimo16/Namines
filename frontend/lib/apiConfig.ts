/**
 * API adresinin TEK kaynağı.
 *
 * NEXT_PUBLIC_API_URL build sırasında bundle'a gömülür; runtime'da değiştirilemez.
 * Docker ile build ediliyorsa --build-arg olarak geçilmeli.
 *
 * Adresi buraya hardcode ETME: 'http://localhost:5000' yazan her yer deploy'da
 * kullanıcının tarayıcısından kendi makinesine istek atmaya çalışır ve özellik bozulur.
 */

/** API kök adresi — '/api' EKLENMEZ. Örn: https://api.namines.app */
export const API_ORIGIN: string = (
  (typeof process !== 'undefined' && process.env.NEXT_PUBLIC_API_URL) || 'http://localhost:5000'
).replace(/\/+$/, '');

/** REST uçlarının kökü. Örn: https://api.namines.app/api */
export const API_BASE_URL = `${API_ORIGIN}/api`;

/** SignalR canvas hub adresi. */
export const CANVAS_HUB_URL = `${API_ORIGIN}/hubs/canvas`;

/**
 * Sunucunun döndürdüğü göreli yolu (ör. '/api/docker/download/{id}') tam URL'e çevirir.
 * Zaten mutlak bir URL verilirse olduğu gibi bırakır.
 */
export const toAbsoluteApiUrl = (path: string): string =>
  /^https?:\/\//i.test(path) ? path : `${API_ORIGIN}${path.startsWith('/') ? path : `/${path}`}`;
