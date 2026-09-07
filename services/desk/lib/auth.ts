/**
 * Namines Desk kimlik doğrulaması — ana Namines hesabıyla oturum (JWT).
 *
 * v0.1'deki ham Gateway API anahtarının yerini alıyor (01-KIMLIK-VE-OTURUM.md).
 * Anahtar kavramı Desk'in arayüzünden tamamen kalkıyor: kullanıcı e-posta +
 * parola ile ana Namines hesabına girer, `POST /api/auth/login`'den JWT alır.
 *
 * `sessionStorage`'da tutuluyor, `localStorage`'da DEĞİL — v0.1'deki anahtar
 * için verilen kararla aynı gerekçe: bu bir erişim belgesi, sekme kapanınca
 * kalmamalı. Paylaşılan bir makinede kalıcı saklamak, kapatıldığı sanılan bir
 * oturumu açık bırakır.
 */

const API = process.env.NAMINES_API ?? 'http://localhost:5000';
const TOKEN_KEY = 'namines-desk-token';

export class AuthError extends Error {
  constructor(message: string, readonly status: number) {
    super(message);
  }
}

export interface DeskProject {
  id: string;
  name: string;
  dbType: string | null;
  isMine: boolean;
  ownerName: string | null;
  /** Şifreli bağlantı kayıtlı mı — bağlantının KENDİSİ asla gelmez, yalnızca bu bayrak. */
  hasConnection: boolean;
  /** Bağlı olunan CANLI veritabanının motoru — `dbType` (tasarım motoru) ile ayrışabilir. */
  connectionDbType: string | null;
  /** SchemaJson'dan sayılır; ayrıştırılamıyorsa null (uydurma 0 değil). */
  tableCount: number | null;
  updatedAt: string | null;
  /**
   * Kullanıcının canvas'ta ÇİZDİĞİ tasarım — ana uygulamanın gerçek kaynağı.
   * D3 (Canvas) drift tespiti için: canlı şema ile bu ikisi ayrışmış olabilir
   * (namines_desk/03-CANVAS.md §2). Ham JSON string olarak taşınır, Desk kendi
   * ayrıştırıcısıyla okur (lib/designSchema.ts) — DatabaseSchema tipi
   * KOPYALANMIYOR, yalnızca ihtiyaç duyulan iki alan (tablo id + ad) okunuyor.
   */
  schemaJson: string | null;
  /** Tablo id'sine göre {x,y} — yalnızca Canvas'ta yerleşim için okunur, hiç yazılmaz. */
  nodePositionsJson: string | null;
  /** Namines Desk v2 §E4.2 — yalnızca Owner ham SQL çalıştırabilir/açabilir. */
  isOwner: boolean;
  /** Proje sahibi SQL konsolunu bu proje için açık mı bırakmış. */
  allowDeskSql: boolean;
}

/** Namines Desk'in bildiği canlı veritabanı motorları — DatabaseType.cs ile birebir. */
export const DESK_DB_ENGINES = ['PostgreSQL', 'MSSQL', 'MySQL', 'MariaDB', 'Oracle', 'SQLite'] as const;

export function getToken(): string | null {
  try { return sessionStorage.getItem(TOKEN_KEY); } catch { return null; }
}

export function setToken(token: string): void {
  try { sessionStorage.setItem(TOKEN_KEY, token); } catch { /* gizli mod */ }
}

export function clearToken(): void {
  try { sessionStorage.removeItem(TOKEN_KEY); } catch { /* gizli mod */ }
}

export async function login(email: string, password: string): Promise<string> {
  const res = await fetch(`${API}/api/auth/login`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ email, password }),
    cache: 'no-store',
  });

  if (!res.ok) {
    let message = 'E-posta veya parola hatalı.';
    try {
      const body = await res.json();
      if (body?.message ?? body?.Message) message = body.message ?? body.Message;
    } catch { /* gövde JSON değilse varsayılan mesaj kalır */ }
    throw new AuthError(message, res.status);
  }

  const body = await res.json();
  const token = body?.token ?? body?.Token;
  if (!token) throw new AuthError('Sunucu bir oturum belgesi döndürmedi.', 500);
  return token as string;
}

/**
 * Kullanıcının erişebildiği projeler — D1'in "proje seçici" bunun üstüne
 * kurulu. Kart ızgarası, bağlantı durumu ve Import akışı D2'nin kapsamı
 * (02-PROJECTS.md); burada yalnızca isim + id yeterli.
 */
export async function fetchProjects(token: string): Promise<DeskProject[]> {
  const res = await fetch(`${API}/api/auth/projects`, {
    headers: { Authorization: `Bearer ${token}` },
    cache: 'no-store',
  });

  if (res.status === 401) throw new AuthError('Oturum süresi doldu.', 401);
  if (!res.ok) throw new AuthError('Projeler okunamadı.', res.status);

  const raw = await res.json();
  return (raw as Array<Record<string, unknown>>).map(p => ({
    id: String(p.id ?? p.Id),
    name: String(p.name ?? p.Name),
    dbType: (p.dbType ?? p.DbType) as string | null,
    isMine: Boolean(p.isMine ?? p.IsMine),
    ownerName: (p.ownerName ?? p.OwnerName) as string | null,
    hasConnection: Boolean(p.hasConnection ?? p.HasConnection),
    connectionDbType: (p.connectionDbType ?? p.ConnectionDbType) as string | null,
    tableCount: (p.tableCount ?? p.TableCount) as number | null,
    updatedAt: (p.updatedAt ?? p.UpdatedAt) as string | null,
    schemaJson: (p.schemaJson ?? p.SchemaJson) as string | null,
    nodePositionsJson: (p.nodePositionsJson ?? p.NodePositionsJson) as string | null,
    isOwner: Boolean(p.isOwner ?? p.IsOwner),
    allowDeskSql: Boolean(p.allowDeskSql ?? p.AllowDeskSql),
  }));
}

/**
 * Namines Desk'in "Import" akışı (02-PROJECTS.md §3): bir Namines projesine
 * canlı veritabanı bağlantısı bağlamak. Sunucu KAYDETMEDEN ÖNCE gerçekten
 * bağlanıp şemayı okumayı dener (GatewayKeyController.SetProjectConnection) —
 * yanlış bir bağlantı dizesi sessizce kaydedilmez, sebebiyle birlikte reddedilir.
 */
export async function setProjectConnection(
  token: string, projectId: string, connectionString: string, dbType: string,
): Promise<void> {
  const res = await fetch(`${API}/api/gateway/keys/project/${encodeURIComponent(projectId)}/connection`, {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json', Authorization: `Bearer ${token}` },
    body: JSON.stringify({ connectionString, dbType }),
    cache: 'no-store',
  });

  if (!res.ok) {
    let message = 'Bağlantı kaydedilemedi.';
    try {
      const body = await res.json();
      if (body?.error) message = body.error;
    } catch { /* gövde JSON değilse varsayılan mesaj kalır */ }
    throw new AuthError(message, res.status);
  }
}
