/**
 * Desk'in gezinme modeli — `AppShell` (sol panel) ile `page.tsx` (yönlendirme)
 * ve `Desk.tsx` (içerik) arasındaki TEK sözleşme.
 *
 * Ayrı bir dosyada olmasının sebebi döngüsel import'u önlemek: kabuk içeriği,
 * içerik de kabuğun görünüm tipini tanımak zorunda.
 */

export type DeskView =
  | 'overview'      // proje listesi / karşılama panosu (proje seçilmemişken tek geçerli görünüm)
  | 'canvas'
  | 'data'
  | 'deployments'
  | 'logs'
  | 'analytics'
  | 'sql'
  | 'apikeys'
  | 'members';

/** Proje bağlamı gerektiren görünümler — proje seçilmeden açılamazlar. */
export const PROJECT_VIEWS: DeskView[] = [
  'canvas', 'data', 'deployments', 'logs', 'analytics', 'sql', 'apikeys', 'members',
];

/** Üst şeritteki ekmek kırıntısında ve sayfa başlığında kullanılan adlar. */
export const VIEW_LABELS: Record<DeskView, string> = {
  overview: 'Projeler',
  canvas: 'Şema',
  data: 'Veri',
  deployments: 'Sürümler',
  logs: 'Kayıtlar',
  analytics: 'Analitik',
  sql: 'SQL konsolu',
  apikeys: 'API anahtarları',
  members: 'Ekip',
};

/**
 * JWT'nin gövdesinden kullanıcı adını/e-postasını okur — YALNIZCA GÖSTERİM
 * için. İmza burada DOĞRULANMIYOR ve doğrulanmamalı: yetkiyi her istekte
 * sunucu doğruluyor (Desk'in kendi yetki kararı yok). Burada yapılan tek şey,
 * profil menüsünde "kim olarak giriş yapıldı" bilgisini göstermek.
 */
export interface SessionUser { name: string | null; email: string | null; }

export function decodeSessionUser(token: string): SessionUser {
  try {
    const payload = token.split('.')[1];
    if (!payload) return { name: null, email: null };
    // base64url → base64, sonra UTF-8 çözümü (Türkçe karakterli adlar bozulmasın).
    const base64 = payload.replace(/-/g, '+').replace(/_/g, '/');
    const json = decodeURIComponent(
      atob(base64)
        .split('')
        .map(c => '%' + c.charCodeAt(0).toString(16).padStart(2, '0'))
        .join(''),
    );
    const claims = JSON.parse(json) as Record<string, unknown>;

    // .NET'in ürettiği JWT'de klasik XML şema URI'leri kullanılıyor
    // (ClaimTypes.Name / ClaimTypes.Email), kısa adlar da olabilir.
    const pick = (...keys: string[]): string | null => {
      for (const k of keys) {
        const v = claims[k];
        if (typeof v === 'string' && v.length > 0) return v;
      }
      return null;
    };

    return {
      name: pick(
        'http://schemas.xmlsoap.org/ws/2005/05/identity/claims/name',
        'unique_name', 'name', 'preferred_username',
      ),
      email: pick(
        'http://schemas.xmlsoap.org/ws/2005/05/identity/claims/emailaddress',
        'email',
      ),
    };
  } catch {
    return { name: null, email: null };
  }
}

/** Profil baloncuğundaki iki harf — ad yoksa e-postadan, o da yoksa "NA". */
export function initialsOf(user: SessionUser): string {
  const source = user.name ?? user.email ?? '';
  const clean = source.trim();
  if (!clean) return 'NA';
  const parts = clean.split(/[\s._-]+/).filter(Boolean);
  if (parts.length >= 2) return (parts[0][0] + parts[1][0]).toUpperCase();
  return clean.slice(0, 2).toUpperCase();
}
