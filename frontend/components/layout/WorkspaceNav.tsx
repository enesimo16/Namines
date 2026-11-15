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
      className="relative flex items-center justify-center w-full min-w-0 h-full"
    >
      <div
        ref={navBarRef}
        id="workspace-nav-bar"
        className="relative flex items-center gap-0.5 p-1 rounded-full bg-surface-800/80 border border-line-strong/80 w-max select-none shadow-2xs"
      >
        {/* Animated Sliding Pill Glider */}
        {glider.visible && (
          <div
            className="absolute top-1 bottom-1 left-0 rounded-full bg-surface-900 border border-line-strong shadow-xs z-10 pointer-events-none transition-all duration-300 ease-[cubic-bezier(0.16,1,0.3,1)]"
            style={{
              transform: `translate3d(${glider.left}px, 0, 0)`,
              width: `${glider.width}px`,
            }}
          />
        )}
        {items.map((item) => {
          const label = t(item.labelKey);
          const isActive = item.active;
          return (
            <button
              key={item.key}
              id={`workspace-nav-${item.key}`}
              type="button"
              onClick={item.onClick}
              disabled={item.busy || !!item.disabledReason}
              title={item.disabledReason ?? label}
              aria-current={isActive ? 'page' : undefined}
              className={`group relative z-20 flex items-center gap-1.5 h-8 px-3 rounded-full text-[12px] whitespace-nowrap transition-colors duration-150 cursor-pointer disabled:opacity-40 disabled:cursor-not-allowed ${
                isActive
                  ? 'text-content-primary font-semibold'
                  : 'text-content-secondary hover:text-content-primary font-medium'
              }`}
            >
              {item.busy ? (
                <Loader2 className="w-3.5 h-3.5 animate-spin text-content-primary shrink-0" />
              ) : (
                <ModuleSymbol
                  module={item.key}
                  size={16}
                  className={`transition-transform duration-150 group-hover:scale-105 shrink-0 ${
                    isActive ? 'text-content-primary' : 'text-content-muted group-hover:text-content-primary'
                  }`}
                />
              )}
              <span>{label}</span>
            </button>
          );
        })}
      </div>
    </nav>
  );
}
