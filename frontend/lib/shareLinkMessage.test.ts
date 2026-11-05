import { describe, expect, it } from 'vitest';
import { shareLinkToast } from './shareLinkMessage';

describe('shareLinkToast', () => {
  it('is a plain success when no link was closed', () => {
    expect(shareLinkToast({ token: 't' })).toEqual({ message: 'Read-only share link copied to clipboard!', type: 'success' });
    expect(shareLinkToast({ token: 't', closedOldestLinks: 0 }).type).toBe('success');
  });

  it('warns and names the count when the cap closed older links', () => {
    const one = shareLinkToast({ token: 't', closedOldestLinks: 1 });
    expect(one.type).toBe('warning');
    expect(one.message).toContain('1 older share link was turned off');
    expect(one.message).toContain('50 active links');

    const many = shareLinkToast({ token: 't', closedOldestLinks: 3 });
    expect(many.message).toContain('3 older share links were turned off');
  });
});
