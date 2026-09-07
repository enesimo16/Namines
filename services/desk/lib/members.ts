import type { DeskSession } from './api';

/**
 * Namines Desk v2 §E2.1 — ekip üyeleri, SALT-OKUNUR görüntü.
 *
 * Backend'de zaten hazır: `GET /api/project/{projectId}/members`
 * (ProjectMemberController.List) — herhangi bir rol görebilir (CanViewAsync).
 * Davet gönderme / rol değiştirme / çıkarma BİLİNÇLİ OLARAK burada YOK —
 * bunlar ana uygulamada kalıyor (aynı dosyanın kendi yorumu: e-posta
 * davetli akış e-posta altyapısı gelene kadar zaten yok, ve rol
 * değişikliği/kaldırma "Destructive/Breaking" onay akışına bağlı — Desk
 * bunu deterministik CRUD paneli olarak taklit etmemeli).
 */

const API = process.env.NAMINES_API ?? 'http://localhost:5000';

export class MembersError extends Error {
  constructor(message: string, readonly status: number) { super(message); }
}

/** OrgRole enum'u (Namines.Core.Models.Auth) ile birebir — string olarak gelir. */
export type OrgRole = 'Viewer' | 'Editor' | 'Admin' | 'Owner' | 'Billing';

export interface ProjectMember {
  userId: string;
  username: string | null;
  email: string | null;
  role: OrgRole;
  joinedAt: string;
}

export async function fetchMembers(session: DeskSession): Promise<ProjectMember[]> {
  const res = await fetch(`${API}/api/project/${encodeURIComponent(session.projectId)}/members`, {
    headers: { Authorization: `Bearer ${session.token}` },
    cache: 'no-store',
  });
  if (!res.ok) {
    let message = `İstek başarısız (${res.status}).`;
    try {
      const body = await res.json();
      if (body?.error) message = body.error;
    } catch { /* gövde JSON değilse varsayılan mesaj kalır */ }
    throw new MembersError(message, res.status);
  }
  const raw = await res.json() as Array<{
    userId: string; username?: string | null; email?: string | null; role: OrgRole; joinedAt: string;
    UserId?: string; Username?: string | null; Email?: string | null; Role?: OrgRole; JoinedAt?: string;
  }>;
  // Backend anonim tip alan adları PascalCase de gelebilir (JSON büyük/küçük
  // harf duyarsızlığına .NET'in varsayılan ayarları güvenmiyor olabilir) —
  // auth.ts'deki `p.isOwner ?? p.IsOwner` örneğiyle aynı savunmacı desen.
  return raw.map(m => ({
    userId: m.userId ?? m.UserId ?? '',
    username: m.username ?? m.Username ?? null,
    email: m.email ?? m.Email ?? null,
    role: m.role ?? m.Role ?? 'Viewer',
    joinedAt: m.joinedAt ?? m.JoinedAt ?? '',
  }));
}
