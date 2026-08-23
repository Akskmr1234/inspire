import { request } from '@/lib/api';

/**
 * The sales documents of section 12.
 *
 * An invoice and a credit note are one kind of document running in opposite directions,
 * which is why there is one set of endpoints and one screen rather than two of each. The
 * numbers are the server's enums, which the wire carries as integers.
 */
export const SalesDocumentKind = { invoice: 1, return: 2 } as const;

/** Where a sales document stands. */
export const SalesInvoiceStatus = { draft: 1, posted: 2, cancelled: 3 } as const;

/** How a document treats tax. */
export const TaxMode = { nonTax: 1, tax: 2, cst: 3 } as const;

/** One page of a longer list. */
export interface PagedResult<TRow> {
  readonly items: readonly TRow[];
  readonly page: number;
  readonly pageSize: number;
  readonly totalCount: number;
  readonly totalPages: number;
  readonly hasMore: boolean;
}

/** A sales document as the list shows it. */
export interface SalesInvoiceSummary {
  readonly salesInvoiceId: string;
  readonly number: string;
  readonly kind: number;
  readonly date: string;
  readonly customerLedgerId: string;
  readonly customerCode: string;
  readonly customerName: string;
  readonly status: number;
  readonly currency: string;
  readonly referenceNumber: string | null;
  readonly lineCount: number;
  readonly taxable: number;
  readonly tax: number;
  readonly total: number;
}

/** One tax head as it was charged on a line. */
export interface SalesInvoiceLineTax {
  readonly component: number;
  readonly percentage: number;
  readonly amount: number;
}

/** One line of a document, as read back. */
export interface SalesInvoiceLineDetail {
  readonly lineNumber: number;
  readonly productId: string;
  readonly batchId: string | null;
  readonly unitId: string;
  readonly quantity: number;
  readonly stockQuantity: number;
  readonly rate: number;
  readonly discount: number;
  readonly taxable: number;
  readonly tax: number;
  readonly components: readonly SalesInvoiceLineTax[];
  readonly serialNumberIds: readonly string[];
}

/** A charge carried beside the goods. */
export interface SalesInvoiceChargeDetail {
  readonly ledgerId: string;
  readonly amount: number;
  readonly isAddition: boolean;
}

/** The header figures of a document. */
export interface SalesInvoiceHeader {
  readonly salesInvoiceId: string;
  readonly number: string;
  readonly status: number;
  readonly taxable: number;
  readonly tax: number;
  readonly chargeTotal: number;
  readonly roundingDifference: number;
  readonly total: number;
}

/** A document in full. */
export interface SalesInvoiceDetail {
  readonly header: SalesInvoiceHeader;
  readonly date: string;
  readonly customerLedgerId: string;
  readonly warehouseId: string;
  readonly mode: number;
  readonly currency: string;
  readonly referenceNumber: string | null;
  readonly narration: string | null;
  readonly lines: readonly SalesInvoiceLineDetail[];
  readonly charges: readonly SalesInvoiceChargeDetail[];
  readonly stockDocumentId: string | null;
  readonly billId: string | null;
  readonly journalVoucherId: string | null;
}

/** What posting produced. */
export interface PostSalesInvoiceResponse {
  readonly salesInvoiceId: string;
  readonly number: string;
  readonly stockDocumentId: string;
  readonly stockDocumentNumber: string;
  readonly billId: string | null;
  readonly journalVoucherId: string;
  readonly total: number;
}

/** One product on a document being entered. */
export interface SalesLineInput {
  readonly productId: string;
  readonly quantity: number;
  readonly rate: number;
  /**
   * The rate tax is charged at, supplied per line.
   *
   * Section 12.4 says it defaults from the product master, which carries no tax rate —
   * so the screen asks for it and the engine still decides which heads it splits into.
   */
  readonly taxPercentage: number;
  readonly unitId?: string | null;
  readonly discount?: number;
  readonly batchNumber?: string | null;
  readonly serialNumbers?: readonly string[];
}

/** What the list is narrowed by. */
export interface SalesFilter {
  readonly from?: string;
  readonly to?: string;
  readonly kind?: number | '';
  readonly status?: number | '';
  readonly customerLedgerId?: string;
  readonly search?: string;
}

/** Whether a kind of document gives goods back rather than selling them. */
export function isReturn(kind: number): boolean {
  return kind === SalesDocumentKind.return;
}

/** Lists sales documents, newest first. One page at a time. */
export async function listSalesInvoices(
  filter: SalesFilter,
  page: number,
  pageSize = 25,
): Promise<PagedResult<SalesInvoiceSummary>> {
  const query = new URLSearchParams({
    page: String(page),
    pageSize: String(pageSize),
  });

  if (filter.from) query.set('from', filter.from);
  if (filter.to) query.set('to', filter.to);
  if (filter.kind !== '' && filter.kind !== undefined)
    query.set('kind', String(filter.kind));
  if (filter.status !== '' && filter.status !== undefined) {
    query.set('status', String(filter.status));
  }
  if (filter.customerLedgerId) query.set('customerLedgerId', filter.customerLedgerId);
  if (filter.search?.trim()) query.set('search', filter.search.trim());

  return request<PagedResult<SalesInvoiceSummary>>(`/sales/invoices?${query}`);
}

/** Reads one document, with its lines and what posting it produced. */
export async function getSalesInvoice(id: string): Promise<SalesInvoiceDetail> {
  return request<SalesInvoiceDetail>(`/sales/invoices/${id}`);
}

/** Enters a document as a draft. Nothing moves until it is posted. */
export async function createSalesInvoice(input: {
  readonly date: string;
  readonly customerLedgerId: string;
  readonly warehouseId: string;
  readonly lines: readonly SalesLineInput[];
  readonly kind: number;
  readonly returnsInvoiceId?: string | null;
  readonly referenceNumber?: string | null;
  readonly narration?: string | null;
}): Promise<SalesInvoiceHeader> {
  return request<SalesInvoiceHeader>('/sales/invoices', {
    method: 'POST',
    body: input,
  });
}

/** Posts a draft: the goods move, the debt is raised or credited, the books follow. */
export async function postSalesInvoice(
  id: string,
  creditDays?: number | null,
): Promise<PostSalesInvoiceResponse> {
  return request<PostSalesInvoiceResponse>(`/sales/invoices/${id}/post`, {
    method: 'POST',
    body: { creditDays: creditDays ?? null },
  });
}

/** Cancels a posted document, putting back everything posting it moved. */
export async function cancelSalesInvoice(id: string, reason: string): Promise<void> {
  await request<void>(`/sales/invoices/${id}/cancel`, {
    method: 'POST',
    body: { reason },
  });
}
