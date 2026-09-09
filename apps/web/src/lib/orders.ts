import { request } from '@/lib/api';
import type { PagedResult } from '@/lib/sales';

/**
 * Purchase orders and sales orders.
 *
 * Both in one module because they are one document pointed two ways — what the firm
 * asked a supplier for, and what a customer asked the firm for — with the same
 * lifecycle, the same conversion, and the same shape on the wire. The API has had
 * both since the orders module was written; the seeded menu has linked to both since
 * then too. What was missing was the screens: `/purchase/orders` and `/sales/orders`
 * were menu entries pointing at routes the router did not have, so both fell through
 * the catch-all and landed on the trial balance.
 */

/** Where an order stands, matching the API's enums. Both use the same numbers. */
export const OrderStatus = {
  draft: 1,
  confirmed: 2,
  completed: 3,
  cancelled: 4,
} as const;

/** One product on an order being entered. */
export interface OrderLineInput {
  readonly productId: string;
  readonly quantity: number;
  readonly rate: number;
  readonly taxPercentage: number;
  readonly unitId?: string | null;
  readonly discount?: number;
}

/** One charge carried beside the goods. */
export interface OrderChargeInput {
  readonly ledgerId: string;
  readonly amount: number;
}

/** An order's header figures. */
export interface OrderHeader {
  readonly number: string;
  readonly status: number;
  readonly taxable: number;
  readonly tax: number;
  readonly chargeTotal: number;
  readonly roundingDifference: number;
  readonly total: number;
}

/** A purchase order's header, which names its own identifier. */
export interface PurchaseOrderHeader extends OrderHeader {
  readonly purchaseOrderId: string;
}

/** A sales order's header. */
export interface SalesOrderHeader extends OrderHeader {
  readonly salesOrderId: string;
}

/** A purchase order as the list shows it. */
export interface PurchaseOrderSummary {
  readonly purchaseOrderId: string;
  readonly number: string;
  readonly date: string;
  readonly expectedOn: string | null;
  readonly supplierLedgerId: string;
  readonly supplierCode: string;
  readonly supplierName: string;
  readonly status: number;
  readonly currency: string;
  readonly referenceNumber: string | null;
  readonly lineCount: number;
  readonly outstandingLines: number;
  readonly taxable: number;
  readonly tax: number;
  readonly total: number;
}

/** A sales order as the list shows it. */
export interface SalesOrderSummary {
  readonly salesOrderId: string;
  readonly number: string;
  readonly date: string;
  readonly expectedOn: string | null;
  readonly customerLedgerId: string;
  readonly customerCode: string;
  readonly customerName: string;
  readonly status: number;
  readonly currency: string;
  readonly referenceNumber: string | null;
  readonly lineCount: number;
  readonly outstandingLines: number;
  readonly taxable: number;
  readonly tax: number;
  readonly total: number;
}

/** One line of an order, and how much of it is still owed. */
export interface OrderLineDetail {
  readonly lineNumber: number;
  readonly productId: string;
  readonly unitId: string;
  readonly quantity: number;
  readonly invoicedQuantity: number;
  readonly outstandingQuantity: number;
  readonly rate: number;
  readonly discount: number;
  readonly taxable: number;
  readonly tax: number;
}

/** A purchase order line, which a conversion names by its own identifier. */
export interface PurchaseOrderLineDetail extends OrderLineDetail {
  readonly purchaseOrderLineId: string;
}

/** A sales order line. */
export interface SalesOrderLineDetail extends OrderLineDetail {
  readonly salesOrderLineId: string;
}

/** A charge as it was agreed on an order. */
export interface OrderChargeDetail {
  readonly ledgerId: string;
  readonly amount: number;
  readonly isAddition: boolean;
}

/** A purchase order in full. */
export interface PurchaseOrderDetail {
  readonly header: PurchaseOrderHeader;
  readonly date: string;
  readonly expectedOn: string | null;
  readonly supplierLedgerId: string;
  readonly warehouseId: string;
  readonly mode: number;
  readonly currency: string;
  readonly referenceNumber: string | null;
  readonly narration: string | null;
  readonly closureReason: string | null;
  readonly lines: readonly PurchaseOrderLineDetail[];
  readonly charges: readonly OrderChargeDetail[];
}

/** A sales order in full. */
export interface SalesOrderDetail {
  readonly header: SalesOrderHeader;
  readonly date: string;
  readonly expectedOn: string | null;
  readonly customerLedgerId: string;
  readonly warehouseId: string;
  readonly mode: number;
  readonly currency: string;
  readonly referenceNumber: string | null;
  readonly narration: string | null;
  readonly closureReason: string | null;
  readonly lines: readonly SalesOrderLineDetail[];
  readonly charges: readonly OrderChargeDetail[];
}

/** What an order list is narrowed by. */
export interface OrderFilter {
  readonly from?: string;
  readonly to?: string;
  readonly status?: number | '';
  readonly partyLedgerId?: string;
  readonly search?: string;
  /** Only orders with goods still owed. What a buyer asks for every morning. */
  readonly outstandingOnly?: boolean;
}

