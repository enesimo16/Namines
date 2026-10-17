'use client';

import { useEffect, useLayoutEffect, useRef, useState } from 'react';
import { useRouter, usePathname } from 'next/navigation';
import { Boxes, Zap, HardDrive, ShieldCheck, Table2, Hammer, Loader2, ChevronDown, ArrowUpRight, LayoutGrid } from 'lucide-react';
import { useSchemaStore } from '../../store/useSchemaStore';
import { useProjectHistoryStore } from '../../store/useProjectHistoryStore';
import { authService } from '../../services/api';
import { openDeskHandoff, DESK_URL } from '../../lib/deskHandoff';
import { useT, useLangAttr, type MessageKey } from '../../lib/i18n';

/**
 * Tek ortak gezinme — Flow/Ground/Vault/Desk daha önce dört ayrı, birbirini
 * bilmeyen yerdeydi (Flow artık kendi sayfası: `/flow`; canvas'a gömülü küçük
 * çubuk "Flow'a tıkladım ama hâlâ diyagramdayım" karışıklığı yaratıyordu): Flow yalnızca canvas'ta sağ tık menüsünün ardında, Ground
 * ve Vault hiçbir menüden bağlı olmayan başıboş sayfalardı (yalnızca URL'i
 * bilenler bulabiliyordu), Desk ise yalnızca Launch'ın tek-tık akışını
 * bitirdikten sonra çıkan bir buton. Bu şerit, proje çalışma alanı
 * rotalarının (canvas/compile/ground/vault) hepsinde aynı yerde durup
 * dördü arasında tek tıkla geçiş sağlıyor.
 *
 * <b>Sığmadığında açılır menüye dönüşüyor — kırılım noktasıyla değil, ÖLÇEREK.</b>
 * Başlığın solundaki (proje adı, ekip düğmesi) ve sağındaki (dil, hesap)
 * grupların genişliği rotaya, oturuma ve dile göre değişiyor; sabit bir `xl:`
 * eşiği bazı birleşimlerde şeridi komşularının altına sokuyordu ("iç içe
 * giriyorlar" geri bildirimi). Şeridin doğal genişliği gizli bir kopyadan
 * ölçülüp ayrılan alanla karşılaştırılıyor; sığmıyorsa tek bir menü düğmesi,
 * o da sığmıyorsa yalnızca ikon.
 */
const WORKSPACE_ROUTES = ['/canvas', '/flow', '/compile', '/ground', '/vault', '/review'];
const ICON_ONLY_BELOW = 132;

type Item = {
  key: string;
  labelKey: MessageKey;
  icon: typeof Boxes;
  active: boolean;
  busy?: boolean;
  /** Etkin değil ama görünür: sebebi `disabledReason` ile söylenir. */
  disabledReason?: string;
  external?: boolean;
  onClick: () => void;
};

