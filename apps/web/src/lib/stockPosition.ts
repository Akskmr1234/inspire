import { request } from '@/lib/api';

/**
 * What is on hand, for the stock register and the stock aging report.
 *
 * The server sends the finest grain - one row per product, godown and batch, and for
 * the aging per receipt date as well - and the screen adds the rows up to whichever
 * columns are showing. Every row is a separate part of the stock, so however they are
 * added up nothing is counted twice and nothing is lost: hide Branch and the branches
 * become one row with their quantities and values added; hide Batch and so do the
 * batches.
 */

/** One part of what is on hand, as the server sends it. */
export interface StockPositionRow {
  readonly productId: string;
  readonly productCode: string;
  readonly productName: string;
  /** Empty for a product not kept in batches. */
  readonly batchNumber: string;
  readonly expiresOn: string | null;
  readonly categoryName: string;
  readonly brandName: string;
  readonly warehouseId: string;
  readonly warehouseName: string;
  readonly branchId: string | null;
  readonly branchName: string;
  readonly stockUnitCode: string;
  readonly quantity: number;
  readonly rate: number;
  readonly value: number;
  /** The aging only: the date this part came in. */
  readonly receivedOn: string | null;
  /** The aging only: how many days it has been held. */
  readonly ageDays: number | null;
}

export interface StockPositionReport {
  readonly asOn: string;
  readonly currency: string;
  readonly rows: readonly StockPositionRow[];
  readonly totalQuantity: number;
  readonly totalValue: number;
}

export interface StockPositionFilters {
  readonly asOn: string;
  readonly branchId: string;
  readonly warehouseId: string;
  readonly categoryId: string;
  readonly brandId: string;
  readonly expiresFrom?: string;
  readonly expiresTo?: string;
}

function read(
  path: 'register' | 'aging',
  filters: StockPositionFilters,
): Promise<StockPositionReport> {
  const query = new URLSearchParams();

  for (const [name, value] of Object.entries(filters)) {
    if (typeof value === 'string' && value !== '') query.set(name, value);
  }

  return request<StockPositionReport>(`/inventory/stock/${path}?${query.toString()}`);
}

/** The stock register: one row per product, godown and batch. */
export const fetchStockRegister = (
  filters: StockPositionFilters,
): Promise<StockPositionReport> => read('register', filters);

/** The stock aging: the register split by the date each part came in. */
export const fetchStockAging = (
  filters: StockPositionFilters,
): Promise<StockPositionReport> => read('aging', filters);

/** A column that tells rows apart; hiding it adds together the rows it told apart. */
export type PositionDimension =
  | 'code'
  | 'name'
  | 'batch'
  | 'expiry'
  | 'category'
  | 'brand'
  | 'godown'
  | 'branch'
  | 'received';

const DIMENSIONS: Readonly<Record<PositionDimension, (row: StockPositionRow) => string>> =
  {
    // Code and name both stand for the product: either showing keeps products apart.
    code: (row) => row.productId,
    name: (row) => row.productId,
    batch: (row) => row.batchNumber,
    expiry: (row) => row.expiresOn ?? '',
    category: (row) => row.categoryName,
    brand: (row) => row.brandName,
    godown: (row) => row.warehouseId,
    branch: (row) => row.branchId ?? '',
    received: (row) => row.receivedOn ?? '',
  };

/** The aging's bands, by the most days stock in each has been held. */
export const AGE_BANDS = [
  { key: 'age0', label: '0–30', upTo: 30 },
  { key: 'age31', label: '31–60', upTo: 60 },
  { key: 'age61', label: '61–90', upTo: 90 },
  { key: 'age91', label: '91–180', upTo: 180 },
  { key: 'age181', label: '180+', upTo: Number.POSITIVE_INFINITY },
] as const;

/** Which band an age falls in. */
export function ageBand(days: number): number {
  return AGE_BANDS.findIndex((band) => days <= band.upTo);
}

/** Rows added up to the columns on screen. */
export interface PositionLine {
  readonly key: string;
  /** The first part's details; those of the columns showing are the same for every part. */
  readonly first: StockPositionRow;
  /** The product, where one product makes up the whole line. */
  readonly productId: string | null;
  /** The unit, where every part is counted in the same one. */
  readonly unit: string;
  readonly quantity: number;
  readonly value: number;
  /** Value over quantity: the average the parts were valued at. */
  readonly rate: number;
  /** The aging: days held, averaged over the quantity. */
  readonly ageDays: number | null;
  /** The aging: quantity in each band. */
  readonly bandQuantity: readonly number[];
  /** The aging: value in each band. */
  readonly bandValue: readonly number[];
}

/** Adds rows up to the dimensions showing, keeping the order they first appear in. */
export function consolidate(
  rows: readonly StockPositionRow[],
  showing: readonly PositionDimension[],
): readonly PositionLine[] {
  const lines = new Map<
    string,
    {
      first: StockPositionRow;
      products: Set<string>;
      units: Set<string>;
      quantity: number;
      value: number;
      aged: number;
      agedQuantity: number;
      bandQuantity: number[];
      bandValue: number[];
    }
  >();

  for (const row of rows) {
    const key = showing.map((dimension) => DIMENSIONS[dimension](row)).join('␟');
    let line = lines.get(key);

    if (!line) {
      line = {
        first: row,
        products: new Set(),
        units: new Set(),
        quantity: 0,
        value: 0,
        aged: 0,
        agedQuantity: 0,
        bandQuantity: AGE_BANDS.map(() => 0),
        bandValue: AGE_BANDS.map(() => 0),
      };
      lines.set(key, line);
    }

    line.products.add(row.productId);
    line.units.add(row.stockUnitCode);
    line.quantity += row.quantity;
    line.value += row.value;

    if (row.ageDays !== null) {
      line.aged += row.ageDays * row.quantity;
      line.agedQuantity += row.quantity;
      const band = ageBand(row.ageDays);
      line.bandQuantity[band] = (line.bandQuantity[band] ?? 0) + row.quantity;
      line.bandValue[band] = (line.bandValue[band] ?? 0) + row.value;
    }
  }

  return [...lines.entries()].map(([key, line]) => {
    // Added in floating point; rounded back to the places the figures came in at.
    const quantity = round(line.quantity, 6);
    const value = round(line.value, 2);

    return {
      key,
      first: line.first,
      productId: line.products.size === 1 ? line.first.productId : null,
      unit: line.units.size === 1 ? line.first.stockUnitCode : '',
      quantity,
      value,
      rate: quantity === 0 ? 0 : round(value / quantity, 4),
      ageDays: line.agedQuantity > 0 ? Math.round(line.aged / line.agedQuantity) : null,
      bandQuantity: line.bandQuantity.map((figure) => round(figure, 6)),
      bandValue: line.bandValue.map((figure) => round(figure, 2)),
    };
  });
}

function round(figure: number, places: number): number {
  const scale = 10 ** places;
  return Math.round(figure * scale) / scale;
}
