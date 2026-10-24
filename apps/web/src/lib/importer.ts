import { request, requestBlob } from '@/lib/api';

/** A ledger in an EasyRetail export, with its balance as at the export date (debit positive). */
export interface ErLedger {
  readonly id: number;
  readonly name: string;
  readonly groupId: number;
  readonly opening: number;
  readonly balance: number;
  readonly postings: number;
}

export interface ErProduct {
  readonly id: number;
  readonly code: string;
  readonly name: string;
  readonly [field: string]: unknown;
}

export interface ErBarcode {
  readonly productId: number;
  readonly barcode: string;
  readonly [field: string]: unknown;
}

export interface ErStock {
  readonly productId: number;
  readonly warehouseId: number;
  readonly batch: string;
  readonly quantity: number;
  readonly cost: number;
}

/** What the exporter writes, and what reading the database directly returns. */
export interface EasyRetailExport {
  readonly format: string;
  readonly version: number;
  readonly database: string;
  readonly asAt: string;
  readonly exportedAt: string;
  readonly company: {
    readonly name: string;
    readonly city: string;
    readonly bookBegin: string;
  } | null;
  readonly groups: readonly unknown[];
  readonly ledgers: readonly ErLedger[];
  readonly customers: readonly unknown[];
  readonly suppliers: readonly unknown[];
  readonly employees: readonly unknown[];
  readonly categories: readonly unknown[];
  readonly subClasses: readonly unknown[];
  readonly brands: readonly unknown[];
  readonly units: readonly unknown[];
  readonly warehouses: readonly unknown[];
  readonly products: readonly ErProduct[];
  readonly barcodes: readonly ErBarcode[];
  readonly stock: readonly ErStock[];
}

export interface ImportStepResult {
  readonly created: number;
  readonly matched: number;
  /** Brought across by an earlier run and left as it is. */
  readonly alreadyImported: number;
  /** Left out: a placeholder, unused, or not importable. */
  readonly skipped: number;
  readonly warnings: readonly string[];
}

export interface ImportBalancesResult {
  readonly step: ImportStepResult;
  readonly totalDebit: number;
  readonly totalCredit: number;
  readonly profitCarried: number;
  readonly difference: number;
}

export interface EasyRetailConnection {
  readonly server: string;
  readonly database: string;
  readonly userName: string | null;
  readonly password: string | null;
  readonly asAt: string | null;
}

const BASE = '/platform/import/easyretail';

/** How many products, and how many stock lines, one request carries. */
export const PRODUCT_BATCH = 500;
export const STOCK_BATCH = 1000;

/** The Windows program that saves an EasyRetail database to an export file. */
export const EXPORTER_FILE = 'easyretail-export.exe';

export const downloadExporter = (): Promise<Blob> => requestBlob(`${BASE}/exporter`);

export const readDatabase = (
  connection: EasyRetailConnection,
): Promise<EasyRetailExport> =>
  request<EasyRetailExport>(`${BASE}/read`, { method: 'POST', body: connection });

export const importMasters = (data: EasyRetailExport): Promise<ImportStepResult> =>
  request<ImportStepResult>(`${BASE}/masters`, {
    method: 'POST',
    body: {
      groups: data.groups,
      ledgers: data.ledgers,
      customers: data.customers,
      suppliers: data.suppliers,
      employees: data.employees,
      categories: data.categories,
      brands: data.brands,
      units: data.units,
      warehouses: data.warehouses,
    },
  });

export const importProducts = (
  data: EasyRetailExport,
  products: readonly ErProduct[],
): Promise<ImportStepResult> => {
  const ids = new Set(products.map((product) => product.id));
  return request<ImportStepResult>(`${BASE}/products`, {
    method: 'POST',
    body: {
      products,
      barcodes: data.barcodes.filter((barcode) => ids.has(barcode.productId)),
      subClasses: data.subClasses,
    },
  });
};

export const importBalances = (
  data: EasyRetailExport,
  carryProfitToEquity: boolean,
): Promise<ImportBalancesResult> =>
  request<ImportBalancesResult>(`${BASE}/balances`, {
    method: 'POST',
    body: { ledgers: data.ledgers, carryProfitToEquity },
  });

export const importStock = (
  asAt: string,
  rows: readonly ErStock[],
): Promise<ImportStepResult> =>
  request<ImportStepResult>(`${BASE}/stock`, { method: 'POST', body: { asAt, rows } });

/** Reads an export file, refusing anything that is not one. */
export function parseExport(text: string): EasyRetailExport {
  const parsed = JSON.parse(text.replace(/^﻿/, '')) as Partial<EasyRetailExport>;

  if (
    parsed.format !== 'easyretail' ||
    parsed.version !== 1 ||
    !Array.isArray(parsed.ledgers)
  ) {
    throw new Error('not-an-export');
  }

  return parsed as EasyRetailExport;
}

/** The figures a person checks before importing: what the export holds and how it balances. */
export interface ExportSummary {
  readonly counts: ReadonlyArray<readonly [string, number]>;
  readonly stockLines: number;
  readonly stockValue: number;
  readonly negativeStock: number;
  readonly debit: number;
  readonly credit: number;
}

export function summarise(data: EasyRetailExport): ExportSummary {
  let debit = 0;
  let credit = 0;
  for (const ledger of data.ledgers) {
    if (ledger.balance > 0) debit += ledger.balance;
    else credit -= ledger.balance;
  }

  let stockLines = 0;
  let stockValue = 0;
  let negativeStock = 0;
  for (const row of data.stock) {
    if (row.quantity > 0) {
      stockLines += 1;
      stockValue += row.quantity * row.cost;
    } else {
      negativeStock += 1;
    }
  }

  return {
    counts: [
      ['groups', data.groups.length],
      ['ledgers', data.ledgers.length],
      ['customers', data.customers.length],
      ['suppliers', data.suppliers.length],
      ['employees', data.employees.length],
      ['categories', data.categories.length],
      ['subClasses', data.subClasses.length],
      ['brands', data.brands.length],
      ['products', data.products.length],
      ['barcodes', data.barcodes.length],
    ],
    stockLines,
    stockValue: Math.round(stockValue * 100) / 100,
    negativeStock,
    debit: Math.round(debit * 100) / 100,
    credit: Math.round(credit * 100) / 100,
  };
}

/** Splits a list into batches of a given size. */
export function batches<T>(items: readonly T[], size: number): T[][] {
  const out: T[][] = [];
  for (let start = 0; start < items.length; start += size) {
    out.push(items.slice(start, start + size));
  }
  return out;
}
