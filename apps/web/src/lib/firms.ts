import { request } from '@/lib/api';
import type { BranchSummary } from '@/lib/branches';

/** A firm, with its branches. */
export interface FirmSummary {
  readonly firmId: string;
  /** Fixed once made. */
  readonly code: string;
  readonly name: string;
  readonly nameArabic: string | null;
  /** The currency its books are kept in, fixed once made. */
  readonly baseCurrency: string;
  readonly taxRegistrationNumber: string | null;
  readonly isActive: boolean;
  /** How many users may work in it. */
  readonly userCount: number;
  /** Head office first. */
  readonly branches: readonly BranchSummary[];
}

export interface SaveFirm {
  /** Read when the firm is made; a firm keeps its code. */
  readonly code: string;
  readonly name: string;
  readonly nameArabic: string | null;
  /** Read when the firm is made; a firm keeps its currency. */
  readonly baseCurrency: string;
  readonly taxRegistrationNumber: string | null;
  readonly isActive: boolean;
}

const FIRMS = '/platform/firms';

/** Every firm with every branch, for the screens that manage them. */
export const listFirms = (): Promise<readonly FirmSummary[]> =>
  request<readonly FirmSummary[]>(FIRMS);

/** The firms the signed-in user may work in, each with only their branches. */
export const listMyFirms = (): Promise<readonly FirmSummary[]> =>
  request<readonly FirmSummary[]>(`${FIRMS}/mine`);

export const createFirm = (body: SaveFirm): Promise<FirmSummary> =>
  request<FirmSummary>(FIRMS, { method: 'POST', body });

export const updateFirm = (firmId: string, body: SaveFirm): Promise<FirmSummary> =>
  request<FirmSummary>(`${FIRMS}/${firmId}`, { method: 'PUT', body });
