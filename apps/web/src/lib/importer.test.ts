import { describe, expect, it } from 'vitest';
import { batches, parseExport, summarise, type EasyRetailExport } from '@/lib/importer';

const empty: EasyRetailExport = {
  format: 'easyretail',
  version: 1,
  database: 'EasyRetail2025',
  asAt: '2026-10-17',
  exportedAt: '2026-10-17T09:30:00',
  company: { name: 'RAJWA', city: 'Doha', bookBegin: '2024-01-01' },
  groups: [],
  ledgers: [],
  customers: [],
  suppliers: [],
  employees: [],
  categories: [],
  subClasses: [],
  brands: [],
  units: [],
  warehouses: [],
  products: [],
  barcodes: [],
  stock: [],
};

describe('parseExport', () => {
  it('reads a file the script wrote, byte order mark and all', () => {
    const parsed = parseExport(`﻿${JSON.stringify(empty)}`);
    expect(parsed.database).toBe('EasyRetail2025');
  });

  it('refuses a JSON file that is not an export', () => {
    expect(() => parseExport(JSON.stringify({ format: 'tally', ledgers: [] }))).toThrow(
      'not-an-export',
    );
  });

  it('lets a broken file fail as a syntax error', () => {
    expect(() => parseExport('{ "format": ')).toThrow(SyntaxError);
  });
});

describe('summarise', () => {
  it('totals balances by side and leaves out stock at zero or below', () => {
    const summary = summarise({
      ...empty,
      ledgers: [
        { id: 1, name: 'Cash', groupId: 7, opening: 0, balance: 1000.1, postings: 3 },
        { id: 3, name: 'Sales', groupId: 14, opening: 0, balance: -975, postings: 2 },
        { id: 4, name: 'Bank', groupId: 19, opening: 0, balance: -0.1, postings: 1 },
      ],
      stock: [
        { productId: 1, warehouseId: 1, batch: '', quantity: 3, cost: 2.5 },
        { productId: 2, warehouseId: 1, batch: '', quantity: 0, cost: 9 },
        { productId: 3, warehouseId: 1, batch: '', quantity: -4, cost: 9 },
      ],
    });

    expect(summary.debit).toBe(1000.1);
    expect(summary.credit).toBe(975.1);
    expect(summary.stockLines).toBe(1);
    expect(summary.stockValue).toBe(7.5);
    expect(summary.negativeStock).toBe(2);
    expect(summary.counts).toContainEqual(['ledgers', 3]);
  });
});

describe('batches', () => {
  it('splits a list into runs of the given size, the last one short', () => {
    expect(batches([1, 2, 3, 4, 5], 2)).toEqual([[1, 2], [3, 4], [5]]);
    expect(batches([], 500)).toEqual([]);
  });
});
