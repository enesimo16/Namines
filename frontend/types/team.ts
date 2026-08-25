/**
 * Team planı: koltuklar, davet bağlantıları ve ekip etkinliği.
 * Backend: TeamController (api/team).
 */

export interface TeamMember {
  userId: string;
  username: string;
  email: string;
  /** 'Viewer' | 'Editor' | 'Admin' | 'Owner' | 'Billing' */
  role: string;
  joinedAt: string;
  isYou: boolean;
}

export interface PendingInvite {
  id: string;
  role: string;
  createdAt: string;
  expiresAt: string;
}

export interface TeamSeats {
  /** Satın alan dahil TOPLAM koltuk. -1 = sınırsız. */
  total: number;
  /** Üyeler + bekleyen davetler. Bekleyen davet de koltuk tutar. */
  used: number;
  /** -1 = sınırsız. */
  available: number;
}

export interface TeamStatus {
  /** 'Free' | 'Pro' | 'Team' | 'Dev' */
  plan: string;
  /** Ekip arayüzü gösterilsin mi — Free/Pro tek kişilik. */
  teamEnabled: boolean;
  seats: TeamSeats;
  organizationId: string;
  members: TeamMember[];
  pendingInvites: PendingInvite[];
}

/**
 * Yeni üretilen davet. `token` YALNIZCA burada, bir kez döner —
 * sunucuda yalnızca özeti saklanıyor, tekrar gösterilemez.
 */
export interface CreatedInvite {
  id: string;
  token: string;
  role: string;
  expiresAt: string;
}

export interface TeamProject {
  id: string;
  name: string;
  dbType: string;
  updatedAt: string;
  ownerUserId: string;
  ownerName: string;
}
