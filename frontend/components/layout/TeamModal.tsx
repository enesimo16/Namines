'use client';

import { useEffect, useRef, useState } from 'react';
import { useRouter } from 'next/navigation';
import { Users, Link2, Copy, Check, Trash2, Loader2, X, Clock, Crown, ShieldCheck, Eye, Pencil, Share2, ExternalLink } from 'lucide-react';
import { teamService } from '../../services/api';
import { TeamStatus, CreatedInvite, TeamProject } from '../../types/team';
import { useProjectHistoryStore } from '../../store/useProjectHistoryStore';
import { useSchemaStore } from '../../store/useSchemaStore';
import { useToastStore } from '../../store/useToastStore';
import { useFocusTrap } from '../../hooks/useFocusTrap';
import { errorMessage } from '../../lib/errors';
import { useEscapeKey } from '../../hooks/useEscapeKey';

interface Props {
  isOpen: boolean;
  onClose: () => void;
}

const roleIcon: Record<string, typeof Eye> = {
  Owner: Crown,
  Admin: ShieldCheck,
  Editor: Pencil,
  Viewer: Eye,
};

/**
 * Ekip paneli: koltuklar, tek kullanımlık davet bağlantıları ve ortak projeler.
 *
 * Yalnızca Team planında dolu açılıyor. Free/Pro tek kişilik; oralarda çalışan
 * bir ekip arayüzü göstermek, satın alınmamış bir özelliği varmış gibi sunmak
 * olurdu — bunun yerine ne olduğunu anlatan bir ekran gösteriliyor.
 */
export default function TeamModal({ isOpen, onClose }: Props) {
  useEscapeKey(isOpen, onClose);
  if (!isOpen) return null;
  return <TeamModalPanel onClose={onClose} />;
}

