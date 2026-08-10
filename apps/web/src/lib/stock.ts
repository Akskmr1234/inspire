import { request } from '@/lib/api';

/**
 * The stock operations of section 8.3.
 *
 * The numbers are the server's enum, which the wire carries as integers.
 */
export const StockDocumentType = {
  openingStock: 1,
  materialReceipt: 2,
  materialIssue: 3,
  stockTransfer: 4,
  stockAdjustment: 5,
  damagedStock: 6,
  physicalVerification: 7,
} as const;

/** Where a stock document stands. */
export const StockDocumentStatus = { draft: 1, posted: 2, cancelled: 3 } as const;

/** The kinds a screen offers, in the order it offers them. */
export const STOCK_TYPES = [
  StockDocumentType.materialReceipt,
  StockDocumentType.materialIssue,
  StockDocumentType.stockTransfer,
  StockDocumentType.stockAdjustment,
  StockDocumentType.damagedStock,
  StockDocumentType.physicalVerification,
  StockDocumentType.openingStock,
] as const;

/**
 * Whether a kind of document carries the cost of the goods.
 *
 * Only where goods arrive from outside the firm's existing stock. Everything else is
 * valued at what the position it leaves already says the goods cost, which is what
 * average costing means — and the server refuses a rate on those, so offering the
 * field would be offering something that cannot be saved.
 */
export function carriesRate(type: number): boolean {
  return (
    type === StockDocumentType.openingStock ||
    type === StockDocumentType.materialReceipt ||
    type === StockDocumentType.stockAdjustment
  );
}

/** Whether a kind of document moves goods between two warehouses. */
export function isTransfer(type: number): boolean {
  return type === StockDocumentType.stockTransfer;
}

/** Whether a line quantity may point downwards. */
export function allowsNegative(type: number): boolean {
  return type === StockDocumentType.stockAdjustment;
}

/** Whether the line quantity is a count rather than a movement. */
export function isCount(type: number): boolean {
  return type === StockDocumentType.physicalVerification;
}

/** A stock document as the list shows it. */
export interface StockDocumentSummary {
  readonly id: string;
  readonly number: string;
  readonly type: number;
  readonly date: string;
  readonly warehouseName: string;
  readonly destinationWarehouseName: string | null;
  readonly referenceNumber: string | null;
  readonly narration: string | null;
  readonly status: number;
  readonly lineCount: number;
  readonly totalQuantity: number;
  readonly totalValue: number;
}

/** One line of a stock document. */
export interface StockDocumentLineView {
  readonly id: string;
  readonly lineNumber: number;
  readonly productId: string;
  readonly productCode: string;
  readonly productDescription: string;
  readonly unitId: string;
  readonly unitCode: string;
  readonly quantity: number;
  readonly stockQuantity: number;
  readonly stockUnitCode: string;
  readonly rate: number;
  readonly remarks: string | null;
  readonly batchId: string | null;
  readonly batchNumber: string | null;
  readonly expiresOn: string | null;
}

/** One movement a document produced. */
export interface StockMovementView {
  readonly productCode: string;
  readonly warehouseName: string;
  readonly quantity: number;
  readonly unitCost: number;
  readonly value: number;
  readonly balanceQuantity: number;
  readonly balanceAverageCost: number;
  readonly batchNumber: string | null;
}

/** A stock document in full. */
export interface StockDocumentDetail {
  readonly id: string;
  readonly number: string;
  readonly type: number;
  readonly date: string;
  readonly warehouseId: string;
  readonly warehouseName: string;
  readonly destinationWarehouseId: string | null;
  readonly destinationWarehouseName: string | null;
  readonly referenceNumber: string | null;
  readonly narration: string | null;
  readonly status: number;
  readonly currency: string;
  readonly cancellationReason: string | null;
  readonly lines: readonly StockDocumentLineView[];
  readonly movements: readonly StockMovementView[];
}

/** What a posting did. */
export interface CreateStockDocumentResponse {
  readonly stockDocumentId: string;
  readonly number: string;
  readonly status: number;
  readonly movements: number;
  readonly totalValue: number;
}

/** One product's position in one warehouse. */
export interface StockValuationRow {
  readonly productId: string;
  readonly productCode: string;
  readonly productDescription: string;
  readonly categoryName: string;
  readonly warehouseId: string;
  readonly warehouseName: string;
  readonly stockUnitCode: string;
  readonly quantity: number;
  readonly averageCost: number;
  readonly value: number;
  readonly reorderLevel: number;
  readonly isBelowReorderLevel: boolean;
}

/** The stock valuation. */
export interface StockValuationReport {
  readonly currency: string;
  readonly rows: readonly StockValuationRow[];
  readonly totalValue: number;
}

