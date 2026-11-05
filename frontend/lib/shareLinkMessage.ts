/**
 * Paylaşım bağlantısı oluşturulunca gösterilecek bildirim. Sunucu proje başına en fazla 50 etkin bağlantı
 * tutar; tavan aşılınca en eski bağlantıları kapatır ve kaç tanesini kapattığını `closedOldestLinks` ile
 * söyler. Sahibi bundan habersiz kalırsa, eski bağlantıyı kullanan biri neden açılmadığını bilemez (BL-7).
 */
export const MAX_ACTIVE_SHARE_LINKS = 50;

export interface ShareLinkResult {
  token: string;
  closedOldestLinks?: number;
}

export function shareLinkToast(result: ShareLinkResult): { message: string; type: 'success' | 'warning' } {
  const closed = result.closedOldestLinks ?? 0;
  if (closed <= 0) {
    return { message: 'Read-only share link copied to clipboard!', type: 'success' };
  }
  const links = closed === 1 ? '1 older share link was' : `${closed} older share links were`;
  return {
    message:
      `Read-only share link copied to clipboard. ${links} turned off, ` +
      `because a project can have at most ${MAX_ACTIVE_SHARE_LINKS} active links.`,
    type: 'warning',
  };
}
