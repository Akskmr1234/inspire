import { request } from '@/lib/api';

const POS = '/sales/pos';

/** What a shift or a day took, by how it was paid. Returns are taken off. */
export interface PosTakings {
  readonly salesCount: number;
  readonly returnsCount: number;
  readonly salesTotal: number;
  readonly returnsTotal: number;
  readonly cashTotal: number;
  readonly cardTotal: number;
  readonly onlineTotal: number;
  readonly creditTotal: number;
  readonly netTotal: number;
}

/** A cashier's shift. */
export interface PosShift {
  readonly shiftId: string;
  readonly businessDate: string;
  readonly openedAtUtc: string;
  readonly openingCash: number;
  readonly closedAtUtc: string | null;
  /** The float, plus the cash taken, less the cash refunded. */
  readonly expectedCash: number;
  readonly countedCash: number | null;
  readonly notes: string | null;
  readonly takings: PosTakings;
}

/** A trading day at the counter. */
export interface PosDay {
  readonly date: string;
  readonly takings: PosTakings;
  readonly openShifts: number;
  readonly isClosed: boolean;
  readonly closedAtUtc: string | null;
  readonly notes: string | null;
}

/** Where the counter stands for the signed-in cashier. */
export interface PosStatus {
  readonly today: string;
  readonly shift: PosShift | null;
  readonly dayClosed: boolean;
}

export const fetchPosStatus = (): Promise<PosStatus> =>
  request<PosStatus>(`${POS}/status`);

export const openPosShift = (openingCash: number): Promise<PosShift> =>
  request<PosShift>(`${POS}/shifts`, { method: 'POST', body: { openingCash } });

export const closePosShift = (
  shiftId: string,
  countedCash: number,
  notes: string | null,
): Promise<PosShift> =>
  request<PosShift>(`${POS}/shifts/${shiftId}/close`, {
    method: 'POST',
    body: { countedCash, notes },
  });

export const fetchPosDay = (date?: string): Promise<PosDay> =>
  request<PosDay>(`${POS}/day${date ? `?date=${date}` : ''}`);

export const closePosDay = (date: string, notes: string | null): Promise<PosDay> =>
  request<PosDay>(`${POS}/day/close`, { method: 'POST', body: { date, notes } });
