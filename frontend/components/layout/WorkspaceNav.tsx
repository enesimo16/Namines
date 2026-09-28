'use client';

import { useEffect, useLayoutEffect, useRef, useState } from 'react';
import { useRouter, usePathname } from 'next/navigation';
import { Boxes, Zap, HardDrive, ShieldCheck, Table2, Loader2, ChevronDown, ArrowUpRight, LayoutGrid } from 'lucide-react';
import { useFlowBarStore } from '../../store/useFlowBarStore';
import { useProjectHistoryStore } from '../../store/useProjectHistoryStore';
import { useToastStore } from '../../store/useToastStore';
import { authService } from '../../services/api';
import { openDeskHandoff } from '../../lib/deskHandoff';
import { useT, useLangAttr, type MessageKey } from '../../lib/i18n';

/**
 * Tek ortak gezinme — Flow/Ground/Vault/Desk daha önce dört ayrı, birbirini
 * bilmeyen yerdeydi: Flow yalnızca canvas'ta sağ tık menüsünün ardında, Ground
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
const WORKSPACE_ROUTES = ['/canvas', '/compile', '/ground', '/vault'];
const ICON_ONLY_BELOW = 132;

type Item = {
  key: string;
  labelKey: MessageKey;
  icon: typeof Boxes;
  active: boolean;
  busy?: boolean;
  external?: boolean;
  onClick: () => void;
};

export default function WorkspaceNav() {
  const router = useRouter();
  const pathname = usePathname();
  const t = useT();
  const lang = useLangAttr();
  const showToast = useToastStore(s => s.showToast);
  const activeProjectId = useProjectHistoryStore(s => s.activeProjectId);
  const [openingDesk, setOpeningDesk] = useState(false);

  const slotRef = useRef<HTMLDivElement>(null);
  const measureRef = useRef<HTMLDivElement>(null);
  const menuRef = useRef<HTMLDivElement>(null);
  const [mode, setMode] = useState<'bar' | 'menu' | 'icon'>('bar');
  const [menuOpen, setMenuOpen] = useState(false);

  const visible = !!pathname && WORKSPACE_ROUTES.includes(pathname);

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

  if (!visible) return null;

  const goToFlow = () => {
    // Global (persist edilen) store — canvas zaten NaminesFlowBar/Panel'i
    // koşulsuz render edip bu durumdan okuyor, o yüzden canvas'a gitmeden
    // ÖNCE açık işaretlemek yeterli.
    useFlowBarStore.getState().setHidden(false);
    useFlowBarStore.getState().setPanelOpen(true);
    if (pathname !== '/canvas') router.push('/canvas');
  };

  const openDesk = async () => {
    if (!activeProjectId) {
      showToast(t('workspaceNav.deskNeedsProject'), 'info');
      return;
    }
    setOpeningDesk(true);
    try {
      const { token } = await authService.createDeskHandoffToken();
      openDeskHandoff({ token, projectId: activeProjectId, view: 'data' });
    } catch {
      showToast(t('workspaceNav.deskFailed'), 'error');
    } finally {
      setOpeningDesk(false);
    }
  };

  const items: Item[] = [
    { key: 'diagram', labelKey: 'workspaceNav.diagram', icon: Boxes, active: pathname === '/canvas', onClick: () => router.push('/canvas') },
    { key: 'flow', labelKey: 'workspaceNav.flow', icon: Zap, active: false, onClick: goToFlow },
    { key: 'ground', labelKey: 'workspaceNav.ground', icon: HardDrive, active: pathname === '/ground', onClick: () => router.push('/ground') },
    { key: 'vault', labelKey: 'workspaceNav.vault', icon: ShieldCheck, active: pathname === '/vault', onClick: () => router.push('/vault') },
    { key: 'desk', labelKey: 'workspaceNav.desk', icon: Table2, active: false, busy: openingDesk, external: true, onClick: openDesk },
  ];
  const current = items.find(item => item.active);
  const CurrentIcon = current?.icon ?? LayoutGrid;
  const currentLabel = current ? t(current.labelKey) : t('workspaceNav.label');

  const renderBar = (interactive: boolean) => (
    <div className="flex items-center gap-0.5 rounded-[var(--radius-control)] border border-content-primary/10 bg-surface-700/60 p-0.5 w-max">
      {items.map(item => {
        const label = t(item.labelKey);
        return (
          <button
            key={item.key}
            type="button"
            tabIndex={interactive ? undefined : -1}
            onClick={interactive ? item.onClick : undefined}
            disabled={item.busy}
            title={item.external ? `${label} — ${t('workspaceNav.opensInNewTab')}` : label}
            aria-current={item.active ? 'page' : undefined}
            className={`relative flex items-center gap-1.5 h-8 px-2.5 rounded-[calc(var(--radius-control)-2px)] text-[11px] font-medium whitespace-nowrap transition-colors cursor-pointer disabled:opacity-60 disabled:cursor-wait ${
              item.active
                ? 'bg-accent-subtle text-accent-text'
                : 'text-content-muted hover:text-content-primary hover:bg-white/[0.05]'
            }`}
          >
            {item.busy ? <Loader2 className="w-3.5 h-3.5 animate-spin" /> : <item.icon className="w-3.5 h-3.5 shrink-0" />}
            <span>{label}</span>
            {item.external && <ArrowUpRight className="w-3 h-3 opacity-60" aria-hidden="true" />}
            {item.active && <span className="absolute left-2 right-2 -bottom-0.5 h-0.5 rounded-full bg-accent-hover" />}
          </button>
        );
      })}
    </div>
  );

  return (
    <div ref={slotRef} lang={lang} className="relative flex items-center justify-center w-full min-w-0">
      {/* Ölçüm kopyası: görünmez, tıklanmaz, erişilebilirlik ağacında yok. */}
      <div ref={measureRef} aria-hidden="true" className="absolute left-0 top-0 invisible pointer-events-none w-max">
        {renderBar(false)}
      </div>

      {mode === 'bar' ? (
        <nav aria-label={t('workspaceNav.label')}>{renderBar(true)}</nav>
      ) : (
        <div ref={menuRef} className="relative">
          <button
            type="button"
            onClick={() => setMenuOpen(open => !open)}
            aria-haspopup="menu"
            aria-expanded={menuOpen}
            aria-label={t('workspaceNav.menu')}
            title={currentLabel}
            className={`tap-44 flex items-center gap-1.5 h-9 rounded-[var(--radius-control)] border text-[11px] font-medium transition-colors cursor-pointer ${
              mode === 'icon' ? 'px-2' : 'px-2.5'
            } ${
              menuOpen
                ? 'border-content-primary/20 bg-surface-600 text-content-primary'
                : 'border-content-primary/10 bg-surface-700/60 text-content-secondary hover:text-content-primary hover:border-content-primary/20'
            }`}
          >
            <CurrentIcon className={`w-3.5 h-3.5 shrink-0 ${current ? 'text-accent-text' : ''}`} />
            {mode === 'menu' && <span className="whitespace-nowrap">{currentLabel}</span>}
            <ChevronDown className={`w-3.5 h-3.5 shrink-0 transition-transform ${menuOpen ? 'rotate-180' : ''}`} />
          </button>

          {menuOpen && (
            <div
              role="menu"
              aria-label={t('workspaceNav.label')}
              className="absolute left-1/2 -translate-x-1/2 top-full mt-1.5 min-w-[190px] rounded-[var(--radius-card)] border border-content-primary/10 bg-surface-800 p-1 shadow-lg shadow-surface-900/60 z-50"
            >
              <div className="px-2.5 pt-1.5 pb-1 text-micro font-bold uppercase tracking-wider text-content-subtle">
                {t('workspaceNav.label')}
              </div>
              {items.map(item => {
                const label = t(item.labelKey);
                return (
                  <button
                    key={item.key}
                    type="button"
                    role="menuitem"
                    disabled={item.busy}
                    aria-current={item.active ? 'page' : undefined}
                    onClick={() => { setMenuOpen(false); item.onClick(); }}
                    className={`w-full flex items-center gap-2.5 min-h-[40px] px-2.5 rounded-[var(--radius-control)] text-xs font-medium transition-colors cursor-pointer disabled:opacity-60 ${
                      item.active
                        ? 'bg-accent-subtle text-accent-text'
                        : 'text-content-secondary hover:text-content-primary hover:bg-white/[0.05]'
                    }`}
                  >
                    {item.busy ? <Loader2 className="w-4 h-4 animate-spin" /> : <item.icon className="w-4 h-4 shrink-0" />}
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