export default function WorkspaceNav() {
  const router = useRouter();
  const pathname = usePathname();
  const t = useT();
  const lang = useLangAttr();
  const activeProjectId = useProjectHistoryStore(s => s.activeProjectId);
  const tableCount = useSchemaStore(s => s.schema?.tables.length ?? 0);
  const [openingDesk, setOpeningDesk] = useState(false);

  const slotRef = useRef<HTMLDivElement>(null);
  const measureRef = useRef<HTMLDivElement>(null);
  const menuRef = useRef<HTMLDivElement>(null);
  const navBarRef = useRef<HTMLDivElement>(null);
  const [activePillRect, setActivePillRect] = useState<{ left: number; width: number; ready: boolean }>({
    left: 0,
    width: 0,
    ready: false,
  });
  const [mode, setMode] = useState<'bar' | 'menu' | 'icon'>('bar');
  const [menuOpen, setMenuOpen] = useState(false);

  // `/review/<id>` de çalışma alanının parçası; tam eşleşme yalnızca liste sayfasını kapsıyordu.
  const visible = !!pathname && WORKSPACE_ROUTES.some(route => pathname === route || pathname.startsWith(`${route}/`));

  // useLayoutEffect: ölçüm boyamadan ÖNCE yapılıyor, yoksa dar ekranda şerit
  // bir kare boyunca taşmış hâliyle görünüp sonra menüye atlıyordu.
  useLayoutEffect(() => {
    if (!visible) return;
    const slot = slotRef.current;
    const measure = measureRef.current;
    if (!slot || !measure) return;
    const update = () => {
      const available = slot.clientWidth;
      const needed = measure.scrollWidth;
      const next = needed <= available ? 'bar' : available < ICON_ONLY_BELOW ? 'icon' : 'menu';
      setMode(next);
      // Şeride dönülünce açık kalan menü anlamsız — burada kapatmak, ayrı bir
      // efektte setState zinciri kurmaktan daha doğrudan.
      if (next === 'bar') setMenuOpen(false);
    };
    update();
    const observer = new ResizeObserver(update);
    observer.observe(slot);
    // Dil değişince etiket genişlikleri değişiyor — kopyayı da izle.
    observer.observe(measure);

    // ResizeObserver YETMİYOR: sayfa görünmezken (arka plan sekmesi, gizli
    // panel) tarayıcı kare üretmediği için geri çağırma hiç çalışmıyor ve ilk
    // ölçüm 0 genişlikle donup kalıyor — geniş ekranda bile "yalnızca ikon"
    // moduna düşüyordu. Sayfa görünür olunca boyut DEĞİŞMEDİĞİ için gözlemci
    // de uyanmıyor; o yüzden görünürlüğü ayrıca dinliyoruz.
    window.addEventListener('resize', update);
    document.addEventListener('visibilitychange', update);
    return () => {
      observer.disconnect();
      window.removeEventListener('resize', update);
      document.removeEventListener('visibilitychange', update);
    };
  }, [visible]);

  useEffect(() => {
    if (!menuOpen) return;
    const onPointer = (e: MouseEvent) => {
      if (menuRef.current && !menuRef.current.contains(e.target as Node)) setMenuOpen(false);
    };
    const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape') setMenuOpen(false); };
    document.addEventListener('mousedown', onPointer);
    document.addEventListener('keydown', onKey);
    return () => {
      document.removeEventListener('mousedown', onPointer);
      document.removeEventListener('keydown', onKey);
    };
  }, [menuOpen]);

  useLayoutEffect(() => {
    if (!visible || mode !== 'bar') return;
    const nav = navBarRef.current;
    if (!nav) return;
    const activeEl = nav.querySelector<HTMLElement>('[aria-current="page"]');
    if (activeEl) {
      setActivePillRect({
        left: activeEl.offsetLeft,
        width: activeEl.offsetWidth,
        ready: true,
      });
    } else {
      setActivePillRect(prev => ({ ...prev, ready: false }));
    }
  }, [pathname, visible, mode]);

  if (!visible) return null;

  // "Approve" düğmesi araç çubuğundan buraya taşındı ve "Build" adını aldı: eylem
  // "diyagramı onayla ve derleme/dışa aktarma ekranına geç". Canvas'ta onay mantığı
  // (düğüm konumlarını şemaya yazma, diff modunda atlama) ToolbarPanel'de yaşıyor —
  // React Flow bağlamı orada — o yüzden burada bir olayla tetikleniyor.
  const goToBuild = () => {
    if (pathname === '/canvas') {
      window.dispatchEvent(new CustomEvent('namines:approve-diagram'));
      return;
    }
    router.push('/compile');
  };

  const openDesk = async () => {
    setOpeningDesk(true);
    try {
      if (activeProjectId) {
        try {
          const { token } = await authService.createDeskHandoffToken();
          openDeskHandoff({ token, projectId: activeProjectId, view: 'data' });
          return;
        } catch {
          window.open(DESK_URL, '_blank');
          return;
        }
      }
      window.open(DESK_URL, '_blank');
    } catch {
      window.open(DESK_URL, '_blank');
    } finally {
      setOpeningDesk(false);
    }
  };

  const items: Item[] = [
    { key: 'diagram', labelKey: 'workspaceNav.diagram', icon: Boxes, active: pathname === '/canvas', onClick: () => router.push('/canvas') },
    { key: 'flow', labelKey: 'workspaceNav.flow', icon: Zap, active: pathname === '/flow', onClick: () => router.push('/flow') },
    { key: 'ground', labelKey: 'workspaceNav.ground', icon: HardDrive, active: pathname === '/ground', onClick: () => router.push('/ground') },
    { key: 'vault', labelKey: 'workspaceNav.vault', icon: ShieldCheck, active: pathname === '/vault', onClick: () => router.push('/vault') },
    { key: 'desk', labelKey: 'workspaceNav.desk', icon: Table2, active: false, busy: openingDesk, external: true, onClick: openDesk },
    {
      key: 'build', labelKey: 'workspaceNav.build', icon: Hammer, active: pathname === '/compile', onClick: goToBuild,
      // Tablo yokken derlenecek bir şey yok; gizlemek yerine sebebini söylüyoruz.
      disabledReason: pathname === '/canvas' && tableCount === 0 ? t('workspaceNav.buildNeedsTables') : undefined,
    },
  ];
  const current = items.find(item => item.active);
  const CurrentIcon = current?.icon ?? LayoutGrid;
  const currentLabel = current ? t(current.labelKey) : t('workspaceNav.label');

  const renderBar = (interactive: boolean) => (
    <div
      ref={interactive ? navBarRef : undefined}
      id={interactive ? 'workspace-nav-bar' : undefined}
      className="relative flex items-center gap-1 rounded-full border border-line-strong bg-surface-800/95 p-1 w-max shadow-sm backdrop-blur-md select-none"
    >
      {/* Liquid sliding pill indicator */}
      {interactive && activePillRect.ready && (
        <span
          className="absolute top-1 bottom-1 rounded-full bg-surface-700 border border-line-strong shadow-xs pointer-events-none transition-all duration-300 ease-[cubic-bezier(0.2,0.8,0.2,1)]"
          style={{
            left: 0,
            transform: `translateX(${activePillRect.left}px)`,
            width: `${activePillRect.width}px`,
          }}
          aria-hidden="true"
        />
      )}
      {items.map(item => {
        const label = t(item.labelKey);
        return (
          <button
            key={item.key}
            id={`workspace-nav-${item.key}`}
            type="button"
            tabIndex={interactive ? undefined : -1}
            onClick={interactive ? item.onClick : undefined}
            disabled={item.busy || !!item.disabledReason}
            title={item.disabledReason ?? (item.external ? `${label} — ${t('workspaceNav.opensInNewTab')}` : label)}
            aria-current={item.active ? 'page' : undefined}
            className={`relative z-10 flex items-center gap-1.5 h-7.5 px-3.5 rounded-full text-micro whitespace-nowrap transition-colors duration-200 cursor-pointer disabled:opacity-40 disabled:cursor-not-allowed ${
              item.active
                ? 'text-content-primary font-bold'
                : 'text-content-muted hover:text-content-primary hover:bg-surface-700/40 font-semibold'
            }`}
          >
            {item.busy ? <Loader2 className="w-3.5 h-3.5 animate-spin" /> : <item.icon className={`w-3.5 h-3.5 shrink-0 transition-colors duration-200 ${item.active ? 'text-namines-teal-vibrant' : 'text-content-muted'}`} />}
            <span>{label}</span>
            {item.external && <ArrowUpRight className="w-3 h-3 opacity-60" aria-hidden="true" />}
          </button>
        );
      })}
    </div>
  );

  return (
    <div id="workspace-nav" ref={slotRef} lang={lang} className="relative flex items-center justify-center w-full min-w-0">
      {/* Ölçüm kopyası: görünmez, tıklanmaz, erişilebilirlik ağacında yok. */}
      <div ref={measureRef} aria-hidden="true" className="absolute left-0 top-0 invisible pointer-events-none w-max">
        {renderBar(false)}
      </div>

      {mode === 'bar' ? (
        <nav aria-label={t('workspaceNav.label')}>{renderBar(true)}</nav>
      ) : (
        <div ref={menuRef} className="relative select-none flex items-center gap-1.5">
          <button
            type="button"
            onClick={() => setMenuOpen(open => !open)}
            aria-haspopup="menu"
            aria-expanded={menuOpen}
            aria-label={t('workspaceNav.menu')}
            title={currentLabel}
            className={`tap-44 flex items-center gap-1.5 h-7.5 rounded-full border text-micro font-bold transition-all cursor-pointer ${
              mode === 'icon' ? 'px-2.5' : 'px-3.5'
            } ${
              menuOpen
                ? 'border-line-strong bg-surface-700 text-content-primary shadow-xs'
                : 'border-line-strong bg-surface-800/95 text-content-secondary hover:text-content-primary hover:border-line-strong hover:bg-surface-700/60 shadow-sm'
            }`}
          >
            <CurrentIcon className="w-3.5 h-3.5 shrink-0 text-namines-teal-vibrant" />
            {mode === 'menu' && <span className="whitespace-nowrap">{currentLabel}</span>}
            <ChevronDown className={`w-3.5 h-3.5 shrink-0 transition-transform ${menuOpen ? 'rotate-180' : ''}`} />
          </button>

          {menuOpen && (
            <div
              role="menu"
              aria-label={t('workspaceNav.label')}
              className="absolute left-1/2 -translate-x-1/2 top-full mt-1.5 min-w-[210px] rounded-[var(--radius-modal)] border border-line-strong bg-surface-800 p-1.5 shadow-2xl z-50 backdrop-blur-2xl animate-dropdown-in"
            >
              <div className="px-2.5 pt-1.5 pb-1 text-micro font-bold uppercase tracking-wider text-content-muted">
                {t('workspaceNav.label')}
              </div>
              {items.map(item => {
                const label = t(item.labelKey);
                return (
                  <button
                    key={item.key}
                    type="button"
                    role="menuitem"
                    disabled={item.busy || !!item.disabledReason}
                    title={item.disabledReason}
                    aria-current={item.active ? 'page' : undefined}
                    onClick={() => { setMenuOpen(false); item.onClick(); }}
                    className={`w-full flex items-center gap-2.5 min-h-[36px] px-2.5 rounded-[var(--radius-control)] text-xs transition-colors cursor-pointer disabled:opacity-50 ${
                      item.active
                        ? 'bg-surface-700 text-content-primary font-bold border border-line-strong shadow-xs'
                        : 'text-content-secondary hover:text-content-primary hover:bg-surface-700/60 font-medium'
                    }`}
                  >
                    {item.busy ? <Loader2 className="w-4 h-4 animate-spin" /> : <item.icon className={`w-4 h-4 shrink-0 ${item.active ? 'text-namines-teal-vibrant' : 'text-content-muted'}`} />}
                    <span className="flex-1 text-left">{label}</span>
                    {item.external && (
                      <ArrowUpRight className="w-3.5 h-3.5 opacity-60" aria-label={t('workspaceNav.opensInNewTab')} />
                    )}
                  </button>
                );
              })}
            </div>
          )}
        </div>
      )}
    </div>
  );
}
