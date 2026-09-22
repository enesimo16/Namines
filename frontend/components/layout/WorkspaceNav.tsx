'use client';

import { useState } from 'react';
import { useRouter, usePathname } from 'next/navigation';
import { Boxes, Zap, HardDrive, ShieldCheck, Table2, Loader2 } from 'lucide-react';
import { useFlowBarStore } from '../../store/useFlowBarStore';
import { useProjectHistoryStore } from '../../store/useProjectHistoryStore';
import { useToastStore } from '../../store/useToastStore';
import { authService } from '../../services/api';
import { openDeskHandoff } from '../../lib/deskHandoff';

/**
 * Tek ortak gezinme — Flow/Ground/Vault/Desk daha önce dört ayrı, birbirini
 * bilmeyen yerdeydi: Flow yalnızca canvas'ta sağ tık menüsünün ardında, Ground
 * ve Vault hiçbir menüden bağlı olmayan başıboş sayfalardı (yalnızca URL'i
 * bilenler bulabiliyordu), Desk ise yalnızca Launch'ın tek-tık akışını
 * bitirdikten sonra çıkan bir buton. Bu şerit, proje çalışma alanı
 * rotalarının (canvas/compile/ground/vault) hepsinde aynı yerde durup
 * dördü arasında tek tıkla geçiş sağlıyor — Ground'dayken Vault'a gitmek için
 * artık ana sayfaya dönüp URL aramak gerekmiyor.
 *
 * Var olan giriş noktaları (canvas'taki sağ tık menüsü, Launch'ın "Open Desk"
 * butonu) BİLEREK kaldırılmadı — bunlar bağlamsal ve hâlâ faydalı (ör. Launch
 * bitince Desk'i doğrudan açmak). Buradaki eksik olan, HER ZAMAN erişilebilir
 * tek bir yerdi; onu ekliyoruz, var olanları silmiyoruz.
 */
const WORKSPACE_ROUTES = ['/canvas', '/compile', '/ground', '/vault'];

export default function WorkspaceNav() {
  const router = useRouter();
  const pathname = usePathname();
  const showToast = useToastStore(s => s.showToast);
  const activeProjectId = useProjectHistoryStore(s => s.activeProjectId);
  const [openingDesk, setOpeningDesk] = useState(false);

  if (!pathname || !WORKSPACE_ROUTES.includes(pathname)) return null;

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
      showToast('Open or save a project first — Desk needs one to open.', 'info');
      return;
    }
    setOpeningDesk(true);
    try {
      const { token } = await authService.createDeskHandoffToken();
      openDeskHandoff({ token, projectId: activeProjectId, view: 'data' });
    } catch {
      showToast('Could not open Desk. Try again.', 'error');
    } finally {
      setOpeningDesk(false);
    }
  };

  const items: Array<{
    key: string;
    label: string;
    icon: typeof Boxes;
    active: boolean;
    busy?: boolean;
    onClick: () => void;
  }> = [
    { key: 'diagram', label: 'Diagram', icon: Boxes, active: pathname === '/canvas', onClick: () => router.push('/canvas') },
    { key: 'flow', label: 'Flow', icon: Zap, active: false, onClick: goToFlow },
    { key: 'ground', label: 'Ground', icon: HardDrive, active: pathname === '/ground', onClick: () => router.push('/ground') },
    { key: 'vault', label: 'Vault', icon: ShieldCheck, active: pathname === '/vault', onClick: () => router.push('/vault') },
    { key: 'desk', label: 'Desk', icon: Table2, active: false, busy: openingDesk, onClick: openDesk },
  ];

  return (
    <nav
      aria-label="Workspace"
      className="flex items-center gap-0.5 bg-white/[0.04] border border-content-primary/10 rounded-full p-0.5 min-w-0"
    >
      {items.map(item => (
        <button
          key={item.key}
          onClick={item.onClick}
          disabled={item.busy}
          title={item.label}
          aria-label={item.label}
          aria-current={item.active ? 'page' : undefined}
          className={`tap-44 flex items-center gap-1.5 px-2.5 py-1.5 rounded-full text-[11px] font-medium transition-colors cursor-pointer whitespace-nowrap disabled:opacity-60 disabled:cursor-wait ${
            item.active
              ? 'bg-accent text-accent-on'
              : 'text-content-muted hover:text-content-primary hover:bg-white/[0.06]'
          }`}
        >
          {item.busy ? <Loader2 className="w-3.5 h-3.5 animate-spin" /> : <item.icon className="w-3.5 h-3.5" />}
          <span className="hidden xl:inline">{item.label}</span>
        </button>
      ))}
    </nav>
  );
}
