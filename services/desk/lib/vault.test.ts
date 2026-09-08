import { describe, expect, it } from 'vitest';
import { formatSize, formatDuration } from './vault';

describe('formatSize', () => {
  it('bayt altinda ondalik gostermez', () => {
    // Bayt bölünmez; "512.0 B" yanlış bir kesinlik izlenimi verirdi.
    expect(formatSize(512)).toBe('512 B');
  });

  it('siniri gectiginde bir ust birime gecer', () => {
    expect(formatSize(1024)).toBe('1.0 KB');
    expect(formatSize(1023)).toBe('1023 B');
  });

  it('buyuk boyutlari dogru birimde gosterir', () => {
    expect(formatSize(1024 * 1024)).toBe('1.0 MB');
    expect(formatSize(1024 * 1024 * 1024 * 3)).toBe('3.0 GB');
  });

  it('bos yedegi tire ile gosterir', () => {
    // Boyut 0 gerçek bir yedek değil (henüz sürüyor ya da başarısız oldu);
    // "0 B" yazmak onu geçerli bir yedek gibi gösterirdi.
    expect(formatSize(0)).toBe('—');
    expect(formatSize(-1)).toBe('—');
  });
});

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
