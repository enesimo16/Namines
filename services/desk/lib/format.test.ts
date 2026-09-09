import { describe, expect, it } from 'vitest';
import { formatSize } from './format';

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
