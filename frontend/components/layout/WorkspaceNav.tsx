'use client';

import { useState, useRef, useEffect } from 'react';
import { useRouter, usePathname } from 'next/navigation';
import { Loader2 } from 'lucide-react';
import { ModuleSymbol, type ModuleKind } from './ModuleLogo';
import { useSchemaStore } from '../../store/useSchemaStore';
import { useProjectHistoryStore } from '../../store/useProjectHistoryStore';
import { useActiveProjectStore } from '../../store/useActiveProjectStore';
import { useAuthStore } from '../../store/useAuthStore';
import { useT, useLangAttr, type MessageKey } from '../../lib/i18n';

/**
 * Tek ortak gezinme — Diagram / Build / Flow / Ground / Vault / Desk / Hub
 * Projeye kilitli çalışma alanı rotaları arasında tarayıcı sekmesi (browser tab)
 * estetiği ile pürüzsüz geçiş sağlar.
 *
 * Aktif sekme:
 * - Navbar alt çizgisine tam oturur (-mb-px ile alt sınırı erir ve çalışma alanına bağlanır).
 * - Sol ve sağ alt köşelerinde matematiksel kavis kanatları (downward curved wings) yer alır.
 * - Çevresi belirgin ve keskin Shopify Polaris çizgisiyle (border-t border-x border-line-strong) vurgulanır.
 */
const WORKSPACE_ROUTES = ['/canvas', '/flow', '/compile', '/ground', '/vault', '/review', '/hub', '/desk'];

type Item = {
  key: ModuleKind;
  labelKey: MessageKey;
  active: boolean;
  busy?: boolean;
  disabledReason?: string;
  onClick: () => void;
};

