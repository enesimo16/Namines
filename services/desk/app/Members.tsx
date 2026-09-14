'use client';

import { useEffect, useState } from 'react';
import EmptyState from './EmptyState';
import { type DeskSession } from '../lib/api';
import { fetchMembers, MembersError, type ProjectMember } from '../lib/members';
import PageHead from './PageHead';

/**
 * Namines Desk v2 §E2.1 — ekip üyeleri, salt-okunur.
 *
 * Davet gönderme, rol değiştirme ve üye çıkarma BİLİNÇLİ OLARAK burada yok —
 * bunlar ana uygulamada kalıyor (lib/members.ts'deki gerekçe).
 */
export default function Members({ session }: { session: DeskSession }) {
  const [members, setMembers] = useState<ProjectMember[] | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    (async () => {
      try {
        const list = await fetchMembers(session);
        if (!cancelled) { setMembers(list); setError(null); }
      } catch (err) {
        if (!cancelled) setError(err instanceof MembersError ? err.message : 'Could not load members.');
      }
    })();
    return () => { cancelled = true; };
  }, [session]);

  return (
    <div className="page">
      <PageHead
        title="Team"
        desc={<>
          People with access to this project and their roles. <b>Read-only view:</b> inviting,
          changing roles, and removing members happens in the main Namines app.
        </>}
      />

      {error && <div className="notice notice-error">{error}</div>}

      {!members ? (
        <div className="empty">Loading…</div>
      ) : members.length === 0 ? (
        <EmptyState
          title="No other members on this project"
          description="Invite teammates to review and approve schema changes together — a second approval catches what working alone can't."
        />
      ) : (
        <div className="grid-wrap">
          <table>
            <thead>
              <tr><th>User</th><th>Email</th><th>Role</th><th>Joined</th></tr>
            </thead>
            <tbody>
              {members.map(m => (
                <tr key={m.userId}>
                  <td className="nowrap">{m.username ?? '—'}</td>
                  <td className="nowrap">{m.email ?? '—'}</td>
                  <td className="nowrap">{roleLabel(m.role)}</td>
                  <td className="nowrap">{m.joinedAt ? new Date(m.joinedAt).toLocaleString('en-US') : '—'}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}

function roleLabel(role: ProjectMember['role']): string {
  switch (role) {
    case 'Owner': return 'Owner';
    case 'Admin': return 'Admin';
    case 'Editor': return 'Editor';
    case 'Viewer': return 'Viewer';
    case 'Billing': return 'Billing';
    default: return role;
  }
}
