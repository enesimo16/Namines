export type LintSeverity = 0 | 1 | 2; // 0: Info, 1: Warning, 2: Error

export interface LintMessage {
  severity: LintSeverity;
  message: string;
  tableId?: string;
  columnId?: string;
}

export interface LintResult {
  messages: LintMessage[];
  hasErrors: boolean;
}
