import { request } from '@/lib/api';
import type { StockLineInput } from '@/lib/stock';

/** Where a transfer request stands. The server's enum, carried as integers. */
export const StockTransferStatus = {
  requested: 1,
  approved: 2,
  dispatched: 3,
  received: 4,
  rejected: 5,
  cancelled: 6,
} as const;

/** The states a list can be filtered to, in the order the work happens. */
export const TRANSFER_STATUSES = [
  StockTransferStatus.requested,
  StockTransferStatus.approved,
  StockTransferStatus.dispatched,
  StockTransferStatus.received,
  StockTransferStatus.rejected,
  StockTransferStatus.cancelled,
] as const;

/** A transfer request as the list shows it. */
export interface StockTransferSummary {
  readonly id: string;
  readonly number: string;
  readonly date: string;
  readonly fromWarehouseId: string;
  readonly fromWarehouseName: string;
  readonly toWarehouseId: string;
  readonly toWarehouseName: string;
  readonly status: number;
  readonly lineCount: number;
  readonly requestedQuantity: number;
  readonly dispatchedQuantity: number;
  readonly narration: string | null;
  readonly dispatchNumber: string | null;
  readonly receiptNumber: string | null;
}

/** One product on a request. Quantities other than `quantity` are in the stock unit. */
export interface StockTransferLine {
  readonly productId: string;
  readonly productCode: string;
  readonly productName: string;
  readonly unitId: string;
  readonly unitCode: string;
  readonly stockUnitCode: string;
  readonly quantity: number;
  readonly stockQuantity: number;
  readonly approvedQuantity: number;
  readonly dispatchedQuantity: number;
  readonly availableAtSource: number;
  readonly tracksBatches: boolean;
  readonly tracksSerialNumbers: boolean;
  readonly remarks: string | null;
}

/** A request with its lines and its history. */
export interface StockTransferDetail {
  readonly summary: StockTransferSummary;
  readonly reason: string | null;
  readonly createdAtUtc: string;
  readonly decidedAtUtc: string | null;
  readonly dispatchDocumentId: string | null;
  readonly dispatchedAtUtc: string | null;
  readonly receiptDocumentId: string | null;
  readonly receivedAtUtc: string | null;
  readonly lines: readonly StockTransferLine[];
}

/** What an action left the request as. */
export interface StockTransferResponse {
  readonly stockTransferRequestId: string;
  readonly number: string;
  readonly status: number;
  readonly documentNumber: string | null;
}

/** A request as entered. */
export interface StockTransferInput {
  readonly date: string;
  readonly fromWarehouseId: string;
  readonly toWarehouseId: string;
  readonly narration: string | null;
  readonly lines: readonly {
    readonly productId: string;
    readonly quantity: number;
    readonly remarks: string | null;
  }[];
}

const TRANSFERS = '/inventory/stock-transfers';

/** Lists requests over a date range. */
export function listStockTransfers(
  from: string,
  to: string,
  status: number | '',
  warehouseId: string,
): Promise<readonly StockTransferSummary[]> {
  const query = new URLSearchParams({ from, to });

  if (status !== '') {
    query.set('status', String(status));
  }

  if (warehouseId) {
    query.set('warehouseId', warehouseId);
  }

  return request<readonly StockTransferSummary[]>(`${TRANSFERS}?${query.toString()}`);
}

/** Reads one request, with what the sending warehouse holds of each product. */
export function getStockTransfer(id: string): Promise<StockTransferDetail> {
  return request<StockTransferDetail>(`${TRANSFERS}/${id}`);
}

/** Asks another warehouse for goods. */
export function createStockTransfer(
  body: StockTransferInput,
): Promise<StockTransferResponse> {
  return request<StockTransferResponse>(TRANSFERS, { method: 'POST', body });
}

/** Changes a request nobody has acted on yet. */
export function updateStockTransfer(
  id: string,
  body: StockTransferInput,
): Promise<StockTransferResponse> {
  return request<StockTransferResponse>(`${TRANSFERS}/${id}`, { method: 'PUT', body });
}

/** Agrees to a request, for the quantities given (stock units) or for all of it. */
export function approveStockTransfer(
  id: string,
  lines: readonly { readonly productId: string; readonly quantity: number }[] | null,
): Promise<StockTransferResponse> {
  return request<StockTransferResponse>(`${TRANSFERS}/${id}/approve`, {
    method: 'POST',
    body: lines ? { lines } : {},
  });
}

/** Turns a request down. */
export function rejectStockTransfer(
  id: string,
  reason: string,
): Promise<StockTransferResponse> {
  return request<StockTransferResponse>(`${TRANSFERS}/${id}/reject`, {
    method: 'POST',
    body: { reason },
  });
}

/** Withdraws a request before anything has moved. */
export function cancelStockTransfer(
  id: string,
  reason: string,
): Promise<StockTransferResponse> {
  return request<StockTransferResponse>(`${TRANSFERS}/${id}/cancel`, {
    method: 'POST',
    body: { reason },
  });
}

/** Sends the approved goods into transit. */
export function dispatchStockTransfer(
  id: string,
  body: {
    readonly date: string;
    readonly lines: readonly StockLineInput[];
    readonly narration: string | null;
  },
): Promise<StockTransferResponse> {
  return request<StockTransferResponse>(`${TRANSFERS}/${id}/dispatch`, {
    method: 'POST',
    body,
  });
}

/** Takes the dispatched goods into the destination. */
export function receiveStockTransfer(
  id: string,
  date: string,
): Promise<StockTransferResponse> {
  return request<StockTransferResponse>(`${TRANSFERS}/${id}/receive`, {
    method: 'POST',
    body: { date },
  });
}
