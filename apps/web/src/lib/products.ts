import { request } from '@/lib/api';

/** Whether a product is stocked, a service, or bought and consumed without stock. */
export const ItemType = { stock: 1, service: 2, nonStock: 3 } as const;

/** How a product's cost is arrived at. Two, as the reference application offers. */
export const CostingMethod = { lastPurchaseRate: 1, averageRate: 2 } as const;

/** How quickly a product turns over. */
export const MovementClass = {
  unclassified: 0,
  fast: 1,
  normal: 2,
  slow: 3,
  dead: 4,
} as const;

/** A product as the list shows it. */
export interface ProductSummary {
  readonly id: string;
  readonly code: string;
  readonly description: string;
  readonly descriptionArabic: string | null;
  readonly itemType: number;
  readonly categoryId: string;
  readonly categoryName: string;
  readonly brandName: string | null;
  readonly stockUnitCode: string;
  readonly currency: string;
  readonly cost: number;
  readonly retailRate: number;
  readonly reorderLevel: number;
  readonly tracksBatches: boolean;
  readonly tracksSerialNumbers: boolean;
  readonly isDiscontinued: boolean;
  readonly isActive: boolean;
  readonly barcodeCount: number;
}

/** One barcode of a product. */
export interface ProductBarcodeView {
  readonly id: string;
  readonly barcode: string;
  readonly cost: number;
  readonly retailRate: number;
  readonly wholesaleRate: number;
  readonly maximumRetailPrice: number;
}

/** A product in full, as the editor needs it. */
export interface ProductDetail {
  readonly id: string;
  readonly code: string;
  readonly description: string;
  readonly descriptionArabic: string | null;
  readonly shortDescription: string | null;
  readonly itemName: string | null;
  readonly manufacturer: string | null;
  readonly label: string | null;
  readonly size: string | null;
  readonly origin: string | null;
  readonly itemType: number;
  readonly categoryId: string;
  readonly brandId: string | null;
  readonly stockUnitId: string;
  readonly purchaseUnitId: string;
  readonly salesUnitId: string;
  readonly currency: string;
  readonly costingMethod: number;
  readonly cost: number;
  readonly profitPercentage: number;
  readonly corPercentage: number;
  readonly retailRate: number;
  readonly wholesaleRate: number;
  readonly otherRate: number;
  readonly maximumRetailPrice: number;
  readonly minimumLevel: number;
  readonly reorderLevel: number;
  readonly maximumLevel: number;
  readonly movement: number;
  readonly device: string | null;
  readonly colour: string | null;
  readonly battery: string | null;
  readonly ram: string | null;
  readonly storage: string | null;
  readonly rack: string | null;
  readonly bin: string | null;
  readonly tracksBatches: boolean;
  readonly tracksSerialNumbers: boolean;
  readonly shelfLifeDays: number | null;
  readonly isPacking: boolean;
  readonly isDiscontinued: boolean;
  readonly isActive: boolean;
  readonly barcodes: readonly ProductBarcodeView[];
}

const PRODUCTS = '/inventory/products';

/**
 * Lists products.
 *
 * The search goes to the server rather than to the grid. A product master is tens of
 * thousands of rows, and the grid's own filtering only helps once they have all been
 * sent — which is the part that would be slow.
 */
export function listProducts(
  search: string,
  categoryId: string,
  includeInactive: boolean,
): Promise<readonly ProductSummary[]> {
  const query = new URLSearchParams({ includeInactive: String(includeInactive) });

  if (search.trim()) {
    query.set('search', search.trim());
  }

  if (categoryId) {
    query.set('categoryId', categoryId);
  }

  return request<readonly ProductSummary[]>(`${PRODUCTS}?${query.toString()}`);
}

/** Reads one product in full. */
export function getProduct(id: string): Promise<ProductDetail> {
  return request<ProductDetail>(`${PRODUCTS}/${id}`);
}

/** Adds a product. Leave the code blank to have the next one issued. */
export function createProduct(body: object): Promise<string> {
  return request<string>(PRODUCTS, { method: 'POST', body });
}

/**
 * Saves one tab of a product.
 *
 * A tab at a time rather than the whole record, so repricing does not resend the
 * reorder levels and quietly overwrite whatever a colleague changed a minute ago.
 */
export function saveProductTab(id: string, tab: string, body: object): Promise<void> {
  return request<void>(`${PRODUCTS}/${id}/${tab}`, { method: 'PUT', body });
}

/** Adds a barcode, optionally with rates of its own. */
export function addProductBarcode(id: string, body: object): Promise<string> {
  return request<string>(`${PRODUCTS}/${id}/barcodes`, { method: 'POST', body });
}

/** Removes a barcode from a product. */
export function removeProductBarcode(id: string, barcodeId: string): Promise<void> {
  return request<void>(`${PRODUCTS}/${id}/barcodes/${barcodeId}`, { method: 'DELETE' });
}

/** Withdraws a product from use, or returns it. Never a delete. */
export function setProductFlag(
  id: string,
  flag: 'active' | 'discontinued',
  value: boolean,
): Promise<void> {
  return request<void>(`${PRODUCTS}/${id}/${flag}`, { method: 'POST', body: { value } });
}