function orderQuery(filter: OrderFilter, page: number, pageSize: number): string {
  const query = new URLSearchParams({
    page: String(page),
    pageSize: String(pageSize),
  });

  if (filter.from) query.set('from', filter.from);
  if (filter.to) query.set('to', filter.to);
  if (filter.status !== '' && filter.status !== undefined) {
    query.set('status', String(filter.status));
  }
  if (filter.search?.trim()) query.set('search', filter.search.trim());
  if (filter.outstandingOnly) query.set('outstandingOnly', 'true');

  return query.toString();
}

// ------------------------------------------------------------------ purchase

/** Lists purchase orders, newest first. */
export function listPurchaseOrders(
  filter: OrderFilter,
  page: number,
  pageSize = 25,
): Promise<PagedResult<PurchaseOrderSummary>> {
  const query = orderQuery(filter, page, pageSize);
  const scoped = filter.partyLedgerId
    ? `${query}&supplierLedgerId=${filter.partyLedgerId}`
    : query;

  return request<PagedResult<PurchaseOrderSummary>>(`/purchase/orders?${scoped}`);
}

/** Reads one purchase order, with what is still owed on each line. */
export function getPurchaseOrder(id: string): Promise<PurchaseOrderDetail> {
  return request<PurchaseOrderDetail>(`/purchase/orders/${id}`);
}

/** Enters a purchase order as a draft. */
export function createPurchaseOrder(input: {
  readonly date: string;
  readonly supplierLedgerId: string;
  readonly warehouseId: string;
  readonly lines: readonly OrderLineInput[];
  readonly charges?: readonly OrderChargeInput[];
  readonly mode?: number | null;
  readonly expectedOn?: string | null;
  readonly referenceNumber?: string | null;
  readonly narration?: string | null;
}): Promise<PurchaseOrderHeader> {
  return request<PurchaseOrderHeader>('/purchase/orders', {
    method: 'POST',
    body: input,
  });
}

/** Confirms a draft, so purchases may be raised from it. */
export function confirmPurchaseOrder(id: string): Promise<PurchaseOrderHeader> {
  return request<PurchaseOrderHeader>(`/purchase/orders/${id}/confirm`, {
    method: 'POST',
    body: {},
  });
}

/**
 * Raises a draft purchase from a confirmed order.
 *
 * Naming no lines converts everything still outstanding, which is the ordinary case:
 * the goods arrived, the order is filled. What comes back is a draft purchase —
 * posting it is the ordinary purchase posting, where the goods and the money move.
 */
export function convertPurchaseOrder(
  id: string,
  input: {
    readonly date?: string | null;
    readonly warehouseId?: string | null;
    readonly supplierInvoiceNumber?: string | null;
    readonly supplierInvoiceDate?: string | null;
  } = {},
): Promise<{ readonly purchaseInvoiceId: string; readonly number: string }> {
  return request<{ readonly purchaseInvoiceId: string; readonly number: string }>(
    `/purchase/orders/${id}/convert`,
    { method: 'POST', body: input },
  );
}

/** Closes an order short, or cancels one nothing has arrived against. */
export function closePurchaseOrder(id: string, reason: string): Promise<void> {
  return request<void>(`/purchase/orders/${id}/close`, {
    method: 'POST',
    body: { reason },
  });
}

// --------------------------------------------------------------------- sales

/** Lists sales orders, newest first. */
export function listSalesOrders(
  filter: OrderFilter,
  page: number,
  pageSize = 25,
): Promise<PagedResult<SalesOrderSummary>> {
  const query = orderQuery(filter, page, pageSize);
  const scoped = filter.partyLedgerId
    ? `${query}&customerLedgerId=${filter.partyLedgerId}`
    : query;

  return request<PagedResult<SalesOrderSummary>>(`/sales/orders?${scoped}`);
}

/** Reads one sales order. */
export function getSalesOrder(id: string): Promise<SalesOrderDetail> {
  return request<SalesOrderDetail>(`/sales/orders/${id}`);
}

/** Enters a sales order as a draft. */
export function createSalesOrder(input: {
  readonly date: string;
  readonly customerLedgerId: string;
  readonly warehouseId: string;
  readonly lines: readonly OrderLineInput[];
  readonly charges?: readonly OrderChargeInput[];
  readonly mode?: number | null;
  readonly expectedOn?: string | null;
  readonly referenceNumber?: string | null;
  readonly narration?: string | null;
}): Promise<SalesOrderHeader> {
  return request<SalesOrderHeader>('/sales/orders', { method: 'POST', body: input });
}

/** Confirms a draft, so invoices may be raised from it. */
export function confirmSalesOrder(id: string): Promise<SalesOrderHeader> {
  return request<SalesOrderHeader>(`/sales/orders/${id}/confirm`, {
    method: 'POST',
    body: {},
  });
}

/** Raises a draft invoice from a confirmed order. */
export function convertSalesOrder(
  id: string,
  input: { readonly date?: string | null; readonly warehouseId?: string | null } = {},
): Promise<{ readonly salesInvoiceId: string; readonly number: string }> {
  return request<{ readonly salesInvoiceId: string; readonly number: string }>(
    `/sales/orders/${id}/convert`,
    { method: 'POST', body: input },
  );
}

/** Closes an order short, or cancels one nothing has gone out against. */
export function closeSalesOrder(id: string, reason: string): Promise<void> {
  return request<void>(`/sales/orders/${id}/close`, {
    method: 'POST',
    body: { reason },
  });
}
