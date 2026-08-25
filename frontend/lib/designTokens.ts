/**
 * Tasarım token'larını JS tarafında okumak için tek geçit.
 *
 * <b>Neden gerekli:</b> FRONTEND.md §4 ham hex yazmayı yasaklıyor ve bunu
 * Tailwind utility'leriyle çözmek çoğu yerde mümkün. Ama bazı yerler bir
 * className kabul etmiyor, gerçek bir renk DEĞERİ istiyor:
 *
 * - React Flow'un `color` / `nodeColor` gibi prop'ları
 * - `<svg fill="...">` (üçüncü taraf marka logoları hariç)
 * - Canvas'ı PNG'ye basan export (`toPng({ backgroundColor })`)
 *
 * Bu noktalarda hex'i elle yazmak, token'ın ikinci bir kopyasını yaratıyordu:
 * `globals.css`'te renk değişince component sessizce eski renkte kalıyordu.
 * Burada değer CSS'ten OKUNUYOR, yani tek doğruluk kaynağı hâlâ `globals.css`.
 *
 * <b>SSR notu:</b> sunucuda `document` yok. O durumda fallback dönüyor —
 * fallback'ler `globals.css`'teki değerlerin aynısı ve yalnızca ilk boyamada
 * geçerli; istemcide hemen gerçek değere dönüyor. Fallback'siz bırakmak,
 * sunucu tarafında `undefined` renk üretip React Flow'u kırıyordu.
 */

/** `globals.css` ile senkron tutulan yedek değerler (yalnızca SSR anında kullanılır). */
const FALLBACKS: Record<string, string> = {
  '--color-bg-base': '#05070c',
  '--color-surface-700': '#10141d',
  '--color-accent': '#3c4a6b',
  '--color-accent-hover': '#4c5c82',
  '--color-line-strong': 'rgba(231, 233, 238, 0.16)',
  '--color-content-subtle': '#7a8194',
  '--color-content-primary': '#e7e9ee',
  '--color-danger': '#b8544b',
  '--color-danger-text': '#e08787',
  '--color-success': '#4b8a6f',
};

/**
 * Bir tasarım token'ının hesaplanmış değerini döndürür.
 *
 * @param name `--color-accent` gibi tam token adı.
 */
export function token(name: string): string {
  if (typeof document === 'undefined') return FALLBACKS[name] ?? 'transparent';

  const value = getComputedStyle(document.documentElement).getPropertyValue(name).trim();
  return value || FALLBACKS[name] || 'transparent';
}

/**
 * Kullanıcının tabloya atayabildiği renk paleti.
 *
 * <b>Bunlar token DEĞİL, veri.</b> Kullanıcının seçtiği bir tablo rengi bir
 * arayüz kararı değil, kullanıcının kendi verisi — şemayla birlikte kaydediliyor.
 * Token'a bağlamak, tema değişince kullanıcının kaydettiği rengin altından
 * zemin çekmek olurdu.
 *
 * Yine de paletin geri kalanıyla uyumlu kalsınlar diye hepsi desatüre seçildi
 * (FRONTEND.md §2'deki "parlak renk yok" kuralı).
 */
export const TABLE_SWATCHES: readonly (string | undefined)[] = [
  undefined,   // renk yok — varsayılan
  '#4c5c82',   // lacivert
  '#7a6a9e',   // mor-gri
  '#a56b8a',   // gül
  '#a6534f',   // kiremit
  '#5a6b7a',   // arduvaz
  '#4b8a6f',   // yeşil
  '#4a7f96',   // camgöbeği
  '#7a8194',   // gri
] as const;
