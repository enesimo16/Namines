// Backend: Namines.Core/Models/Auth/Organization.cs — OrgRole
//
// DİKKAT: API rolleri STRING olarak döndürüyor ("Owner", "Editor") çünkü
// Program.cs'te global bir JsonStringEnumConverter kayıtlı. Bu tip başlangıçta
// sayısal enum olarak yazılmıştı ve `m.role === OrgRole.Editor` karşılaştırmaları
// sessizce hep false dönüyordu (rol menüsü ve "üye yönetebilir mi" kontrolü
// bozuktu). String union'a çevrildi — tel üzerindeki gerçek biçim bu.
export type OrgRole = 'Viewer' | 'Editor' | 'Admin' | 'Owner' | 'Billing';

export const OrgRole = {
  Viewer: 'Viewer',
  Editor: 'Editor',
  Admin: 'Admin',
  Owner: 'Owner',
  Billing: 'Billing',
} as const;

export const ORG_ROLE_LABEL: Record<OrgRole, string> = {
  Viewer: 'Viewer',
  Editor: 'Editor',
  Admin: 'Admin',
  Owner: 'Owner',
  Billing: 'Billing',
};

/** 05 §6 yetki tablosunun kullanıcıya gösterilen özeti. */
export const ORG_ROLE_HINT: Record<OrgRole, string> = {
  Viewer: 'Reads only — cannot edit the schema or vote on reviews.',
  Editor: 'Edits the schema, opens change requests and votes on them.',
  Admin: 'Everything an editor can do, plus managing team members.',
  Owner: 'Full control, including billing and deleting the workspace.',
  Billing: 'Billing only — no access to the schema or reviews.',
};

/** Rol değiştirme/ekleme menüsünde gösterilenler (Owner devri ayrı bir akış). */
export const ASSIGNABLE_ROLES: OrgRole[] = ['Viewer', 'Editor', 'Admin'];

/** Oy verebilen roller — backend CountVotingMembersAsync ile birebir aynı olmalı. */
export const VOTING_ROLES: OrgRole[] = ['Editor', 'Admin', 'Owner'];

export interface ProjectMember {
  userId: string;
  username: string | null;
  email: string | null;
  role: OrgRole;
  joinedAt: string;
}
