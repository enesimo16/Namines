'use client';

import { useEffect, useState } from 'react';
import { Users, UserPlus, Trash2, Loader2, ChevronDown, Info } from 'lucide-react';
import { memberService } from '../../services/api';
import { useToastStore } from '../../store/useToastStore';
import {
  ProjectMember, OrgRole, ORG_ROLE_LABEL, ORG_ROLE_HINT, ASSIGNABLE_ROLES, VOTING_ROLES,
} from '../../types/member';

interface Props {
  projectId: string;
  /** Kimliğim e-postayla eşleşiyor: UserProfile'da id alanı yok ve auth
   *  sözleşmesini bunun için değiştirmek gereksiz — üye listesi e-posta döndürüyor. */
  currentUserEmail: string | null;
}

/**
 * 05 §6 — proje ekibi yönetimi.
 *
 * Neden görünür bir yerde: new-phase/29 §3'ün "Destructive/Breaking → 2 farklı kişi
 * onaylamalı" kuralı, ekipte kimse yoksa uygulanamaz. Kullanıcı ekibini buradan
 * kurmadan yüksek riskli bir change request'i geçiremez, o yüzden panel review
 * listesinin hemen yanında duruyor.
 */
export default function TeamPanel({ projectId, currentUserEmail }: Props) {
  const showToast = useToastStore(s => s.showToast);

  const [members, setMembers] = useState<ProjectMember[] | null>(null);
  const [email, setEmail] = useState('');
  const [role, setRole] = useState<OrgRole>('Editor');
  const [isAdding, setIsAdding] = useState(false);
  const [busyUserId, setBusyUserId] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  const load = () => {
    memberService.list(projectId)
      .then(setMembers)
      .catch(() => setMembers([]));
  };

  useEffect(load, [projectId]);

  const me = members?.find(m => m.email != null && m.email === currentUserEmail);
  const canManage = me?.role === 'Admin' || me?.role === 'Owner';
  // Oy verebilenler = Editor+ (Viewer/Billing oy veremez, backend de böyle sayıyor).
  const votingCount = (members ?? []).filter(m => VOTING_ROLES.includes(m.role)).length;

  const handleAdd = async () => {
    if (!email.trim()) return;
    setIsAdding(true);
    setError(null);
    try {
      await memberService.add(projectId, email.trim(), role);
      setEmail('');
      showToast('Team member added.', 'success');
      load();
    } catch (err: any) {
      setError(err?.response?.data?.error ?? 'Could not add the member.');
    } finally {
      setIsAdding(false);
    }
  };

  const handleRoleChange = async (userId: string, next: OrgRole) => {
    setBusyUserId(userId);
    try {
      await memberService.changeRole(projectId, userId, next);
      load();
    } catch (err: any) {
      showToast(err?.response?.data?.error ?? 'Could not change the role.', 'error');
    } finally {
      setBusyUserId(null);
    }
  };

  const handleRemove = async (userId: string) => {
    setBusyUserId(userId);
    try {
      await memberService.remove(projectId, userId);
      showToast('Member removed.', 'info');
      load();
    } catch (err: any) {
      showToast(err?.response?.data?.error ?? 'Could not remove the member.', 'error');
    } finally {
      setBusyUserId(null);
    }
  };

  return (
    <div className="bg-surface-700 border border-surface-500 rounded-[var(--radius-card)] overflow-hidden">
      <div className="flex items-center justify-between gap-3 px-4 h-11 border-b border-surface-500 bg-surface-800">
        <div className="flex items-center gap-2">
          <Users className="w-4 h-4 text-accent-text" />
          <span className="text-[12px] font-semibold text-content-primary">Team</span>
          {members && (
            <span className="text-[10px] font-mono text-content-subtle">
              {members.length} member{members.length === 1 ? '' : 's'}
            </span>
          )}
        </div>
      </div>

      {/* Onay kuralının ekip büyüklüğüne göre ne anlama geldiğini AÇIKÇA söyle —
          kullanıcı "neden hâlâ onaylanmadı" sorusuyla baş başa kalmasın. */}
      <div className="flex items-start gap-2 px-4 py-2.5 border-b border-surface-500 bg-accent-subtle/40">
        <Info className="w-3.5 h-3.5 text-accent-text shrink-0 mt-px" />
        <p className="text-[11px] text-content-secondary leading-relaxed">
          {votingCount <= 1 ? (
            <>You&apos;re the only person who can vote, so high-risk changes are approved by your own vote. Add a teammate to require a second pair of eyes.</>
          ) : votingCount === 2 ? (
            <>With two voters, a high-risk change needs <strong className="text-content-primary">1 approval</strong> from the other person — an author still can&apos;t approve their own change.</>
          ) : (
            <>With {votingCount} voters, a high-risk change needs <strong className="text-content-primary">2 approvals</strong>, and never from the author.</>
          )}
        </p>
      </div>

      {members === null ? (
        <div className="flex items-center justify-center py-8">
          <Loader2 className="w-4 h-4 text-content-muted animate-spin" />
        </div>
      ) : (
        <>
          <ul className="divide-y divide-surface-500/40">
            {members.map(m => {
              const isSelf = m.email != null && m.email === currentUserEmail;
              const isBusy = busyUserId === m.userId;
              return (
                <li key={m.userId} className="flex items-center gap-3 px-4 py-2.5">
                  <span className="flex items-center justify-center w-7 h-7 rounded-full bg-accent-subtle text-accent-text text-[11px] font-semibold shrink-0">
                    {(m.username ?? m.email ?? '?').slice(0, 1).toUpperCase()}
                  </span>

                  <div className="min-w-0 flex-1">
                    <p className="text-[12px] text-content-primary truncate">
                      {m.username ?? m.email}
                      {isSelf && <span className="text-content-subtle font-normal"> · you</span>}
                    </p>
                    <p className="text-[10px] text-content-subtle truncate">{m.email}</p>
                  </div>

                  {canManage && m.role !== 'Owner' ? (
                    <div className="relative shrink-0">
                      <select
                        value={m.role}
                        disabled={isBusy}
                        onChange={e => handleRoleChange(m.userId, e.target.value as OrgRole)}
                        aria-label={`Role for ${m.username ?? m.email}`}
                        title={ORG_ROLE_HINT[m.role]}
                        className="appearance-none bg-surface-600 border border-surface-500 rounded-[var(--radius-control)] h-7 pl-2 pr-6 text-[11px] text-content-secondary cursor-pointer focus:outline-none focus-visible:ring-1 focus-visible:ring-[var(--color-focus-ring)] disabled:opacity-50"
                      >
                        {ASSIGNABLE_ROLES.map(r => (
                          <option key={r} value={r}>{ORG_ROLE_LABEL[r]}</option>
                        ))}
                      </select>
                      <ChevronDown className="w-3 h-3 text-content-subtle absolute right-1.5 top-2 pointer-events-none" />
                    </div>
                  ) : (
                    <span
                      title={ORG_ROLE_HINT[m.role]}
                      className="shrink-0 text-[10px] font-semibold uppercase tracking-wider px-2 py-1 rounded-[var(--radius-control)] bg-surface-600 text-content-muted"
                    >
                      {ORG_ROLE_LABEL[m.role]}
                    </span>
                  )}

                  {canManage && m.role !== 'Owner' && (
                    <button
                      onClick={() => handleRemove(m.userId)}
                      disabled={isBusy}
                      aria-label={`Remove ${m.username ?? m.email}`}
                      title="Remove from team"
                      className="shrink-0 flex items-center justify-center w-7 h-7 rounded-[var(--radius-control)] text-content-subtle hover:text-danger-text hover:bg-danger-subtle transition-colors cursor-pointer disabled:opacity-40"
                    >
                      {isBusy ? <Loader2 className="w-3.5 h-3.5 animate-spin" /> : <Trash2 className="w-3.5 h-3.5" />}
                    </button>
                  )}
                </li>
              );
            })}
          </ul>

          {canManage && (
            <div className="px-4 py-3 border-t border-surface-500 bg-surface-800/60">
              <div className="flex items-center gap-2">
                <input
                  type="email"
                  value={email}
                  onChange={e => { setEmail(e.target.value); setError(null); }}
                  onKeyDown={e => { if (e.key === 'Enter') handleAdd(); }}
                  placeholder="teammate@example.com"
                  aria-label="Teammate email"
                  className="flex-1 min-w-0 bg-surface-600 border border-surface-500 rounded-[var(--radius-control)] h-8 px-2.5 text-[11px] text-content-primary placeholder-content-subtle focus:outline-none focus-visible:ring-1 focus-visible:ring-[var(--color-focus-ring)]"
                />
                <div className="relative shrink-0">
                  <select
                    value={role}
                    onChange={e => setRole(e.target.value as OrgRole)}
                    aria-label="Role for the new member"
                    className="appearance-none bg-surface-600 border border-surface-500 rounded-[var(--radius-control)] h-8 pl-2 pr-6 text-[11px] text-content-secondary cursor-pointer focus:outline-none focus-visible:ring-1 focus-visible:ring-[var(--color-focus-ring)]"
                  >
                    {ASSIGNABLE_ROLES.map(r => (
                      <option key={r} value={r}>{ORG_ROLE_LABEL[r]}</option>
                    ))}
                  </select>
                  <ChevronDown className="w-3 h-3 text-content-subtle absolute right-1.5 top-2.5 pointer-events-none" />
                </div>
                <button
                  onClick={handleAdd}
                  disabled={isAdding || !email.trim()}
                  className="shrink-0 inline-flex items-center gap-1.5 h-8 px-3 rounded-[var(--radius-control)] text-[11px] font-semibold bg-content-primary text-surface-900 hover:bg-content-primary-hover transition-colors cursor-pointer disabled:opacity-40 disabled:cursor-not-allowed"
                >
                  {isAdding ? <Loader2 className="w-3.5 h-3.5 animate-spin" /> : <UserPlus className="w-3.5 h-3.5" />}
                  Add
                </button>
              </div>

              {error && (
                <p className="mt-2 text-[11px] text-danger-text">{error}</p>
              )}
              <p className="mt-2 text-[10px] text-content-subtle">
                The person must already have a Namines account — email invitations aren&apos;t available yet.
              </p>
            </div>
          )}
        </>
      )}
    </div>
  );
}