function TeamModalPanel({ onClose }: { onClose: () => void }) {
  const router = useRouter();
  const [team, setTeam] = useState<TeamStatus | null>(null);
  const [projects, setProjects] = useState<TeamProject[]>([]);
  const [isLoading, setIsLoading] = useState(true);
  const [isCreating, setIsCreating] = useState(false);
  const [isSharing, setIsSharing] = useState(false);
  const [fresh, setFresh] = useState<CreatedInvite | null>(null);
  const [copied, setCopied] = useState(false);
  const localProjects = useProjectHistoryStore(s => s.projects);
  const activeProjectId = useProjectHistoryStore(s => s.activeProjectId);
  const showToast = useToastStore(s => s.showToast);
  const modalRef = useRef<HTMLDivElement>(null);
  useFocusTrap(true, modalRef);

  /** Veriyi ceker; yukleme bayragini cagirana birakir. */
  const fetchTeam = async () => {
    try {
      // Modül açılışında tüm projeleri buluta yazmak (syncAllToCloud) kaldırıldı: yalnızca
      // paylaşım eyleminde gerekiyor (handleShareProject), burada sadece çekilir.
      await useProjectHistoryStore.getState().syncWithCloud();
      const [status, act] = await Promise.all([teamService.status(), teamService.activity()]);
      setTeam(status);
      setProjects(act.projects);
    } catch {
      showToast('Team information could not be loaded.', 'error');
    }
  };

  /** Kullanici eylemlerinden sonra yenile (spinner gostererek). */
  const load = async () => {
    setIsLoading(true);
    try {
      await fetchTeam();
    } finally {
      setIsLoading(false);
    }
  };

  useEffect(() => {
    let cancelled = false;
    fetchTeam().finally(() => {
      if (!cancelled) setIsLoading(false);
    });
    return () => { cancelled = true; };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const inviteUrl = fresh ? `${window.location.origin}/join/${fresh.token}` : '';

  const handleCreate = async () => {
    setIsCreating(true);
    try {
      const created = await teamService.createInvite('Editor', 7);
      setFresh(created);
      setCopied(false);
      await load();
    } catch (err) {
      showToast(errorMessage(err, 'Invite link could not be created.'), 'error');
    } finally {
      setIsCreating(false);
    }
  };

  const handleCopy = async () => {
    await navigator.clipboard.writeText(inviteUrl);
    setCopied(true);
    showToast('Invite link copied.', 'success');
  };

  const handleRevoke = async (id: string) => {
    try {
      await teamService.revokeInvite(id);
      showToast('Invite revoked. The seat is free again.', 'info');
      await load();
    } catch {
      showToast('Invite could not be revoked.', 'error');
    }
  };

  const handleOpenSharedProject = async (projectId: string) => {
    let snap = useProjectHistoryStore.getState().loadProject(projectId);
    if (!snap) {
      await useProjectHistoryStore.getState().syncWithCloud();
      snap = useProjectHistoryStore.getState().loadProject(projectId);
    }
    if (!snap) {
      showToast('Could not load project schema.', 'error');
      return;
    }
    useProjectHistoryStore.getState().setActiveProjectId(snap.id);
    useSchemaStore.getState().setDbType(snap.dbType);
    useSchemaStore.getState().setProjectName(snap.name);
    useSchemaStore.getState().loadFromSchema(snap.schema, snap.nodePositions, true);
    onClose();
    router.push('/canvas');
  };

  const handleShareProject = async (projectId: string) => {
    setIsSharing(true);
    try {
      await useProjectHistoryStore.getState().syncAllToCloud();
      await teamService.shareProject(projectId);
      await useProjectHistoryStore.getState().syncWithCloud();
      await fetchTeam();
      showToast('Project shared with team workspace.', 'success');
    } catch (err) {
      showToast(errorMessage(err, 'Could not share project with team.'), 'error');
    } finally {
      setIsSharing(false);
    }
  };

  return (
    <div className="fixed inset-0 z-modal flex items-center justify-center p-4">
      <div className="absolute inset-0 bg-surface-900/80 backdrop-blur-sm" onClick={onClose} />

      <div
        ref={modalRef}
        role="dialog"
        aria-modal="true"
        aria-labelledby="team-title"
        className="relative w-full max-w-2xl max-h-[88vh] overflow-y-auto bg-surface-900/98 border border-line-strong rounded-[var(--radius-modal)] p-6 sm:p-7 text-content-primary shadow-2xl backdrop-blur-2xl animate-in zoom-in-95 duration-200"
      >
        <div className="flex items-start justify-between mb-6 pb-4 border-b border-line">
          <div className="flex items-center gap-3">
            <div className="w-10 h-10 rounded-full bg-surface-800 border border-line-strong flex items-center justify-center shrink-0">
              <Users className="w-5 h-5 text-content-primary" />
            </div>
            <div>
              <h2 id="team-title" className="text-base font-bold text-content-primary">
                Team Workspace
              </h2>
              <p className="text-xs text-content-secondary mt-0.5">
                {team?.teamEnabled
                  ? 'Everyone here shares the same workspace and projects.'
                  : 'Working together is part of the Team plan.'}
              </p>
            </div>
          </div>
          <button
            type="button"
            onClick={onClose}
            aria-label="Close"
            className="tap-44 w-8 h-8 rounded-full border border-line bg-surface-800/80 hover:bg-surface-700/80 flex items-center justify-center text-content-muted hover:text-content-primary transition-colors cursor-pointer"
          >
            <X className="w-4 h-4" />
          </button>
        </div>

        {isLoading && !team ? (
          <div className="flex items-center gap-2 text-xs text-content-muted py-12 justify-center">
            <Loader2 className="w-4 h-4 animate-spin text-content-primary" /> Loading team…
          </div>
        ) : !team?.teamEnabled ? (
          <div className="space-y-4">
            <div className="border border-line-strong rounded-[var(--radius-card)] p-5 bg-surface-800/60 shadow-sm">
              <p className="text-sm font-semibold mb-2 text-content-primary">Your plan is single-seat</p>
              <p className="text-xs text-content-secondary leading-relaxed">
                The Team plan gives you <strong className="text-content-primary font-semibold">3 seats</strong> — you plus two
                people you invite. Everyone sees the same projects, and the activity list shows who changed what.
                Invites are private, single-use links: once someone joins, that link stops working.
              </p>
            </div>
            <button
              type="button"
              onClick={() => {
                onClose();
                window.dispatchEvent(new Event('namines:open-ai-settings'));
              }}
              className="w-full bg-accent hover:bg-accent-hover text-accent-on font-mono font-bold py-2.5 rounded-full text-xs uppercase tracking-wider transition-all shadow-sm cursor-pointer"
            >
              See plans
            </button>
          </div>
        ) : (
          <div className="space-y-6">
            {/* Koltuklar */}
            <div className="flex items-center justify-between border border-line-strong rounded-[var(--radius-card)] px-4 py-3 bg-surface-800/60 shadow-sm">
              <div>
                <p className="text-[10px] uppercase tracking-wider text-content-muted font-mono font-semibold">Seats</p>
                <p className="text-sm font-bold mt-0.5 text-content-primary">
                  {team.seats.total < 0
                    ? `${team.seats.used} used · unlimited`
                    : `${team.seats.used} of ${team.seats.total} used`}
                </p>
              </div>
              <button
                type="button"
                onClick={handleCreate}
                disabled={isCreating || team.seats.available === 0}
                className="flex items-center gap-2 bg-accent hover:bg-accent-hover text-accent-on font-mono font-bold px-4 py-2 rounded-full text-xs uppercase tracking-wider transition-all disabled:opacity-40 disabled:cursor-not-allowed shadow-sm cursor-pointer"
              >
                {isCreating ? <Loader2 className="w-3.5 h-3.5 animate-spin text-accent-on" /> : <Link2 className="w-3.5 h-3.5" />}
                {team.seats.available === 0 ? 'No seats left' : 'Create invite link'}
              </button>
            </div>

            {/* Yeni bağlantı — yalnızca bir kez gösterilir */}
            {fresh && (
              <div className="border border-line-subtle rounded-[var(--radius-card)] p-4 bg-surface-800/80 space-y-2 shadow-sm">
                <p className="text-xs font-semibold text-content-primary">Copy this link now — it is shown only once.</p>
                <div className="flex items-center gap-2">
                  <code className="flex-1 text-xs font-mono bg-surface-900 border border-line rounded-[var(--radius-control)] px-3 py-2 overflow-x-auto whitespace-nowrap text-content-primary">
                    {inviteUrl}
                  </code>
                  <button
                    type="button"
                    onClick={handleCopy}
                    className="tap-44 p-2 rounded-full border border-line bg-surface-700 hover:bg-surface-600 transition-colors text-content-primary cursor-pointer"
                    aria-label="Copy invite link"
                  >
                    {copied ? <Check className="w-3.5 h-3.5 text-content-primary" /> : <Copy className="w-3.5 h-3.5" />}
                  </button>
                </div>
                <p className="text-[10px] text-content-muted font-mono">
                  Single use — it stops working the moment someone joins with it.
                </p>
              </div>
            )}

            {/* Üyeler */}
            <div>
              <h3 className="text-[10px] uppercase tracking-wider text-content-muted font-mono font-semibold mb-2">Members</h3>
              <div className="border border-line-strong rounded-[var(--radius-card)] divide-y divide-line overflow-hidden bg-surface-800/40 shadow-sm">
                {team.members.map(m => {
                  const Icon = roleIcon[m.role] ?? Eye;
                  return (
                    <div key={m.userId} className="flex items-center gap-3 px-4 py-3">
                      <div className="w-8 h-8 rounded-full bg-surface-700 border border-line flex items-center justify-center text-xs font-mono font-bold text-content-primary shrink-0">
                        {m.username.substring(0, 2).toUpperCase()}
                      </div>
                      <div className="min-w-0 flex-1">
                        <p className="text-xs font-semibold text-content-primary truncate">
                          {m.username}
                          {m.isYou && <span className="text-content-muted font-normal"> · you</span>}
                        </p>
                        <p className="text-[11px] text-content-muted truncate">{m.email}</p>
                      </div>
                      <span className="flex items-center gap-1.5 px-2 py-0.5 rounded-full border border-line bg-surface-800 text-[10px] font-mono text-content-secondary shrink-0">
                        <Icon className="w-3 h-3 text-content-primary" /> {m.role}
                      </span>
                    </div>
                  );
                })}
              </div>
            </div>

            {/* Bekleyen davetler */}
            {team.pendingInvites.length > 0 && (
              <div>
                <h3 className="text-[10px] uppercase tracking-wider text-content-muted font-mono font-semibold mb-2">
                  Pending invites
                </h3>
                <p className="text-[11px] text-content-muted mb-2">
                  A pending invite holds a seat until it is used or revoked.
                </p>
                <div className="border border-line-strong rounded-[var(--radius-card)] divide-y divide-line overflow-hidden bg-surface-800/40 shadow-sm">
                  {team.pendingInvites.map(i => (
                    <div key={i.id} className="flex items-center gap-3 px-4 py-3">
                      <Clock className="w-3.5 h-3.5 text-content-muted shrink-0" />
                      <div className="min-w-0 flex-1">
                        <p className="text-xs font-medium text-content-primary">{i.role} invite</p>
                        <p className="text-[10px] text-content-muted font-mono">
                          expires {new Date(i.expiresAt).toLocaleDateString('en-US')}
                        </p>
                      </div>
                      <button
                        type="button"
                        onClick={() => handleRevoke(i.id)}
                        className="tap-44 p-1.5 rounded-full text-content-muted hover:text-danger-text hover:bg-surface-700 transition-colors cursor-pointer"
                        aria-label="Revoke invite"
                      >
                        <Trash2 className="w-3.5 h-3.5" />
                      </button>
                    </div>
                  ))}
                </div>
              </div>
            )}

            {/* Ortak projeler / kim ne yaptı */}
            <div>
              <div className="flex items-center justify-between gap-2 mb-2">
                <h3 className="text-[10px] uppercase tracking-wider text-content-muted font-mono font-semibold">
                  Shared projects ({projects.length})
                </h3>
                {activeProjectId && !projects.some(p => p.id === activeProjectId) && (
                  <button
                    type="button"
                    disabled={isSharing}
                    onClick={() => handleShareProject(activeProjectId)}
                    className="inline-flex items-center gap-1.5 px-2.5 py-1 rounded-full border border-line-strong bg-surface-800 hover:bg-surface-700 text-content-primary text-[11px] font-medium transition-colors cursor-pointer disabled:opacity-50"
                  >
                    {isSharing ? <Loader2 className="w-3 h-3 animate-spin" /> : <Share2 className="w-3 h-3" />}
                    <span>Share active project</span>
                  </button>
                )}
              </div>
              {projects.length === 0 ? (
                <p className="text-xs text-content-muted border border-line-strong rounded-[var(--radius-card)] px-4 py-6 text-center bg-surface-800/30">
                  No shared projects yet. Anything anyone saves shows up here for the whole team.
                </p>
              ) : (
                <div className="border border-line-strong rounded-[var(--radius-card)] divide-y divide-line overflow-hidden bg-surface-800/40 shadow-sm">
                  {projects.map(p => (
                    <div key={p.id} className="flex items-center justify-between gap-3 px-4 py-3 hover:bg-surface-800/80 transition-colors">
                      <div className="min-w-0 flex-1">
                        <p className="text-xs font-semibold text-content-primary truncate">{p.name}</p>
                        <p className="text-[10px] text-content-muted font-mono truncate">
                          {p.dbType} · {p.ownerName} · {new Date(p.updatedAt).toLocaleString('en-US')}
                        </p>
                      </div>
                      <button
                        type="button"
                        onClick={() => handleOpenSharedProject(p.id)}
                        className="inline-flex items-center gap-1.5 px-3 py-1.5 rounded-[var(--radius-control)] border border-line-strong bg-surface-700/80 hover:bg-surface-700 text-content-primary text-xs font-medium transition-colors cursor-pointer shrink-0"
                      >
                        <span>Open</span>
                        <ExternalLink className="w-3 h-3 text-content-secondary" />
                      </button>
                    </div>
                  ))}
                </div>
              )}

              {/* Henüz ekiple paylaşılmamış yerel projeler */}
              {localProjects.filter(lp => !projects.some(sp => sp.id === lp.id)).length > 0 && (
                <div className="mt-4">
                  <h4 className="text-[10px] uppercase tracking-wider text-content-muted font-mono font-semibold mb-2">
                    Your other projects (click to share with team)
                  </h4>
                  <div className="border border-line rounded-[var(--radius-card)] divide-y divide-line overflow-hidden bg-surface-850/40">
                    {localProjects
                      .filter(lp => !projects.some(sp => sp.id === lp.id))
                      .slice(0, 10)
                      .map(lp => (
                        <div key={lp.id} className="flex items-center justify-between gap-3 px-4 py-2.5">
                          <div className="min-w-0 flex-1">
                            <p className="text-xs font-medium text-content-primary truncate">{lp.name}</p>
                            <p className="text-[10px] text-content-muted font-mono truncate">
                              {lp.dbType} · {lp.schema?.tables?.length ?? 0} tables
                            </p>
                          </div>
                          <button
                            type="button"
                            disabled={isSharing}
                            onClick={() => handleShareProject(lp.id)}
                            className="inline-flex items-center gap-1.5 px-3 py-1 rounded-[var(--radius-control)] border border-line-strong bg-surface-800 hover:bg-surface-700 text-content-primary text-xs font-medium transition-colors cursor-pointer shrink-0 disabled:opacity-50"
                          >
                            <Share2 className="w-3 h-3" />
                            <span>Share</span>
                          </button>
                        </div>
                      ))}
                  </div>
                  {localProjects.filter(lp => !projects.some(sp => sp.id === lp.id)).length > 10 && (
                    <p className="mt-1.5 text-[10px] text-content-muted">
                      +{localProjects.filter(lp => !projects.some(sp => sp.id === lp.id)).length - 10} more
                    </p>
                  )}
                </div>
              )}
            </div>
          </div>
        )}
      </div>
    </div>
  );
}
