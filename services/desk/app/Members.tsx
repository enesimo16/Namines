'use client';

import { useEffect, useState } from 'react';
import EmptyState from './EmptyState';
import { type DeskSession } from '../lib/api';
import { fetchMembers, MembersError, type ProjectMember } from '../lib/members';
import PageHead from './PageHead';
import { useI18n, type MessageKey } from '../lib/i18n';
import { rich } from '../lib/rich';

/**
 * Namines Desk v2 §E2.1 — ekip üyeleri, salt-okunur.
 *
 * Davet gönderme, rol değiştirme ve üye çıkarma BİLİNÇLİ OLARAK burada yok —
 * bunlar ana uygulamada kalıyor (lib/members.ts'deki gerekçe).
 */
export default function Members({ session }: { session: DeskSession }) {
  const { t, dt } = useI18n();
  const [members, setMembers] = useState<ProjectMember[] | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    (async () => {
      try {
        const list = await fetchMembers(session);
        if (!cancelled) { setMembers(list); setError(null); }
      } catch (err) {
        if (!cancelled) setError(err instanceof MembersError ? err.message : t('members.loadFailed'));
      }
    })();
    return () => { cancelled = true; };
  }, [session, t]);

  return (
    <div className="page">
      <PageHead
        title={t('nav.views.members')}
        desc={rich(t('members.desc'))}
      />

      {error && <div className="notice notice-error">{error}</div>}

      {!members ? (
        <div className="empty">{t('common.loading')}</div>
      ) : members.length === 0 ? (
        <EmptyState
          title={t('members.emptyTitle')}
          description={t('members.emptyDesc')}
        />
      ) : (
        <div className="grid-wrap">
          <table>
            <thead>
              <tr><th>{t('members.user')}</th><th>{t('members.email')}</th><th>{t('members.role')}</th><th>{t('members.joined')}</th></tr>
            </thead>
            <tbody>
              {members.map(m => (
                <tr key={m.userId}>
                  <td className="nowrap">{m.username ?? '—'}</td>
                  <td className="nowrap">{m.email ?? '—'}</td>
                  <td className="nowrap">{m.role in ROLE_KEYS ? t(ROLE_KEYS[m.role]) : m.role}</td>
                  <td className="nowrap">{m.joinedAt ? dt(m.joinedAt) : '—'}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}

// Sunucu bilinmeyen bir rol döndürürse (yeni bir rol eklendiyse) ham adı gösterilir.
const ROLE_KEYS: Record<string, MessageKey> = {
  Owner: 'members.roles.Owner',
  Admin: 'members.roles.Admin',
  Editor: 'members.roles.Editor',
  Viewer: 'members.roles.Viewer',
  Billing: 'members.roles.Billing',
};
