/**
 * Birden fazla görünümün paylaştığı biçimlendirme yardımcıları.
 *
 * Ayrı bir dosyada olmasının sebebi: `formatSize` önce Vault'ta yazıldı, sonra
 * Ground'un da aynı şeye ihtiyacı oldu. İkinci bir kopya çıkarmak, aynı sayının
 * iki ekranda farklı görünmeye başlaması demekti.
 */

/**
 * Bayt sayısını okunur hâle getirir.
 *
 * 1024 tabanı: yedek boyutu diskte yer kaplayan boyuttur, pazarlama boyutu değil.
 */
export function formatSize(bytes: number): string {
  if (bytes <= 0) return '—';

  const units = ['B', 'KB', 'MB', 'GB', 'TB'];
  let value = bytes;
  let unit = 0;
  while (value >= 1024 && unit < units.length - 1) {
    value /= 1024;
    unit += 1;
  }

  // Bayt bölünmez; büyük birimlerde tek ondalık okunurluğu artırıyor.
  return `${unit === 0 ? value : value.toFixed(1)} ${units[unit]}`;
}
