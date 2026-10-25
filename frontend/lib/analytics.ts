import posthog from 'posthog-js';
import { trackActivity } from './telemetry';

/**
 * 21 §8 — Ürün Analitiği (PostHog) & Olay Taksonomisi.
 *
 * <b>Kota Koruma Stratejisi:</b>
 * PostHog ücretsiz katmanında aylık 1.000.000 etkinlik sınırı vardır.
 * - <b>Mükerrer İstek Filtreleme (Deduplication):</b> Kullanıcıların çift tıklamaları veya
 *   hızlı arayüz tetiklemeleri 1.5 saniyelik önbellek penceresiyle elenir.
 * - <b>Yalnızca Kanonik Olaylar:</b> Gürültülü ara durumlar (hover, focus, her tuş basımı)
 *   asla gönderilmez; yalnızca dönüşüm hunisi ve değer üreten kritik dönüm noktaları izlenir.
 * - <b>Fail-Safe:</b> PostHog anahtarı tanımlanmadığında ya da istemci bloklandığında
 *   uygulama hatasız çalışmaya devam eder ve iç admin telemetrisine (`trackActivity`) yazar.
 */

// 1.5 saniye içinde aynı anahtara sahip mükerrer olayları engelleyen bellek içi pencere
const dedupeCache = new Map<string, number>();
const DEDUPE_WINDOW_MS = 1500;

function shouldThrottle(eventKey: string): boolean {
  const now = Date.now();
  const lastTime = dedupeCache.get(eventKey);
  if (lastTime && now - lastTime < DEDUPE_WINDOW_MS) {
    return true;
  }
  dedupeCache.set(eventKey, now);

  // Bellek sızıntısını önlemek için periyodik temizleme
  if (dedupeCache.size > 200) {
    for (const [key, timestamp] of dedupeCache.entries()) {
      if (now - timestamp > DEDUPE_WINDOW_MS * 2) {
        dedupeCache.delete(key);
      }
    }
  }
  return false;
}

/**
 * PostHog olayını güvenli ve kota korumalı şekilde ateşler.
 */
export function trackEvent(eventName: string, properties?: Record<string, unknown>): void {
  if (typeof window === 'undefined') return;

  const dedupeKey = `${eventName}:${JSON.stringify(properties || {})}`;
  if (shouldThrottle(dedupeKey)) {
    return;
  }

  // 1. PostHog'a gönder (yapılandırılmışsa)
  try {
    if (process.env.NEXT_PUBLIC_POSTHOG_KEY) {
      posthog.capture(eventName, properties);
    }
  } catch {
    // PostHog hatası hiçbir zaman kullanıcı akışını kesintiye uğratmaz
  }

  // 2. İç telemetriye (AdminTelemetryService) non-blocking bildir
  try {
    trackActivity({
      action: eventName,
      module: (properties?.module as string) || 'analytics',
      metadata: properties ? JSON.stringify(properties) : undefined,
    });
  } catch {
    // İç telemetri hatası sessizce yutulur
  }
}

// ─────────────────────────────────────────────────────────────────────────────
// KANONİK DÖNÜŞÜM & YAŞAM DÖNGÜSÜ OLAYLARI (21 §8)
// ─────────────────────────────────────────────────────────────────────────────

export interface SchemaGeneratedPayload {
  source: 'prompt' | 'url' | 'image' | 'voice' | 'import' | 'starter' | 'blank';
  tableCount: number;
  durationMs?: number;
  hasRelations?: boolean;
}

export function trackSchemaGenerated(payload: SchemaGeneratedPayload): void {
  trackEvent('schema_generated', {
    source: payload.source,
    tableCount: payload.tableCount,
    durationMs: payload.durationMs,
    hasRelations: payload.hasRelations,
    module: 'canvas',
  });
}

export interface DatabaseProvisionedPayload {
  provider: string; // 'docker' | 'neon' | 'supabase' vb.
  engine: string;   // 'postgresql' | 'mysql' | 'sqlite' vb.
  mode: string;     // 'ephemeral' | 'persistent' | 'branch'
  durationMs?: number;
  success?: boolean;
}

export function trackDatabaseProvisioned(payload: DatabaseProvisionedPayload): void {
  trackEvent('database_provisioned', {
    provider: payload.provider,
    engine: payload.engine,
    mode: payload.mode,
    durationMs: payload.durationMs,
    success: payload.success ?? true,
    module: 'ground',
  });
}

export interface MigrationAppliedPayload {
  risk: 'safe' | 'warning' | 'critical' | 'unknown';
  environment: 'development' | 'staging' | 'production';
  operationCount?: number;
  durationMs?: number;
}

