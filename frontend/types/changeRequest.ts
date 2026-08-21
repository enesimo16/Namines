import { ImpactReport, RiskLevel, MigrationResult } from './migration';

export type { RiskLevel, ImpactReport, MigrationResult };

// Backend: Namines.Core/Enums/ChangeRequestStatus.cs
export type ChangeRequestStatus = 'PendingReview' | 'Approved' | 'Rejected';

// Backend: Namines.Core/Enums/ApprovalDecision.cs
export type ApprovalDecision = 'Approved' | 'Rejected';

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

export interface ChangeRequestApprovalDto {
  id: string;
  userId: string;
  username: string | null;
  decision: ApprovalDecision;
  comment: string | null;
  createdAt: string;
}

export interface SchemaVersionSummary {
  id: string;
  version: number;
  tableCount: number;
  createdAt: string;
}

// Backend: Namines.Core/Models/TestRunResult.cs — "Run Tests" (G12)
export interface TestRunInfo {
  testRunSupported: boolean | null;
  testRunSuccess: boolean | null;
  testRunMessage: string | null;
  testRunFailedStatement: string | null;
  testRunDurationMs: number | null;
  testRunAt: string | null;
}

// Backend: Namines.Core/Models/AffectedCodeMatch.cs — "Affected Code" scan (G13)
export interface AffectedCodeMatch {
  fileName: string;
  lineNumber: number;
  matchedIdentifier: string;
  lineText: string;
}

export interface AffectedCodeScanResult {
  candidateIdentifiers: string[];
  matches: AffectedCodeMatch[];
  filesScanned: number;
}

// Backend: Namines.Core/Models/Auth/ChangeRequestAuditLog.cs — G16
export type ChangeRequestAuditAction = 'Created' | 'AutoApproved' | 'Approved' | 'Rejected';

export interface ChangeRequestAuditEntry {
  id: string;
  action: ChangeRequestAuditAction;
  actorUserId: string | null;
  actorUsername: string | null;
  details: string | null;
  createdAt: string;
}

export interface ChangeRequestDetail {
  id: string;
  projectId: string;
  branchId: string;
  branchName: string;
  title: string | null;
  status: ChangeRequestStatus;
  riskLevel: RiskLevel;
  createdByUserId: string;
  createdAt: string;
  resolvedAt: string | null;
  headVersion: SchemaVersionSummary;
  baseVersion: SchemaVersionSummary | null;
  impact: ImpactReport;
  aiExplanation: string | null;
  migration: MigrationResult | null;
  testRun: TestRunInfo | null;
  requiredApprovals: number;
  approvedCount: number;
  rejectedCount: number;
  approvals: ChangeRequestApprovalDto[];
}
