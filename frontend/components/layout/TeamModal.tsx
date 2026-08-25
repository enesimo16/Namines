'use client';

import { useEffect, useRef, useState } from 'react';
import { Users, Link2, Copy, Check, Trash2, Loader2, X, Clock, Crown, ShieldCheck, Eye, Pencil } from 'lucide-react';
import { teamService } from '../../services/api';
import { TeamStatus, CreatedInvite, TeamProject } from '../../types/team';
import { useToastStore } from '../../store/useToastStore';
import { useFocusTrap } from '../../hooks/useFocusTrap';

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
  const [team, setTeam] = useState<TeamStatus | null>(null);
  const [projects, setProjects] = useState<TeamProject[]>([]);
  const [isLoading, setIsLoading] = useState(false);
  const [isCreating, setIsCreating] = useState(false);
  // Yeni üretilen bağlantı yalnızca bu state'te yaşıyor: sunucuda özeti
  // saklandığı için modal kapanınca bir daha gösterilemez.
  const [fresh, setFresh] = useState<CreatedInvite | null>(null);
  const [copied, setCopied] = useState(false);
  const showToast = useToastStore(s => s.showToast);
  const modalRef = useRef<HTMLDivElement>(null);
  useFocusTrap(isOpen, modalRef);

  const load = async () => {
    setIsLoading(true);
    try {
      const [status, act] = await Promise.all([teamService.status(), teamService.activity()]);
      setTeam(status);
      setProjects(act.projects);
    } catch {
      showToast('Team information could not be loaded.', 'error');
    } finally {
      setIsLoading(false);
    }
  };

  useEffect(() => {
    if (!isOpen) return;
    setFresh(null);
    setCopied(false);
    load();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [isOpen]);

  if (!isOpen) return null;

  const inviteUrl = fresh ? `${window.location.origin}/join/${fresh.token}` : '';

  const handleCreate = async () => {
    setIsCreating(true);
    try {
      const created = await teamService.createInvite('Editor', 7);
      setFresh(created);
      setCopied(false);
      await load();
    } catch (err: any) {
      showToast(err?.response?.data?.error ?? 'Invite link could not be created.', 'error');
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

  return (
    <div className="fixed inset-0 z-[95] flex items-center justify-center p-4">
      <div className="absolute inset-0 bg-surface-900/80 backdrop-blur-sm" onClick={onClose} />

      <div
        ref={modalRef}
        role="dialog"
        aria-modal="true"
        aria-labelledby="team-title"
        className="relative w-full max-w-3xl max-h-[88vh] overflow-y-auto bg-surface-800 border border-content-primary/15 rounded-2xl p-6 text-content-primary animate-in zoom-in-95 duration-200"
      >
        <div className="flex items-start justify-between mb-5">
          <div>
            <h2 id="team-title" className="text-base font-bold flex items-center gap-2">
              <Users className="w-4 h-4" /> Team
            </h2>
            <p className="text-[11px] text-content-muted mt-1">
              {team?.teamEnabled
                ? 'Everyone here shares the same workspace and projects.'
                : 'Working together is part of the Team plan.'}
            </p>
          </div>
          <button
            type="button"
            onClick={onClose}
            aria-label="Close"
            className="tap-44 p-1.5 rounded-lg text-content-muted hover:text-content-primary hover:bg-white/[0.06]"
          >
            <X className="w-4 h-4" />
          </button>
        </div>

        {isLoading && !team ? (
          <div className="flex items-center gap-2 text-xs text-content-muted py-10 justify-center">
            <Loader2 className="w-4 h-4 animate-spin" /> Loading team…
          </div>
        ) : !team?.teamEnabled ? (
          <div className="space-y-4">
            <div className="border border-content-primary/15 rounded-xl p-5 bg-surface-700">
              <p className="text-sm font-semibold mb-2">Your plan is single-seat</p>
              <p className="text-[11px] text-content-secondary leading-relaxed">
                The Team plan gives you <strong className="text-content-primary">3 seats</strong> — you plus two
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
              className="w-full bg-content-primary hover:bg-content-secondary text-surface-900 font-semibold py-2.5 rounded-xl text-sm transition-colors"
            >
              See plans
            </button>
          </div>
        ) : (
          <div className="space-y-6">
            {/* Koltuklar */}
            <div className="flex items-center justify-between border border-content-primary/15 rounded-xl px-4 py-3 bg-surface-700">
              <div>
                <p className="text-[10px] uppercase tracking-wider text-content-subtle font-semibold">Seats</p>
                <p className="text-sm font-bold mt-0.5">
                  {team.seats.total < 0
                    ? `${team.seats.used} used · unlimited`
                    : `${team.seats.used} of ${team.seats.total} used`}
                </p>
              </div>
              <button
                type="button"
                onClick={handleCreate}
                disabled={isCreating || team.seats.available === 0}
                className="flex items-center gap-2 bg-content-primary hover:bg-content-secondary text-surface-900 font-semibold px-4 py-2 rounded-lg text-xs transition-colors disabled:opacity-40 disabled:cursor-not-allowed"
              >
                {isCreating ? <Loader2 className="w-3.5 h-3.5 animate-spin" /> : <Link2 className="w-3.5 h-3.5" />}
                {team.seats.available === 0 ? 'No seats left' : 'Create invite link'}
              </button>
            </div>

            {/* Yeni bağlantı — yalnızca bir kez gösterilir */}
            {fresh && (
              <div className="border border-content-primary/25 rounded-xl p-4 bg-white/[0.04] space-y-2">
                <p className="text-[11px] font-semibold">Copy this link now — it is shown only once.</p>
                <div className="flex items-center gap-2">
                  <code className="flex-1 text-[10px] bg-surface-900 rounded-lg px-3 py-2 overflow-x-auto whitespace-nowrap">
                    {inviteUrl}
                  </code>
                  <button
                    type="button"
                    onClick={handleCopy}
                    className="tap-44 p-2 rounded-lg bg-white/[0.08] hover:bg-white/[0.14] transition-colors"
                    aria-label="Copy invite link"
                  >
                    {copied ? <Check className="w-3.5 h-3.5" /> : <Copy className="w-3.5 h-3.5" />}
                  </button>
                </div>
                <p className="text-[10px] text-content-muted">
                  Single use — it stops working the moment someone joins with it.
                </p>
              </div>
            )}

            {/* Üyeler */}
            <div>
              <h3 className="text-[10px] uppercase tracking-wider text-content-subtle font-semibold mb-2">Members</h3>
              <div className="border border-content-primary/15 rounded-xl divide-y divide-content-primary/10 overflow-hidden">
                {team.members.map(m => {
                  const Icon = roleIcon[m.role] ?? Eye;
                  return (
                    <div key={m.userId} className="flex items-center gap-3 px-4 py-2.5">
                      <div className="w-7 h-7 rounded-full bg-white/[0.08] flex items-center justify-center text-[10px] font-bold shrink-0">
                        {m.username.substring(0, 2).toUpperCase()}
                      </div>
                      <div className="min-w-0 flex-1">
                        <p className="text-xs font-semibold truncate">
                          {m.username}
                          {m.isYou && <span className="text-content-muted font-normal"> · you</span>}
                        </p>
                        <p className="text-[10px] text-content-muted truncate">{m.email}</p>
                      </div>
                      <span className="flex items-center gap-1.5 text-[10px] font-semibold text-content-secondary shrink-0">
                        <Icon className="w-3 h-3" /> {m.role}
                      </span>
                    </div>
                  );
                })}
              </div>
            </div>

            {/* Bekleyen davetler */}
            {team.pendingInvites.length > 0 && (
              <div>
                <h3 className="text-[10px] uppercase tracking-wider text-content-subtle font-semibold mb-2">
                  Pending invites
                </h3>
                <p className="text-[10px] text-content-muted mb-2">
                  A pending invite holds a seat until it is used or revoked.
                </p>
                <div className="border border-content-primary/15 rounded-xl divide-y divide-content-primary/10 overflow-hidden">
                  {team.pendingInvites.map(i => (
                    <div key={i.id} className="flex items-center gap-3 px-4 py-2.5">
                      <Clock className="w-3.5 h-3.5 text-content-muted shrink-0" />
                      <div className="min-w-0 flex-1">
                        <p className="text-xs font-medium">{i.role} invite</p>
                        <p className="text-[10px] text-content-muted">
                          expires {new Date(i.expiresAt).toLocaleDateString()}
                        </p>
                      </div>
                      <button
                        type="button"
                        onClick={() => handleRevoke(i.id)}
                        className="tap-44 p-1.5 rounded-lg text-content-muted hover:text-danger hover:bg-white/[0.06] transition-colors"
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
              <h3 className="text-[10px] uppercase tracking-wider text-content-subtle font-semibold mb-2">
                Shared projects
              </h3>
              {projects.length === 0 ? (
                <p className="text-[11px] text-content-muted border border-content-primary/15 rounded-xl px-4 py-5 text-center">
                  No shared projects yet. Anything anyone saves shows up here for the whole team.
                </p>
              ) : (
                <div className="border border-content-primary/15 rounded-xl divide-y divide-content-primary/10 overflow-hidden">
                  {projects.map(p => (
                    <div key={p.id} className="flex items-center gap-3 px-4 py-2.5">
                      <div className="min-w-0 flex-1">
                        <p className="text-xs font-semibold truncate">{p.name}</p>
                        <p className="text-[10px] text-content-muted truncate">
                          {p.dbType} · {p.ownerName} · {new Date(p.updatedAt).toLocaleString()}
                        </p>
                      </div>
                    </div>
                  ))}
                </div>
              )}
            </div>
          </div>
        )}
      </div>
    </div>
  );
}
