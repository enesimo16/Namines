import type { DeskSession } from './api';

/**
 * D5 — Deployments (namines_desk/05-DEPLOYMENTS.md). "Deployment" kavramının
 * Namines'teki karşılığı ŞEMA SÜRÜMÜ: `ChangeRequest` + `SchemaVersion`.
 *
 * Bu ekran YENİ bir backend ucu gerektirmiyor — `ChangeRequestController`
 * zaten oturum (JWT) + `OrgAccess.CanViewAsync` ile korunan üç uç sunuyor
 * (Viewer dahil her org üyesi okuyabilir), Desk'in D1'deki oturum modeliyle
 * birebir örtüşüyor. Tipler ana uygulamadan KOPYALANMIYOR — yalnızca bu
 * ekranın gösterdiği alanlar burada ayrıca (bilinçli kopya) tanımlı.
 */

const API = process.env.NAMINES_API ?? 'http://localhost:5000';

async function get<T>(path: string, session: DeskSession): Promise<T> {
  const res = await fetch(`${API}${path}`, {
    headers: { Authorization: `Bearer ${session.token}` },
    cache: 'no-store',
  });
  if (!res.ok) throw new Error(`İstek başarısız (${res.status}).`);
  return res.json() as Promise<T>;
}

export type ChangeRequestStatus = 'PendingReview' | 'Approved' | 'Rejected';
export type RiskLevel = 'Safe' | 'Risky' | 'Destructive' | 'Breaking';
export type ChangeKind = 'Added' | 'Removed' | 'Modified' | 'RenamedFrom';

export interface ChangeRequestSummary {
  id: string;
  title: string | null;
  status: ChangeRequestStatus;
  riskLevel: RiskLevel;
  branchName: string;
  tableCount: number;
  createdAt: string;
  resolvedAt: string | null;
  approvedCount: number;
  requiredApprovals: number;
}

export interface AffectedTable { tableName: string; kind: ChangeKind; changedColumns: string[]; previousName: string | null; }
export interface BreakingChange { description: string; kind: string; tableName: string | null; columnName: string | null; suggestedMitigation: string | null; }
export interface DataLossRisk { tableName: string; columnName: string | null; reason: string; }
export interface MigrationLockRisk { operation: string; severity: string; tableName: string | null; saferAlternative: string | null; }
export interface ImpactReport {
  affectedTables: AffectedTable[];
  affectedRelations: unknown[];
  affectedIndexes: unknown[];
  breakingChanges: BreakingChange[];
  dataLossRisks: DataLossRisk[];
  lockRisks: MigrationLockRisk[];
  indexSuggestions: unknown[];
  rollback: { isReversible: boolean; reason: string | null };
  overallRisk: RiskLevel;
}

export interface ChangeRequestApproval {
  id: string; userId: string; username: string | null;
  decision: 'Approved' | 'Rejected'; comment: string | null; createdAt: string;
}

export interface ChangeRequestDetail extends ChangeRequestSummary {
  projectId: string;
  branchId: string;
  createdByUserId: string;
  headVersion: { id: string; version: number; tableCount: number; createdAt: string };
  baseVersion: { id: string; version: number; tableCount: number; createdAt: string } | null;
  impact: ImpactReport | null;
  aiExplanation: string | null;
  approvedCount: number;
  rejectedCount: number;
  requiredApprovals: number;
  approvals: ChangeRequestApproval[];
}

export interface AuditEntry {
  id: string; action: string; actorUserId: string | null; actorUsername: string | null;
  details: string | null; createdAt: string;
}

export const deploymentsApi = {
  list: (session: DeskSession, projectId: string) =>
    get<ChangeRequestSummary[]>(`/api/changerequest/project/${encodeURIComponent(projectId)}`, session),

  detail: (session: DeskSession, changeRequestId: string) =>
    get<ChangeRequestDetail>(`/api/changerequest/${encodeURIComponent(changeRequestId)}`, session),

  audit: (session: DeskSession, changeRequestId: string) =>
    get<AuditEntry[]>(`/api/changerequest/${encodeURIComponent(changeRequestId)}/audit`, session),
};