export function trackMigrationApplied(payload: MigrationAppliedPayload): void {
  trackEvent('migration_applied', {
    risk: payload.risk,
    environment: payload.environment,
    operationCount: payload.operationCount,
    durationMs: payload.durationMs,
    module: 'review',
  });
}

export interface QuotaExceededPayload {
  resource: 'ai_tokens' | 'branches' | 'storage_mb' | 'queries';
  plan?: string;
}

export function trackQuotaExceeded(payload: QuotaExceededPayload): void {
  trackEvent('quota_exceeded', {
    resource: payload.resource,
    plan: payload.plan || 'free',
    module: 'billing',
  });
}

export function trackDdlDownloaded(engine: string, tableCount?: number): void {
  trackEvent('ddl_downloaded', {
    engine,
    tableCount,
    module: 'compile',
  });
}

export function trackConsoleOpened(projectId: string): void {
  trackEvent('console_opened', {
    projectId,
    module: 'console',
  });
}

export function trackConsoleRowCreated(table: string): void {
  trackEvent('console_row_created', {
    table,
    module: 'console',
  });
}

export function trackEjectDownloaded(target: string): void {
  trackEvent('eject_downloaded', {
    target,
    module: 'eject',
  });
}

export function trackErrorShown(surface: string, code?: string, message?: string): void {
  trackEvent('error_shown', {
    surface,
    code,
    message,
    module: 'error',
  });
}

export function trackFeedbackSubmitted(kind: string, rating?: number): void {
  trackEvent('feedback_submitted', {
    kind,
    rating,
    module: 'feedback',
  });
}

export function trackHelpOpened(topic: string): void {
  trackEvent('help_opened', {
    topic,
    module: 'help',
  });
}

// ─────────────────────────────────────────────────────────────────────────────
// KULLANICI KİMLİK & FEATURE FLAG YÖNETİMİ
// ─────────────────────────────────────────────────────────────────────────────

export function identifyUser(params: {
  userId: string;
  email?: string;
  role?: string;
  plan?: string;
}): void {
  if (typeof window === 'undefined') return;
  try {
    if (process.env.NEXT_PUBLIC_POSTHOG_KEY) {
      posthog.identify(params.userId, {
        email: params.email,
        role: params.role || 'user',
        plan: params.plan || 'free',
      });
    }
  } catch {
    // Fail-safe
  }
}

export function resetUser(): void {
  if (typeof window === 'undefined') return;
  try {
    if (process.env.NEXT_PUBLIC_POSTHOG_KEY) {
      posthog.reset();
    }
  } catch {
    // Fail-safe
  }
}

export function isFeatureFlagEnabled(flagKey: string, defaultValue = false): boolean {
  if (typeof window === 'undefined') return defaultValue;
  try {
    if (process.env.NEXT_PUBLIC_POSTHOG_KEY) {
      const isEnabled = posthog.isFeatureEnabled(flagKey);
      return typeof isEnabled === 'boolean' ? isEnabled : defaultValue;
    }
  } catch {
    // Fail-safe
  }
  return defaultValue;
}

export function getFeatureFlagPayload(flagKey: string): unknown {
  if (typeof window === 'undefined') return null;
  try {
    if (process.env.NEXT_PUBLIC_POSTHOG_KEY) {
      return posthog.getFeatureFlagPayload(flagKey);
    }
  } catch {
    // Fail-safe
  }
  return null;
}

// ─────────────────────────────────────────────────────────────────────────────
// NAMINES HUB OLAYLARI (G57 — Faz 4)
// ─────────────────────────────────────────────────────────────────────────────

export function trackHubRepoViewed(owner: string, slug: string): void {
  trackEvent('hub_repo_viewed', {
    owner,
    slug,
    module: 'hub',
  });
}

export function trackHubRepoStarred(projectId: string, isStarred: boolean): void {
  trackEvent('hub_repo_starred', {
    projectId,
    isStarred,
    module: 'hub',
  });
}

export function trackHubRepoForked(projectId: string): void {
  trackEvent('hub_repo_forked', {
    projectId,
    module: 'hub',
  });
}

export function trackHubIssueCreated(projectId: string, issueNumber: number): void {
  trackEvent('hub_issue_created', {
    projectId,
    issueNumber,
    module: 'hub',
  });
}

export function trackHubGithubSync(projectId: string, repo: string): void {
  trackEvent('hub_github_sync', {
    projectId,
    repo,
    module: 'hub',
  });
}
