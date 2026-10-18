import { request } from '@/lib/api';

/** A unit of measurement, as the master list shows it. */
export interface UnitSummary {
  readonly id: string;
  readonly code: string;
  readonly name: string;
  readonly symbol: string | null;
  /** Null when this unit is the base of its own measurement group. */
  readonly baseUnitId: string | null;
  readonly baseUnitCode: string | null;
  readonly conversionFactor: number;
  readonly decimalPlaces: number;
  readonly isActive: boolean;
}

/** A product category or sub-class. */
export interface CategorySummary {
  readonly id: string;
  readonly parentId: string | null;
  readonly parentName: string | null;
  readonly code: string;
  readonly name: string;
  readonly nameArabic: string | null;
  readonly isActive: boolean;
}

/** A brand. */
export interface BrandSummary {
  readonly id: string;
  readonly code: string;
  readonly name: string;
  readonly nameArabic: string | null;
  readonly isActive: boolean;
}

/** A place stock is held. */
export interface WarehouseSummary {
  readonly id: string;
  readonly code: string;
  readonly name: string;
  readonly nameArabic: string | null;
  readonly branchId: string | null;
  readonly branchName: string | null;
  readonly address: string | null;
  readonly isDefault: boolean;
  readonly isActive: boolean;
}

/**
 * Which master a withdrawal applies to.
 *
 * The names match the server's enum, which is what the route segment binds against.
 */
export type MasterKind = 'UnitOfMeasure' | 'Category' | 'Brand' | 'Warehouse';

const INVENTORY = '/inventory';

/** Lists a master, optionally including records withdrawn from use. */
export function listMaster<TRow>(
  path: string,
  includeInactive: boolean,
): Promise<readonly TRow[]> {
  return request<readonly TRow[]>(
    `${INVENTORY}/${path}?includeInactive=${String(includeInactive)}`,
  );
}

/** Adds a record to a master. */
export function createMaster(path: string, body: object): Promise<string> {
  return request<string>(`${INVENTORY}/${path}`, { method: 'POST', body });
}

/**
 * Withdraws a record from use, or returns it.
 *
 * Never a delete. A record already named on a document has to go on meaning what it
 * meant, so withdrawal is a flag and the row stays.
 */
export function setMasterActive(
  kind: MasterKind,
  id: string,
  isActive: boolean,
): Promise<void> {
  return request<void>(`${INVENTORY}/${kind}/${id}/active`, {
    method: 'POST',
    body: { isActive },
  });
}

/**
 * Changes a unit of measurement: its name, symbol, decimals and conversion factor.
 *
 * The code never moves, and the server refuses a new factor once a product is measured
 * in the unit or a document was entered in it - changing it then would silently restate
 * quantities recorded months ago.
 */
export function reviseUnit(
  id: string,
  unit: {
    readonly name: string;
    readonly symbol: string | null;
    readonly decimalPlaces: number;
    readonly conversionFactor: number;
  },
): Promise<void> {
  return request<void>(`${INVENTORY}/units/${id}`, {
    method: 'PUT',
    body: unit,
  });
}

/**
 * Corrects a category, brand or warehouse: its names, and a warehouse's address.
 *
 * The code stays as it is - imports and a firm's own lists refer to the record by it.
 */
export function updateMaster(
  kind: Exclude<MasterKind, 'UnitOfMeasure'>,
  id: string,
  body: {
    readonly name: string;
    readonly nameArabic: string | null;
    readonly address?: string | null;
    /** The category a category sits beneath, or null for the top level. */
    readonly parentId?: string | null;
  },
): Promise<void> {
  return request<void>(`${INVENTORY}/${kind}/${id}`, { method: 'PUT', body });
}

/** Makes one warehouse the one new documents default to. */
export function setDefaultWarehouse(id: string): Promise<void> {
  return request<void>(`${INVENTORY}/warehouses/${id}/default`, {
    method: 'POST',
    body: {},
  });
}