/** One movement, as the stock ledger shows it. */
export interface StockLedgerRow {
  readonly date: string;
  readonly documentId: string;
  readonly documentType: number;
  readonly documentNumber: string;
  readonly warehouseName: string;
  readonly quantityIn: number;
  readonly quantityOut: number;
  readonly unitCost: number;
  readonly value: number;
  readonly balanceQuantity: number;
  readonly balanceAverageCost: number;
  readonly narration: string | null;
}

/** The stock ledger of one product. */
export interface StockLedgerReport {
  readonly productCode: string;
  readonly productDescription: string;
  readonly stockUnitCode: string;
  readonly currency: string;
  readonly openingQuantity: number;
  readonly rows: readonly StockLedgerRow[];
  readonly closingQuantity: number;
  readonly totalIn: number;
  readonly totalOut: number;
}

/** One product's movement over a period. */
export interface ItemMovementRow {
  readonly productId: string;
  readonly productCode: string;
  readonly productDescription: string;
  readonly categoryName: string;
  readonly stockUnitCode: string;
  readonly quantityIn: number;
  readonly quantityOut: number;
  readonly valueIn: number;
  readonly valueOut: number;
  readonly movements: number;
  readonly lastMovedOn: string | null;
}

/** One line of a document being entered. */
export interface StockLineInput {
  readonly productId: string;
  readonly quantity: number;
  readonly unitId?: string | null;
  readonly rate?: number;
  readonly remarks?: string | null;
  readonly batchId?: string | null;
  readonly batchNumber?: string | null;
  readonly manufacturedOn?: string | null;
  readonly expiresOn?: string | null;
  readonly serialNumbers?: readonly string[] | null;
  readonly warrantyUntil?: string | null;
}

/** Where a serialised unit stands: the five states of section 12.7. */
export const SerialStatus = {
  inStock: 1,
  issued: 2,
  returnedToSupplier: 3,
  returnedFromCustomer: 4,
  recorded: 5,
} as const;

/** One serialised unit, as a screen shows it. */
export interface SerialNumberView {
  readonly serialNumberId: string;
  readonly number: string;
  readonly productId: string;
  readonly productCode: string;
  readonly productDescription: string;
  readonly batchNumber: string | null;
  readonly status: number;
  readonly warehouseId: string | null;
  readonly warehouseName: string | null;
  readonly unitCost: number;
  readonly receivedOn: string | null;
  readonly issuedOn: string | null;
  readonly warrantyUntil: string | null;
  readonly isUnderWarranty: boolean;
}

/** What is held of one batch in one warehouse. */
export interface BatchStockRow {
  readonly batchId: string;
  readonly batchNumber: string;
  readonly productId: string;
  readonly productCode: string;
  readonly productDescription: string;
  readonly stockUnitCode: string;
  readonly warehouseId: string;
  readonly warehouseName: string;
  readonly quantity: number;
  readonly unitCost: number;
  readonly value: number;
  readonly purchaseRate: number;
  readonly manufacturedOn: string | null;
  readonly expiresOn: string | null;
  readonly daysToExpiry: number | null;
}

/** The batch-wise stock. */
export interface BatchStockReport {
  readonly currency: string;
  readonly rows: readonly BatchStockRow[];
  readonly totalValue: number;
}

/**
 * Whether a kind of document may put a batch on the books that was not there before.
 *
 * The documents that can increase stock. On everything else the screen offers a choice
 * of what is in stock rather than a box to type into, because a number the server does
 * not recognise there is a typing mistake rather than a new lot.
 */
export function opensBatches(type: number): boolean {
  return carriesRate(type) || type === StockDocumentType.physicalVerification;
}

const STOCK = '/inventory/stock';

/** Lists stock documents over a date range. */
export function listStockDocuments(
  from: string,
  to: string,
  type: number | '',
  warehouseId: string,
): Promise<readonly StockDocumentSummary[]> {
  const query = new URLSearchParams({ from, to });

  if (type !== '') {
    query.set('type', String(type));
  }

  if (warehouseId) {
    query.set('warehouseId', warehouseId);
  }

  return request<readonly StockDocumentSummary[]>(
    `${STOCK}/documents?${query.toString()}`,
  );
}

/** Reads one document, with what it said and what it did. */
export function getStockDocument(id: string): Promise<StockDocumentDetail> {
  return request<StockDocumentDetail>(`${STOCK}/documents/${id}`);
}

/** Enters a stock document and, by default, posts it. */
export function createStockDocument(body: {
  readonly type: number;
  readonly date: string;
  readonly warehouseId: string;
  readonly lines: readonly StockLineInput[];
  readonly destinationWarehouseId?: string | null;
  readonly referenceNumber?: string | null;
  readonly narration?: string | null;
  readonly postImmediately: boolean;
}): Promise<CreateStockDocumentResponse> {
  return request<CreateStockDocumentResponse>(`${STOCK}/documents`, {
    method: 'POST',
    body,
  });
}