export default function WorkspaceNav() {
  const router = useRouter();
  const pathname = usePathname();
  const t = useT();
  const lang = useLangAttr();
  const activeProjectId = useProjectHistoryStore((s) => s.activeProjectId);
  const { scopedProjectId, scopedHubRepo } = useActiveProjectStore();
  const tableCount = useSchemaStore((s) => s.schema?.tables.length ?? 0);

  // Global Console vs Project Workspace
  const isGlobalPlane =
    (pathname === '/vault' || pathname === '/ground' || pathname === '/flow' || pathname === '/hub' || pathname === '/desk') &&
    !scopedProjectId;
  const targetProjectId = isGlobalPlane ? null : (scopedProjectId || activeProjectId);

  const visible = !!pathname && WORKSPACE_ROUTES.some((route) => pathname === route || pathname.startsWith(`${route}/`));

  const navBarRef = useRef<HTMLDivElement>(null);
  const [glider, setGlider] = useState<{ left: number; width: number; visible: boolean }>({
    left: 0,
    width: 0,
    visible: false,
  });

  useEffect(() => {
    const updateGlider = () => {
      if (!navBarRef.current) return;
      const activeBtn = navBarRef.current.querySelector<HTMLButtonElement>('button[aria-current="page"]');
      if (activeBtn) {
        const parentRect = navBarRef.current.getBoundingClientRect();
        const btnRect = activeBtn.getBoundingClientRect();
        setGlider({
          left: btnRect.left - parentRect.left,
          width: btnRect.width,
          visible: true,
        });
      } else {
        setGlider((prev) => ({ ...prev, visible: false }));
      }
    };

    updateGlider();
    // Re-check after render
    const tId = setTimeout(updateGlider, 50);
    window.addEventListener('resize', updateGlider);
    return () => {
      clearTimeout(tId);
      window.removeEventListener('resize', updateGlider);
    };
  }, [pathname]);

  // Hook'lar erken dönüşten ÖNCE çağrılmalı; yoksa görünür/gizli rotalar arasında "Rendered fewer hooks" çöküşü olur.
  if (!visible) return null;

  const goToBuild = () => {
    if (pathname === '/canvas') {
      window.dispatchEvent(new CustomEvent('namines:approve-diagram'));
      return;
    }
    router.push(targetProjectId ? `/compile?project=${encodeURIComponent(targetProjectId)}` : '/compile');
  };

  const goToHub = () => {
    if (!isGlobalPlane) {
      if (scopedHubRepo && (!targetProjectId || scopedHubRepo.id === targetProjectId)) {
        router.push(`/hub/${scopedHubRepo.owner}/${scopedHubRepo.slug}`);
        return;
      }
      if (targetProjectId) {
        const activeProj = useProjectHistoryStore.getState().projects.find((p) => p.id === targetProjectId);
        if (activeProj) {
          const owner = useAuthStore.getState().user?.username || 'me';
          const slug = activeProj.name.toLowerCase().replace(/[^a-z0-9\-]/g, '-').replace(/-+/g, '-').replace(/^-|-$/g, '');
          router.push(`/hub/${owner}/${slug || targetProjectId}`);
          return;
        }
      }
    }
    router.push('/hub');
  };

  // Diagram ile Build yan yana, ardından diğer modüller (Flow, Ground, Vault, Desk, Hub)
  const items: Item[] = [
    {
      key: 'diagram',
      labelKey: 'workspaceNav.diagram',
      active: pathname === '/canvas',
      onClick: () => router.push(targetProjectId ? `/canvas?project=${encodeURIComponent(targetProjectId)}` : '/canvas'),
    },
    {
      key: 'build',
      labelKey: 'workspaceNav.build',
      active: pathname === '/compile',
      onClick: goToBuild,
      disabledReason: pathname === '/canvas' && tableCount === 0 ? t('workspaceNav.buildNeedsTables') : undefined,
    },
    {
      key: 'flow',
      labelKey: 'workspaceNav.flow',
      active: pathname === '/flow',
      onClick: () => router.push(targetProjectId ? `/flow?project=${encodeURIComponent(targetProjectId)}` : '/flow'),
    },
    {
      key: 'ground',
      labelKey: 'workspaceNav.ground',
      active: pathname === '/ground',
      onClick: () => router.push(targetProjectId ? `/ground?project=${encodeURIComponent(targetProjectId)}` : '/ground'),
    },
    {
      key: 'vault',
      labelKey: 'workspaceNav.vault',
      active: pathname === '/vault',
      onClick: () => router.push(targetProjectId ? `/vault?project=${encodeURIComponent(targetProjectId)}` : '/vault'),
    },
    {
      key: 'desk',
      labelKey: 'workspaceNav.desk',
      active: pathname === '/desk' || pathname?.startsWith('/desk/'),
      onClick: () => router.push(targetProjectId ? `/desk?project=${encodeURIComponent(targetProjectId)}` : '/desk'),
    },
    {
      key: 'hub',
      labelKey: 'workspaceNav.hub',
      active: !!pathname && (pathname === '/hub' || pathname.startsWith('/hub/')),
      onClick: goToHub,
    },
  ];

  return (
    <nav
      id="workspace-nav"
      aria-label={t('workspaceNav.label')}
      lang={lang}
      className="relative flex items-end justify-center w-full min-w-0 h-full"
    >
      <div
        ref={navBarRef}
        id="workspace-nav-bar"
        className="relative flex items-end gap-1 px-1.5 w-max select-none h-full"
      >
        {/* Animated Liquid Sliding Glider */}
        {glider.visible && (
          <div
            className="absolute bottom-0 h-9 rounded-t-[8px] bg-surface-800 border-t border-x border-line-strong -mb-px z-10 pointer-events-none transition-all duration-300 ease-[cubic-bezier(0.16,1,0.3,1)] shadow-xs"
            style={{
              transform: `translate3d(${glider.left}px, 0, 0)`,
              width: `${glider.width}px`,
            }}
          >
            <div className="absolute inset-x-2 -top-px h-[2px] bg-gradient-to-r from-transparent via-content-primary/30 to-transparent" />
          </div>
        )}
        {items.map((item, idx) => {
          const label = t(item.labelKey);
          const isActive = item.active;
          return (
            <div key={item.key} className="relative flex items-end">
              {/* Pasif sekmeler arasında ince dikey ayırıcı */}
              {!isActive && idx > 0 && !items[idx - 1].active && (
                <span className="absolute -left-0.5 top-1/2 -translate-y-1/2 h-3.5 w-px bg-line-strong/60 pointer-events-none" />
              )}

              <button
                id={`workspace-nav-${item.key}`}
                type="button"
                onClick={item.onClick}
                disabled={item.busy || !!item.disabledReason}
                title={item.disabledReason ?? label}
                aria-current={isActive ? 'page' : undefined}
                className={`group relative flex items-center gap-1.5 px-3.5 rounded-t-[8px] text-xs transition-colors duration-150 cursor-pointer disabled:opacity-40 disabled:cursor-not-allowed ${
                  isActive
                    ? 'h-9 bg-surface-800 text-content-primary font-bold border-t border-x border-line-strong -mb-px z-20 shadow-xs'
                    : 'h-8 mb-0.5 text-content-secondary hover:text-content-primary hover:bg-surface-700/50 font-medium'
                }`}
              >
                {/* Sol alta doğru eğim ve kavis (Left curved wing) */}
                {isActive && (
                  <svg
                    className="absolute -bottom-px -left-2 w-2 h-2 text-surface-800 pointer-events-none"
                    viewBox="0 0 8 8"
                    fill="none"
                    aria-hidden="true"
                  >
                    <path d="M0 8 C4 8 8 4 8 0 V8 H0 Z" fill="currentColor" />
                    <path d="M0 8 C4 8 8 4 8 0" stroke="var(--color-line-strong)" strokeWidth="1" fill="none" />
                  </svg>
                )}

                {item.busy ? (
                  <Loader2 className="w-3.5 h-3.5 animate-spin text-content-primary shrink-0" />
                ) : (
                  <ModuleSymbol
                    module={item.key}
                    size={16}
                    className={`transition-colors shrink-0 ${
                      isActive ? 'text-content-primary' : 'text-content-muted group-hover:text-content-primary'
                    }`}
                  />
                )}
                <span>{label}</span>

                {/* Sağ alta doğru eğim ve kavis (Right curved wing) */}
                {isActive && (
                  <svg
                    className="absolute -bottom-px -right-2 w-2 h-2 text-surface-800 pointer-events-none"
                    viewBox="0 0 8 8"
                    fill="none"
                    aria-hidden="true"
                  >
                    <path d="M0 0 C0 4 4 8 8 8 H0 V0 Z" fill="currentColor" />
                    <path d="M0 0 C0 4 4 8 8 8" stroke="var(--color-line-strong)" strokeWidth="1" fill="none" />
                  </svg>
                )}
              </button>
            </div>
          );
        })}
      </div>
    </nav>
  );
}
