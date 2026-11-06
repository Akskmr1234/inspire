import { request } from '@/lib/api';

/** A branch of the firm. */
export interface BranchSummary {
  readonly branchId: string;
  /** Fixed once made: documents are numbered by it. */
  readonly code: string;
  readonly name: string;
  readonly nameArabic: string | null;
  readonly isHeadOffice: boolean;
  readonly isActive: boolean;
  readonly address: string | null;
  readonly phone: string | null;
  readonly email: string | null;
  /** How many users may work in it. */
  readonly userCount: number;
}

export interface SaveBranch {
  /** Read when the branch is made; a branch keeps its code. */
  readonly code: string;
  readonly name: string;
  readonly nameArabic: string | null;
  readonly address: string | null;
  readonly phone: string | null;
  readonly email: string | null;
  readonly isActive: boolean;
}

const BRANCHES = '/platform/branches';

/** Every branch of the firm, for the screens that manage them. */
export const listBranches = (): Promise<readonly BranchSummary[]> =>
  request<readonly BranchSummary[]>(BRANCHES);

/** The branches the signed-in user may work in, for the switcher. */
export const listMyBranches = (): Promise<readonly BranchSummary[]> =>
  request<readonly BranchSummary[]>(`${BRANCHES}/mine`);

export const createBranch = (body: SaveBranch): Promise<BranchSummary> =>
  request<BranchSummary>(BRANCHES, { method: 'POST', body });

export const updateBranch = (
  branchId: string,
  body: SaveBranch,
): Promise<BranchSummary> =>
  request<BranchSummary>(`${BRANCHES}/${branchId}`, { method: 'PUT', body });