/** Posts a draft, moving the stock it names. */
export function postStockDocument(id: string): Promise<CreateStockDocumentResponse> {
  return request<CreateStockDocumentResponse>(`${STOCK}/documents/${id}/post`, {
    method: 'POST',
    body: {},
  });
}

/**
 * Cancels a posted document, reversing what it moved.
 *
 * Never a delete. The reversal is written into the stock ledger beside the original,
 * because a ledger that can lose a movement is one nobody can reconcile against a
 * physical count.
 */
export function cancelStockDocument(id: string, reason: string): Promise<void> {
  return request<void>(`${STOCK}/documents/${id}/cancel`, {
    method: 'POST',
    body: { reason },
  });
}

/** Reads what is on hand and what it is worth. */
export function fetchStockValuation(
  warehouseId: string,
  categoryId: string,
  includeZero: boolean,
): Promise<StockValuationReport> {
  const query = new URLSearchParams({ includeZero: String(includeZero) });

  if (warehouseId) {
    query.set('warehouseId', warehouseId);
  }

  if (categoryId) {
    query.set('categoryId', categoryId);
  }

  return request<StockValuationReport>(`${STOCK}/valuation?${query.toString()}`);
}

/** Reads one product's movements, with the position each left behind. */
export function fetchStockLedger(
  productId: string,
  from: string,
  to: string,
  warehouseId: string,
): Promise<StockLedgerReport> {
  const query = new URLSearchParams({ productId, from, to });

  if (warehouseId) {
    query.set('warehouseId', warehouseId);
  }

  return request<StockLedgerReport>(`${STOCK}/ledger?${query.toString()}`);
}

/** Reads what moved over a period. */
export function fetchItemMovement(
  from: string,
  to: string,
  warehouseId: string,
  categoryId: string,
): Promise<readonly ItemMovementRow[]> {
  const query = new URLSearchParams({ from, to });

  if (warehouseId) {
    query.set('warehouseId', warehouseId);
  }

  if (categoryId) {
    query.set('categoryId', categoryId);
  }

  return request<readonly ItemMovementRow[]>(`${STOCK}/movement?${query.toString()}`);
}

/**
 * Lists the batches of one product that can be picked from.
 *
 * What section 10 means by selection on sale: each row carries what is available, what
 * the lot was bought at, and when it expires — so the screen can offer a choice, or
 * make it when only one comes back.
 */
export function fetchProductBatches(
  productId: string,
  warehouseId: string,
  includeEmpty = false,
): Promise<readonly BatchStockRow[]> {
  const query = new URLSearchParams({
    productId,
    includeEmpty: String(includeEmpty),
  });

  if (warehouseId) {
    query.set('warehouseId', warehouseId);
  }

  return request<readonly BatchStockRow[]>(`${STOCK}/batches?${query.toString()}`);
}

/** Reads the batch-wise stock. */
export function fetchBatchStock(
  warehouseId: string,
  categoryId: string,
  includeZero: boolean,
): Promise<BatchStockReport> {
  const query = new URLSearchParams({ includeZero: String(includeZero) });

  if (warehouseId) {
    query.set('warehouseId', warehouseId);
  }

  if (categoryId) {
    query.set('categoryId', categoryId);
  }

  return request<BatchStockReport>(`${STOCK}/batch-stock?${query.toString()}`);
}

/**
 * Lists the serialised units of one product.
 *
 * Section 12.7's selection on sale: the units on the shelf, each with what it cost and
 * how long it is covered for. A unit that has gone out is not offered again.
 */
export function fetchProductSerials(
  productId: string,
  warehouseId: string,
  includeGone = false,
): Promise<readonly SerialNumberView[]> {
  const query = new URLSearchParams({
    productId,
    includeGone: String(includeGone),
  });

  if (warehouseId) {
    query.set('warehouseId', warehouseId);
  }

  return request<readonly SerialNumberView[]>(`${STOCK}/serials?${query.toString()}`);
}

/** Finds units by the number on the case, across every product. */
export function findSerial(number: string): Promise<readonly SerialNumberView[]> {
  return request<readonly SerialNumberView[]>(
    `${STOCK}/serials/find?number=${encodeURIComponent(number)}`,
  );
}

/** Reads what has expired, and what is about to. */
export function fetchExpiring(
  asOn: string,
  withinDays: number | '',
  warehouseId: string,
  categoryId: string,
): Promise<readonly BatchStockRow[]> {
  const query = new URLSearchParams({ asOn });

  if (withinDays !== '') {
    query.set('withinDays', String(withinDays));
  }

  if (warehouseId) {
    query.set('warehouseId', warehouseId);
  }

  if (categoryId) {
    query.set('categoryId', categoryId);
  }

  return request<readonly BatchStockRow[]>(`${STOCK}/expiry?${query.toString()}`);
}
