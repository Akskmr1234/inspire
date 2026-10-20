import { request } from '@/lib/api';

/** Whether a year takes postings, is closed, or is locked for good. */
export const FinancialYearStatus = { open: 1, closed: 2, locked: 3 } as const;

/** A financial year: the period every document posts into. */
export interface FinancialYearSummary {
  readonly financialYearId: string;
  readonly code: string;
  readonly startDate: string;
  readonly endDate: string;
  readonly status: number;
}

/** A year's label and dates, as entered. */
export interface FinancialYearInput {
  readonly code: string;
  readonly startDate: string;
  readonly endDate: string;
}

const YEARS = '/platform/financial-years';

/** Lists the firm's financial years, earliest first. */
export function listFinancialYears(): Promise<readonly FinancialYearSummary[]> {
  return request<readonly FinancialYearSummary[]>(YEARS);
}

/** Adds a year. */
export function createFinancialYear(
  input: FinancialYearInput,
): Promise<FinancialYearSummary> {
  return request<FinancialYearSummary>(YEARS, { method: 'POST', body: input });
}

/** Renames a year or moves its dates. */
export function updateFinancialYear(
  id: string,
  input: FinancialYearInput,
): Promise<FinancialYearSummary> {
  return request<FinancialYearSummary>(`${YEARS}/${id}`, { method: 'PUT', body: input });
}

/** Closes a year to postings, or opens it again. */
export function setFinancialYearOpen(
  id: string,
  open: boolean,
): Promise<FinancialYearSummary> {
  return request<FinancialYearSummary>(`${YEARS}/${id}/open`, {
    method: 'POST',
    body: { open },
  });
}
