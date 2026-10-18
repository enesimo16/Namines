import { adminService } from '../services/adminService';

export function trackActivity(options: {
  action: string;
  module: string;
  path?: string;
  metadata?: string;
  durationSeconds?: number;
}): void {
  if (typeof window === 'undefined') return;

  const currentPath = options.path || window.location.pathname;

  // Non-blocking fire and forget
  adminService.trackEvent({
    path: currentPath,
    action: options.action,
    module: options.module,
    metadata: options.metadata,
    durationSeconds: options.durationSeconds,
  }).catch(() => {
    // Silently ignore telemetry failure
  });
}
