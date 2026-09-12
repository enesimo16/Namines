import type { SchemaColumn, SchemaRelation, SchemaTable } from './schema';

/**
 * Sunucu yanıtlarının tipleri (FE-008 / B-57).
 *
 * **Neden ayrı dosya:** `services/api.ts` 13 uçta `Promise<any>` dönüyordu.
 * `any` dönen bir istemci, çağrı noktasındaki her yazım hatasını sessizce
 * kabul eder — `data.tableCount` yerine `data.tablecount` yazmak derlenir ve
 * `undefined` gelir. Bu dosyadaki tipler o hataları derleme anına taşıyor.
 *
 * **Alanlar İSTEĞE BAĞLI tutuldu** çünkü bu şekiller sunucunun anonim
 * projeksiyonlarından geliyor (`new { ... }`) ve orada bir alanın adı
 * değişirse burada `undefined` gelir. Zorunlu işaretlemek, olmayan bir
 * garantiyi varmış gibi göstermek olurdu.
 */

export interface AuthUser {
  username: string;
  email: string;
  type: 'individual' | 'corporate';
  companyName?: string | null;
}

export interface QuotaInfo {
  dailyLimit: number;
  used: number;
  remaining: number;
  resetAt: string;
  plan?: string;
}

export interface AuthResponse {
  token: string;
  user: AuthUser;
  quota?: QuotaInfo;
}

/** `POST /auth/sync` — kaç projenin kaydedildiğini söyler. */
export interface SyncResult {
  message?: string;
  savedCount?: number;
}

/**
 * `GET /auth/projects` satırı.
 *
 * `rowVersion` optimistic concurrency belirteci (REL-003a): istemci onu
 * saklayıp `sync`'te geri göndermek zorunda, yoksa çakışma koruması
 * devreye girmez.
 */
export interface CloudProjectDto {
  id: string;
  name: string;
  dbType: string;
  // Bu dordu sunucunun projeksiyonunda KOSULSUZ var (AuthController
  // .GetProjects); istege bagli isaretlemek, olmayan bir belirsizligi
  // cagri noktalarina yayardi (`CrossDatabasePanel` bunlari zorunlu bekliyor).
  schemaJson: string;
  nodePositionsJson: string;
  createdAt: string;
  updatedAt: string;
  tableCount?: number | null;
  hasConnection?: boolean;
  connectionDbType?: string | null;
  organizationId?: string | null;
  allowDeskSql?: boolean;
  ownerUserId?: string;
  ownerName?: string;
  isMine?: boolean;
  isOwner?: boolean;
  rowVersion?: number;
  autoApproveSafeChanges?: boolean;
}

/** `POST /auth/sync` gövdesindeki tek proje. */
export interface SyncProjectPayload {
  id: string;
  name: string;
  dbType: string;
  schemaJson: string;
  nodePositionsJson: string;
  rowVersion?: number;
}

export interface ProfileDto {
  email?: string;
  fullName?: string;
  companyName?: string;
  githubUrl?: string;
  linkedinUrl?: string;
  websiteUrl?: string;
  twitterUrl?: string;
  bio?: string;
  location?: string;
}

export interface SubscriptionStatusDto {
  status?: string;
  plan?: string;
  currentPeriodEnd?: string;
}

/**
 * Linter bulgusu.
 *
 * Şekil UYDURULMADI: `app/demo/page.tsx` bu yanıtı zaten çalışır hâlde
 * tüketiyordu ve alan adları oradan alındı (`tableId`/`columnId`, `table`/
 * `column` DEĞİL). İlk yazdığım tahmin yanlıştı ve tsc onu yakaladı.
 *
 * `severity` hem sayı hem metin olabiliyor — sunucu sürümüne göre değişiyor,
 * o yüzden birleşim tip.
 */
export interface LintMessage {
  severity: number | string;
  message: string;
  tableId?: string | null;
  columnId?: string | null;
  code?: string;
}

export interface LintResult {
  messages?: LintMessage[];
}

/**
 * DBA analizi bulgusu (`/aidba/analyze`).
 *
 * Şekil `hooks/useAIDba.ts`'ten alındı — orada zaten çalışır hâlde
 * tanımlıydı. Benim ilk tahminim (`id`, `title`, `table`) YANLIŞTI ve tsc
 * yakaladı. Tek kaynak burası; kanca bunu yeniden ihraç ediyor.
 *
 * `severity` SAYISAL: 0 Info, 1 Warning, 2 Error.
 */
export interface DbaIssue {
  ruleId: string;
  tableName: string;
  columnName?: string;
  severity: 0 | 1 | 2;
  message: string;
  suggestion?: string;
  source: string;
  category?: 'Performance' | 'Security' | 'FinOps';
}

export interface DbaAnalysisResult {
  issues?: DbaIssue[];
  totalScore?: number;
  overallAssessment?: string;
}

/**
 * Test verisi üretimi (`/smartseed/generate`).
 *
 * Şekil `components/compile/SmartSeedPanel.tsx`'in zaten tükettiği alanlardan
 * alındı — ilk tahminim (`rowCount`, `tables`) yanlıştı ve tsc yakaladı.
 */
export interface SmartSeedResult {
  sqlScript: string;
  detectedDomain: string;
  tableRowCounts: Record<string, number>;
  estimatedSizeBytes: number;
}

/** `reviseSchema` girdisi — AI'a gönderilen seçili parçalar. */
export type ReviseSelection = SchemaTable | SchemaColumn;
export type ReviseRelations = SchemaRelation[];
