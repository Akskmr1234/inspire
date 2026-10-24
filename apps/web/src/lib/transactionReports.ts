import { request } from '@/lib/api';

/** Which transactions a report reads. */
export type TransactionDocument = 'Sales' | 'Purchase';

/** What a report's rows are. */
export type TransactionGrouping =
  'Month' | 'Day' | 'Bill' | 'Item' | 'Brand' | 'Category' | 'Salesman' | 'Party';

/** One row of a transaction report. */
export interface TransactionReportRow {
  readonly key: string;
  readonly label: string;
  readonly detail: string | null;
  readonly bills: number;
  readonly quantity: number;
  readonly amount: number;
  readonly tax: number;
  readonly others: number;
  readonly total: number;
  /** On sales only: what the goods cost, the profit on them, and its share. */
  readonly cost: number | null;
  readonly profit: number | null;
  readonly marginPercent: number | null;
}

/** A transaction report: its rows and their totals. */
export interface TransactionReport {
  readonly from: string;
  readonly to: string;
  readonly currency: string;
  readonly groupBy: TransactionGrouping;
  readonly rows: readonly TransactionReportRow[];
  readonly totals: TransactionReportRow;
}

/** The filters a transaction report may be narrowed by. Blank means every one. */
export interface TransactionFilters {
  readonly partyLedgerId: string;
  readonly salesmanLedgerId: string;
  readonly productId: string;
  readonly mode: string;
  readonly warehouseId: string;
  readonly brandId: string;
  readonly categoryId: string;
  readonly areaId: string;
  readonly routeId: string;
  readonly taxPercentage: string;
  readonly serialNumber: string;
}

/** No filter at all. */
export const NO_FILTERS: TransactionFilters = {
  partyLedgerId: '',
  salesmanLedgerId: '',
  productId: '',
  mode: '',
  warehouseId: '',
  brandId: '',
  categoryId: '',
  areaId: '',
  routeId: '',
  taxPercentage: '',
  serialNumber: '',
};

/** Reads sales or purchases over a period, added up by what is asked. */
export function fetchTransactionReport(
  document: TransactionDocument,
  from: string,
  to: string,
  groupBy: TransactionGrouping,
  filters: TransactionFilters,
): Promise<TransactionReport> {
  const query = new URLSearchParams({ document, from, to, groupBy });

  for (const [key, value] of Object.entries(filters)) {
    if (typeof value === 'string' && value.trim() !== '') {
      query.set(key, value.trim());
    }
  }

  return request<TransactionReport>(`/reports/transactions?${query.toString()}`);
}
