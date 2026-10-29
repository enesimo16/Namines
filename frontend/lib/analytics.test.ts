// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import posthog from 'posthog-js';
import {
  trackEvent,
  trackSchemaGenerated,
  trackDatabaseProvisioned,
  trackMigrationApplied,
  trackQuotaExceeded,
  trackDdlDownloaded,
  trackConsoleOpened,
  trackErrorShown,
  identifyUser,
  resetUser,
  isFeatureFlagEnabled,
  getFeatureFlagPayload,
} from './analytics';

vi.mock('posthog-js', () => ({
  default: {
    capture: vi.fn(),
    identify: vi.fn(),
    reset: vi.fn(),
    isFeatureEnabled: vi.fn(),
    getFeatureFlagPayload: vi.fn(),
  },
}));

vi.mock('./telemetry', () => ({
  trackActivity: vi.fn(),
}));

describe('analytics & quota protection', () => {
  const originalEnv = process.env.NEXT_PUBLIC_POSTHOG_KEY;

  beforeEach(() => {
    vi.clearAllMocks();
    process.env.NEXT_PUBLIC_POSTHOG_KEY = 'phc_test_dummy_key';
  });

  afterEach(() => {
    // `= undefined` atamak env'e "undefined" STRING'i yazar (truthy) — silmek gerekir.
    if (originalEnv === undefined) delete process.env.NEXT_PUBLIC_POSTHOG_KEY;
    else process.env.NEXT_PUBLIC_POSTHOG_KEY = originalEnv;
  });

  it('PostHog yapılandırıldığında olayı doğru alanlarla iletir', () => {
    trackSchemaGenerated({
      source: 'prompt',
      tableCount: 4,
      durationMs: 1200,
      hasRelations: true,
    });

    expect(posthog.capture).toHaveBeenCalledWith('schema_generated', {
      source: 'prompt',
      tableCount: 4,
      durationMs: 1200,
      hasRelations: true,
      module: 'canvas',
    });
  });

  it('KOTA KORUMASI: 1.5 saniye içinde aynı olay ve verilerle mükerrer çağrıyı filtreler (deduplication)', () => {
    // 1. çağrı
    trackQuotaExceeded({ resource: 'ai_tokens', plan: 'free' });
    expect(posthog.capture).toHaveBeenCalledTimes(1);

    // 2. hemen ardından gelen mükerrer çağrı (ör. hızlı çift tıklama)
    trackQuotaExceeded({ resource: 'ai_tokens', plan: 'free' });
    // Hala 1 olmalı, kotayı yakmamalı!
    expect(posthog.capture).toHaveBeenCalledTimes(1);

    // Farklı bir olay ise filtrelenmemeli
    trackDdlDownloaded('postgresql', 3);
    expect(posthog.capture).toHaveBeenCalledTimes(2);
  });

  it('veritabanı sağlama ve migrasyon kanonik olaylarını doğru kaydeder', () => {
    trackDatabaseProvisioned({
      provider: 'docker',
      engine: 'postgresql',
      mode: 'ephemeral',
      durationMs: 450,
      success: true,
    });

    expect(posthog.capture).toHaveBeenCalledWith('database_provisioned', {
      provider: 'docker',
      engine: 'postgresql',
      mode: 'ephemeral',
      durationMs: 450,
      success: true,
      module: 'ground',
    });

    trackMigrationApplied({
      risk: 'safe',
      environment: 'development',
      operationCount: 2,
      durationMs: 120,
    });

    expect(posthog.capture).toHaveBeenCalledWith('migration_applied', {
      risk: 'safe',
      environment: 'development',
      operationCount: 2,
      durationMs: 120,
      module: 'review',
    });
  });

  it('konsol açılışı ve hata sinyallerini izler', () => {
    trackConsoleOpened('proj_987');
    expect(posthog.capture).toHaveBeenCalledWith('console_opened', {
      projectId: 'proj_987',
      module: 'console',
    });

    trackErrorShown('review_diff', 'SYNTAX_ERR', 'Invalid constraint syntax');
    expect(posthog.capture).toHaveBeenCalledWith('error_shown', {
      surface: 'review_diff',
      code: 'SYNTAX_ERR',
      message: 'Invalid constraint syntax',
      module: 'error',
    });
  });

  it('kullanıcı kimliklendirme ve sıfırlama işlemlerini güvenle yönetir', () => {
    identifyUser({
      userId: 'usr_123',
      email: 'founder@namines.com',
      role: 'admin',
      plan: 'pro',
    });

    expect(posthog.identify).toHaveBeenCalledWith('usr_123', {
      email: 'founder@namines.com',
      role: 'admin',
      plan: 'pro',
    });

    resetUser();
    expect(posthog.reset).toHaveBeenCalled();
  });

  it('feature flag denetimini doğru fallback ile sağlar', () => {
    vi.mocked(posthog.isFeatureEnabled).mockReturnValue(true);
    expect(isFeatureFlagEnabled('flag_dark_mode')).toBe(true);

    vi.mocked(posthog.isFeatureEnabled).mockReturnValue(undefined as unknown as boolean);
    expect(isFeatureFlagEnabled('flag_unknown', false)).toBe(false);

    vi.mocked(posthog.getFeatureFlagPayload).mockReturnValue({ variant: 'v2' });
    expect(getFeatureFlagPayload('flag_onboarding')).toEqual({ variant: 'v2' });
  });

  it('PostHog anahtarı yoksa hatasız çalışır ve çağrı yapmaz', () => {
    delete process.env.NEXT_PUBLIC_POSTHOG_KEY;

    expect(() => {
      trackEvent('any_event', { test: 1 });
    }).not.toThrow();

    expect(posthog.capture).not.toHaveBeenCalled();
  });
});
