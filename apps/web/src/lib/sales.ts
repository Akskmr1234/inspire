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

/**
 * How a sale was settled at the counter. Online is a UPI or card transfer - Google Pay
 * and the like - into one of the firm's bank accounts, with its transaction ID kept.
 */
export const SalesPaymentMode = { credit: 1, cash: 2, online: 3 } as const;

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
  /** When it was entered, which gives the time of the sale. */
  readonly createdAtUtc: string;
  readonly paymentMode: number;
  readonly salesmanName: string | null;
  readonly billingManName: string | null;
  /** What the line discounts took off. */
  readonly discount: number;
  /** What the additional ledgers added, net of what they deducted. */
  readonly chargeTotal: number;
  readonly narration: string | null;
  /** What was paid with the sale. */
  readonly amountReceived: number;
  /** An online payment's transaction ID. */
  readonly paymentReference: string | null;
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
  /** The same units by number, which is what a correction sends back. */
  readonly serialNumbers: readonly string[];
}

/** One charge on a document being entered. */
export interface SalesChargeInput {
  readonly ledgerId: string;
  /** Always positive. The firm's charge matrix decides which way it moves the total. */
  readonly amount: number;
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
  readonly kind: number;
  readonly returnsInvoiceId: string | null;
  readonly paymentMode: number;
  readonly salesmanLedgerId: string | null;
  readonly billingManLedgerId: string | null;
  readonly createdAtUtc: string;
  /** What the customer paid with the sale. */
  readonly amountReceived: number;
  /** The cash or bank account it went into. */
  readonly receivedIntoLedgerId: string | null;
  /** An online payment's transaction ID. */
  readonly paymentReference: string | null;
  /** The receipt posting raised for the payment. */
  readonly receiptVoucherId: string | null;
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

/** What the entry screen sends, for a new document or a correction. */
export interface SalesInvoiceInput {
  readonly date: string;
  readonly customerLedgerId: string;
  readonly warehouseId: string;
  readonly lines: readonly SalesLineInput[];
  readonly kind: number;
  readonly returnsInvoiceId?: string | null;
  readonly referenceNumber?: string | null;
  readonly narration?: string | null;
  /** Freight, delivery, a discount off the whole document. */
  readonly charges?: readonly SalesChargeInput[];
  /** The tax mode. Omitted, the server takes it from the firm's regime. */
  readonly mode?: number | null;
  /** Cash at the counter or on credit. Credit when omitted. */
  readonly paymentMode?: number;
  /** The employee who made the sale. */
  readonly salesmanLedgerId?: string | null;
  /** The employee who billed it. */
  readonly billingManLedgerId?: string | null;
  /**
   * What the customer paid with the sale. Posting raises the receipt for it against the
   * sale's own bill; zero leaves the whole of it on account.
   */
  readonly amountReceived?: number;
  /** The cash or bank account the payment went into. Required when anything was paid. */
  readonly receivedIntoLedgerId?: string | null;
  /** An online payment's transaction ID. */
  readonly paymentReference?: string | null;
  /**
   * The whole bill was paid, whatever it rounds to: the server takes the amount from its
   * own total, so a payment in full is never refused for a fraction.
   */
  readonly paidInFull?: boolean;
}

/** Where a customer stands, for the counter selling to them. */
export interface CustomerAccount {
  readonly customerId: string;
  readonly currency: string;
  /** What they owe across everything posted to them. Negative after an advance. */
  readonly balance: number;
  /** What their unpaid invoices come to. */
  readonly outstanding: number;
  readonly creditLimit: number | null;
  readonly creditDays: number | null;
  /** What is left of the limit; negative once passed, null with no limit. */
  readonly available: number | null;
  readonly isOverLimit: boolean;
}

/** Reads a customer's balance and unpaid invoices. */
export async function getCustomerAccount(customerId: string): Promise<CustomerAccount> {
  return request<CustomerAccount>(`/sales/customers/${customerId}/account`);
}

/**
 * Enters a document. A draft unless `post` is set, in which case it is saved and posted
 * in one step on the server: a sale the books refuse leaves no draft behind.
 */
export async function createSalesInvoice(
  input: SalesInvoiceInput & { readonly post?: boolean },
): Promise<SalesInvoiceHeader> {
  return request<SalesInvoiceHeader>('/sales/invoices', {
    method: 'POST',
    body: input,
  });
}

/**
 * Corrects an invoice, keeping its number.
 *
 * A draft is rewritten (and posted if asked). A posted one has its issue, bill and journal
 * cancelled and is posted again as corrected, in one step on the server, so the books are
 * never left with the sale half-undone. Refused once the customer has paid against it.
 */
export async function updateSalesInvoice(
  id: string,
  input: SalesInvoiceInput & { readonly post?: boolean; readonly reason?: string | null },
): Promise<SalesInvoiceHeader> {
  return request<SalesInvoiceHeader>(`/sales/invoices/${id}`, {
    method: 'PUT',
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
