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
        if (!cancelled) setError(err instanceof MembersError ? err.message : 'Üyeler okunamadı.');
      }
    })();
    return () => { cancelled = true; };
  }, [session]);

  return (
    <div className="page">
      <PageHead
        title="Ekip"
        desc={<>
          Bu projeye erişebilen kişiler ve rolleri. <b>Salt-okunur görüntü:</b> davet
          gönderme, rol değiştirme ve üye çıkarma ana Namines uygulamasında yapılır —
          aynı işi iki yerde yapmak &quot;hangi ekran yetkili&quot; sorusunu bulanıklaştırırdı.
        </>}
      />

      {error && <div className="notice notice-error">{error}</div>}

      {!members ? (
        <div className="empty">Yükleniyor…</div>
      ) : members.length === 0 ? (
        <EmptyState
          title="Bu projede başka üye yok"
          description={
            <>
              Ekip arkadaşlarınızı davet ederek şema değişikliklerini birlikte inceleyebilir ve
              onaylayabilirsiniz. Riskli değişiklikler için ikinci bir onay, tek başına
              çalışırken alınamayan bir güvencedir.
            </>
          }
        />
      ) : (
        <div className="grid-wrap">
          <table>
            <thead>
              <tr><th>Kullanıcı</th><th>E-posta</th><th>Rol</th><th>Katılma</th></tr>
            </thead>
            <tbody>
              {members.map(m => (
                <tr key={m.userId}>
                  <td>{m.username ?? '—'}</td>
                  <td>{m.email ?? '—'}</td>
                  <td>{roleLabel(m.role)}</td>
                  <td>{m.joinedAt ? new Date(m.joinedAt).toLocaleString('tr-TR') : '—'}</td>
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
    case 'Owner': return 'Sahip';
    case 'Admin': return 'Yönetici';
    case 'Editor': return 'Düzenleyici';
    case 'Viewer': return 'Görüntüleyici';
    case 'Billing': return 'Faturalama';
    default: return role;
  }
}
