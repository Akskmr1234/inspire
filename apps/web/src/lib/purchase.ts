import { request } from '@/lib/api';
import type { PagedResult } from '@/lib/sales';

/**
 * The purchase documents of section 13.
 *
 * A purchase and a debit note are one kind of document running in opposite directions,
 * which is why there is one set of endpoints and one screen rather than two of each. The
 * numbers are the server's enums, which the wire carries as integers.
 */
export const PurchaseDocumentKind = { invoice: 1, return: 2 } as const;

/** Where a purchase document stands. */
export const PurchaseInvoiceStatus = { draft: 1, posted: 2, cancelled: 3 } as const;

/** A purchase document as the list shows it. */
export interface PurchaseInvoiceSummary {
  readonly purchaseInvoiceId: string;
  readonly number: string;
  readonly kind: number;
  readonly date: string;
  readonly supplierLedgerId: string;
  readonly supplierCode: string;
  readonly supplierName: string;
  readonly status: number;
  readonly currency: string;
  readonly supplierInvoiceNumber: string | null;
  readonly supplierInvoiceDate: string | null;
  readonly lineCount: number;
  readonly taxable: number;
  readonly tax: number;
  readonly total: number;
}

/** One tax head as it was charged on a line. */
export interface PurchaseInvoiceLineTax {
  readonly component: number;
  readonly percentage: number;
  readonly amount: number;
}

/** One line of a document, as read back. */
export interface PurchaseInvoiceLineDetail {
  readonly lineNumber: number;
  readonly productId: string;
  /** A number rather than an identifier: the purchase is what opens the batch. */
  readonly batchNumber: string | null;
  readonly expiresOn: string | null;
  readonly unitId: string;
  readonly quantity: number;
  readonly stockQuantity: number;
  readonly rate: number;
  readonly discount: number;
  readonly taxable: number;
  readonly tax: number;
  readonly components: readonly PurchaseInvoiceLineTax[];
  readonly serialNumbers: readonly string[];
}

/** A charge carried beside the goods. */
export interface PurchaseInvoiceChargeDetail {
  readonly ledgerId: string;
  readonly amount: number;
  readonly isAddition: boolean;
}

/** The header figures of a document. */
export interface PurchaseInvoiceHeader {
  readonly purchaseInvoiceId: string;
  readonly number: string;
  readonly status: number;
  readonly taxable: number;
  readonly tax: number;
  readonly chargeTotal: number;
  readonly roundingDifference: number;
  readonly total: number;
}

/** A document in full. */
export interface PurchaseInvoiceDetail {
  readonly header: PurchaseInvoiceHeader;
  readonly date: string;
  readonly supplierLedgerId: string;
  readonly warehouseId: string;
  readonly mode: number;
  readonly currency: string;
  readonly supplierInvoiceNumber: string | null;
  readonly supplierInvoiceDate: string | null;
  readonly narration: string | null;
  readonly kind: number;
  readonly returnsInvoiceId: string | null;
  readonly lines: readonly PurchaseInvoiceLineDetail[];
  readonly charges: readonly PurchaseInvoiceChargeDetail[];
  readonly stockDocumentId: string | null;
  readonly billId: string | null;
  readonly journalVoucherId: string | null;
}

/** What posting produced. */
export interface PostPurchaseInvoiceResponse {
  readonly purchaseInvoiceId: string;
  readonly number: string;
  readonly stockDocumentId: string;
  readonly stockDocumentNumber: string;
  readonly billId: string | null;
  readonly journalVoucherId: string;
  readonly total: number;
}

/** One product on a document being entered. */
export interface PurchaseLineInput {
  readonly productId: string;
  readonly quantity: number;
  readonly rate: number;
  /**
   * The rate the supplier charged tax at, supplied per line.
   *
   * Read off their invoice rather than defaulted from the product master, which carries no
   * tax rate — see the note in the README.
   */
  readonly taxPercentage: number;
  readonly unitId?: string | null;
  readonly discount?: number;
  /** The batch the goods arrived in. Typed, not chosen: this is what opens it. */
  readonly batchNumber?: string | null;
  readonly expiresOn?: string | null;
  readonly serialNumbers?: readonly string[];
}

/** What the list is narrowed by. */
export interface PurchaseFilter {
  readonly from?: string;
  readonly to?: string;
  readonly kind?: number | '';
  readonly status?: number | '';
  readonly supplierLedgerId?: string;
  readonly search?: string;
}

/** Whether a kind of document sends goods back rather than buying them. */
export function isPurchaseReturn(kind: number): boolean {
  return kind === PurchaseDocumentKind.return;
}

/** Lists purchase documents, newest first. One page at a time. */
export async function listPurchaseInvoices(
  filter: PurchaseFilter,
  page: number,
  pageSize = 25,
): Promise<PagedResult<PurchaseInvoiceSummary>> {
  const query = new URLSearchParams({
    page: String(page),
    pageSize: String(pageSize),
  });

  if (filter.from) query.set('from', filter.from);
  if (filter.to) query.set('to', filter.to);
  if (filter.kind !== '' && filter.kind !== undefined) query.set('kind', String(filter.kind));
  if (filter.status !== '' && filter.status !== undefined) {
    query.set('status', String(filter.status));
  }
  if (filter.supplierLedgerId) query.set('supplierLedgerId', filter.supplierLedgerId);
  if (filter.search?.trim()) query.set('search', filter.search.trim());

  return request<PagedResult<PurchaseInvoiceSummary>>(`/purchase/invoices?${query}`);
}

/** Reads one document, with its lines and what posting it produced. */
export async function getPurchaseInvoice(id: string): Promise<PurchaseInvoiceDetail> {
  return request<PurchaseInvoiceDetail>(`/purchase/invoices/${id}`);
}

/** Enters a document as a draft. Nothing moves until it is posted. */
export async function createPurchaseInvoice(input: {
  readonly date: string;
  readonly supplierLedgerId: string;
  readonly warehouseId: string;
  readonly lines: readonly PurchaseLineInput[];
  readonly kind: number;
  readonly returnsInvoiceId?: string | null;
  readonly supplierInvoiceNumber?: string | null;
  readonly supplierInvoiceDate?: string | null;
  readonly narration?: string | null;
}): Promise<PurchaseInvoiceHeader> {
  return request<PurchaseInvoiceHeader>('/purchase/invoices', {
    method: 'POST',
    body: input,
  });
}

/** Posts a draft: the goods arrive, the debt is raised or debited, the books follow. */
export async function postPurchaseInvoice(
  id: string,
  creditDays?: number | null,
): Promise<PostPurchaseInvoiceResponse> {
  return request<PostPurchaseInvoiceResponse>(`/purchase/invoices/${id}/post`, {
    method: 'POST',
    body: { creditDays: creditDays ?? null },
  });
}
