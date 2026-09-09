import { describe, expect, it, vi, afterEach } from 'vitest';
import { daysUntilPurge } from './ground';

/**
 * `daysUntilPurge` — kullanıcıya "geri almak için kaç günün var" diyen sayı.
 *
 * Yanlış hesaplanması, kullanıcının hâlâ kurtarabileceği bir veritabanını
 * kurtarılamaz sanması (ya da tersi) demek.
 */
describe('daysUntilPurge', () => {
  const now = new Date('2026-09-09T12:00:00Z');

  afterEach(() => vi.useRealTimers());

  function at(iso: string) {
    vi.useFakeTimers();
    vi.setSystemTime(new Date(iso));
  }

  it('tam pencereyi gun olarak verir', () => {
    at(now.toISOString());
    expect(daysUntilPurge('2026-09-09T12:00:00Z', 7)).toBe(7);
  });

  it('gecen sureyi duser', () => {
    at('2026-09-12T12:00:00Z');
    expect(daysUntilPurge('2026-09-09T12:00:00Z', 7)).toBe(4);
  });

  it('kalan kismi gunu YUKARI yuvarlar', () => {
    // 23 saat kalmisken "0 gun" demek, kullaniciya hala geri alabilecegi bir
    // seyi kaybettigini dusundururdu.
    at('2026-09-15T13:00:00Z');
    expect(daysUntilPurge('2026-09-09T12:00:00Z', 7)).toBe(1);
  });

  it('pencere dolduysa sifir doner', () => {
    at('2026-09-20T12:00:00Z');
    expect(daysUntilPurge('2026-09-09T12:00:00Z', 7)).toBe(0);
  });

  it('gecersiz tarihi sifir sayar', () => {
    at(now.toISOString());
    // Negatif ya da NaN bir sayi gostermek, kullaniciyi yorumlamak zorunda
    // birakirdi; sifir "artik guvenme" demek ve dogru taraf bu.
    expect(daysUntilPurge('gecersiz', 7)).toBe(0);
  });
});
