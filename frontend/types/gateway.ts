// Backend: Namines.Core/Models/GatewayModels.cs — G14 Minimal Gateway (read-only)
export interface GatewayRow {
  values: Record<string, unknown>;
}

export interface GatewayListResult {
  rows: GatewayRow[];
  page: number;
  pageSize: number;
  totalCount: number;
}
