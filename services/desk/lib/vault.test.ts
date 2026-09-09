import { describe, expect, it } from 'vitest';
import { formatDuration } from './vault';

describe('formatDuration', () => {
  it('bitmemis is icin null doner', () => {
    expect(formatDuration('2026-01-01T00:00:00Z', null)).toBeNull();
  });

  it('milisaniye, saniye ve dakikayi ayirir', () => {
    expect(formatDuration('2026-01-01T00:00:00.000Z', '2026-01-01T00:00:00.400Z')).toBe('400 ms');
    expect(formatDuration('2026-01-01T00:00:00Z', '2026-01-01T00:00:08Z')).toBe('8.0 sn');
    expect(formatDuration('2026-01-01T00:00:00Z', '2026-01-01T00:02:05Z')).toBe('2 dk 5 sn');
  });

  it('tutarsiz zaman damgalarini gostermez', () => {
    // Bitiş, başlangıçtan önce olamaz. Negatif bir süre yazmak, saat kaymasını
    // kullanıcının yorumlaması gereken bir veri hâline getirirdi.
    expect(formatDuration('2026-01-01T00:00:10Z', '2026-01-01T00:00:00Z')).toBeNull();
    expect(formatDuration('gecersiz', '2026-01-01T00:00:00Z')).toBeNull();
  });
});
