import { DatabaseSchema } from './schema';

export enum ProjectVisibility {
  Private = 'Private',
  Team = 'Team',
  Public = 'Public',
}

export function normalizeVisibility(v: unknown): ProjectVisibility {
  if (v === 2 || v === '2' || v === 'Public' || v === ProjectVisibility.Public) return ProjectVisibility.Public;
  if (v === 1 || v === '1' || v === 'Team' || v === ProjectVisibility.Team) return ProjectVisibility.Team;
  return ProjectVisibility.Private;
}

export interface HubRepoSummary {
  id: string;
  name: string;
  slug: string | null;
  description: string | null;
  dbType: string;
  category: string;
  tags: string | null;
  starCount: number;
  forkCount: number;
  forkedFromProjectId: string | null;
  createdAt: string;
  updatedAt: string;
  owner: {
    id: string;
    username: string;
  };
  stats: {
    tableCount: number;
    relationCount: number;
  };
}

export interface HubExploreResponse {
  total: number;
  page: number;
  pageSize: number;
  items: HubRepoSummary[];
}

export interface HubRepoDetail {
  id: string;
  name: string;
  slug: string | null;
  description: string | null;
  visibility: ProjectVisibility;
  category: string;
  tags: string | null;
  dbType: string;
  starCount: number;
  forkCount: number;
  forkedFromProjectId: string | null;
  forkedFrom: {
    id: string;
    name: string;
    slug: string | null;
    ownerUsername: string;
  } | null;
  linkedGithubRepo: string | null;
  autoApproveSafeChanges?: boolean;
  createdAt: string;
  updatedAt: string;
  owner: {
    id: string;
    username: string;
    email?: string;
  };
  caller: {
    isStarred: boolean;
    role: string | null;
    canEdit: boolean;
  };
  stats: {
    tableCount: number;
    relationCount: number;
    openIssuesCount: number;
    closedIssuesCount: number;
    openPrCount: number;
    closedPrCount: number;
  };
  branches: {
    id: string;
    name: string;
    isDefault: boolean;
    createdAt: string;
    /** Yalnızca yerel taslak repolarda dolu: dalın istemcideki şema anlık görüntüsü. */
    schema?: DatabaseSchema;
  }[];
  schema: DatabaseSchema;
  nodePositionsJson: string;
  /** Yalnızca istemcide: Hub'da karşılığı olmayan, kullanıcının yerel projesinden türetilmiş taslak. */
  isLocalDraft?: boolean;
}

export interface HubIssue {
  id: string;
  issueNumber: number;
  title: string;
  body: string;
  isClosed: boolean;
  relatedTableName: string | null;
  nslRuleCode: string | null;
  labels: string | null;
  createdAt: string;
  closedAt: string | null;
  createdBy: {
    id: string;
    username: string;
  };
  commentsCount: number;
}

export interface HubIssueComment {
  id: string;
  body: string;
  createdAt: string;
  author: {
    id: string;
    username: string;
  };
}

export interface HubIssuesResponse {
  total: number;
  openCount: number;
  closedCount: number;
  page: number;
  pageSize: number;
  items: HubIssue[];
}
