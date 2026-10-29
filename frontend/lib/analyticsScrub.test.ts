import { describe, it, expect } from 'vitest';
import { scrubUrl, scrubEvent } from './analyticsScrub';

describe('analyticsScrub', () => {
  it('maskeler join/share token segmentini ve sorguyu düşürür', () => {
    expect(scrubUrl('https://namines.app/join/abc123?x=1')).toBe('https://namines.app/join/:token');
    expect(scrubUrl('https://namines.app/share/tok')).toBe('https://namines.app/share/:token');
    expect(scrubUrl('https://namines.app/canvas?roomId=secret')).toBe('https://namines.app/canvas');
    expect(scrubUrl('/join/abc')).toBe('/join/:token');
  });

  it('URL olmayan değerleri değiştirmez', () => {
    expect(scrubUrl(undefined)).toBeUndefined();
    expect(scrubUrl('')).toBe('');
  });

  it('olay özelliklerini ve $set alanlarını temizler', () => {
    const ev = scrubEvent({
      properties: {
        $current_url: 'https://x.io/share/t0k?roomId=1',
        $pathname: '/share/t0k',
        $referrer: 'https://x.io/join/zzz',
        $set: { $initial_current_url: 'https://x.io/join/q' },
        other: 'keep',
      },
    });
    expect(ev.properties.$current_url).toBe('https://x.io/share/:token');
    expect(ev.properties.$pathname).toBe('/share/:token');
    expect(ev.properties.$referrer).toBe('https://x.io/join/:token');
    expect((ev.properties.$set as Record<string, string>).$initial_current_url).toBe('https://x.io/join/:token');
    expect(ev.properties.other).toBe('keep');
  });
});
