import { describe, expect, it } from 'vitest';
import {
  ageBand,
  consolidate,
  type PositionDimension,
  type StockPositionRow,
} from '@/lib/stockPosition';

/** A soap in two batches across two branches, and a pen with no batches. */
function part(overrides: Partial<StockPositionRow>): StockPositionRow {
  return {
    productId: 'soap',
    productCode: 'S1',
    productName: 'Soap',
    batchNumber: 'B1',
    expiresOn: '2027-01-31',
    categoryName: 'Bath',
    brandName: 'Lux',
    warehouseId: 'doha-main',
    warehouseName: 'Doha main',
    branchId: 'doha',
    branchName: 'Doha',
    stockUnitCode: 'PC',
    quantity: 10,
    rate: 2,
    value: 20,
    receivedOn: null,
    ageDays: null,
    ...overrides,
  };
}

const rows: readonly StockPositionRow[] = [
  part({}),
  part({ batchNumber: 'B2', expiresOn: '2027-06-30', quantity: 5, value: 10 }),
  part({
    warehouseId: 'wakra-shop',
    warehouseName: 'Wakra shop',
    branchId: 'wakra',
    branchName: 'Wakra',
    quantity: 3,
    value: 6,
  }),
  part({
    productId: 'pen',
    productCode: 'P1',
    productName: 'Pen',
    batchNumber: '',
    expiresOn: null,
    categoryName: 'Office',
    quantity: 7,
    rate: 1.5,
    value: 10.5,
  }),
];

const all: readonly PositionDimension[] = [
  'code',
  'name',
  'batch',
  'expiry',
  'category',
  'brand',
  'godown',
  'branch',
];

const without = (...hidden: PositionDimension[]): readonly PositionDimension[] =>
  all.filter((dimension) => !hidden.includes(dimension));

const total = (lines: ReturnType<typeof consolidate>): [number, number] => [
  lines.reduce((sum, line) => sum + line.quantity, 0),
  lines.reduce((sum, line) => sum + line.value, 0),
];

describe('consolidate', () => {
  it('keeps every part apart when every column is showing', () => {
    const lines = consolidate(rows, all);

    expect(lines).toHaveLength(4);
    expect(total(lines)).toEqual([25, 46.5]);
  });

  it('adds the branches together when Branch and Godown are hidden', () => {
    const lines = consolidate(rows, without('branch', 'godown'));

    // Soap B1 in Doha and in Wakra become one line; B2 and the pen stay as they were.
    expect(lines).toHaveLength(3);
    const soapB1 = lines.find(
      (line) => line.first.productId === 'soap' && line.first.batchNumber === 'B1',
    );
    expect(soapB1?.quantity).toBe(13);
    expect(soapB1?.value).toBe(26);
    expect(total(lines)).toEqual([25, 46.5]);
  });

  it('adds the batches together when Batch and Expiry are hidden', () => {
    const lines = consolidate(rows, without('batch', 'expiry'));

    // Soap in Doha (both batches), soap in Wakra, the pen.
    expect(lines).toHaveLength(3);
    expect(lines[0]?.quantity).toBe(15);
    expect(lines[0]?.rate).toBe(2);
    expect(total(lines)).toEqual([25, 46.5]);
  });

  it('comes down to one line per product with branch and batch both hidden', () => {
    const lines = consolidate(rows, without('batch', 'expiry', 'branch', 'godown'));

    expect(lines.map((line) => [line.productId, line.quantity, line.value])).toEqual([
      ['soap', 18, 36],
      ['pen', 7, 10.5],
    ]);
  });

  it('can add up by category alone, without the products', () => {
    const lines = consolidate(rows, ['category']);

    expect(lines.map((line) => [line.first.categoryName, line.quantity])).toEqual([
      ['Bath', 18],
      ['Office', 7],
    ]);
  });

  it('claims no single product for a line that is several', () => {
    // Soap and pen are both Lux.
    const [line] = consolidate(rows, ['brand']);

    expect(line?.quantity).toBe(25);
    expect(line?.productId).toBeNull();
  });

  it('averages the rate over the quantity, not over the rows', () => {
    const lines = consolidate(
      [
        part({ quantity: 1, value: 10 }),
        part({ warehouseId: 'store', quantity: 9, value: 9 }),
      ],
      ['code'],
    );

    expect(lines[0]?.rate).toBe(1.9);
  });

  it('spreads aged stock across its bands and averages its age by quantity', () => {
    const aged = [
      part({ receivedOn: '2026-10-01', ageDays: 10, quantity: 4, value: 8 }),
      part({ receivedOn: '2026-06-01', ageDays: 150, quantity: 6, value: 12 }),
    ];

    const [line] = consolidate(aged, ['code']);

    expect(line?.bandQuantity).toEqual([4, 0, 0, 6, 0]);
    expect(line?.bandValue).toEqual([8, 0, 0, 12, 0]);
    expect(line?.ageDays).toBe(94);
  });
});

describe('ageBand', () => {
  it('puts each age in the band it falls in', () => {
    expect([0, 30, 31, 60, 61, 90, 91, 180, 181, 999].map(ageBand)).toEqual([
      0, 0, 1, 1, 2, 2, 3, 3, 4, 4,
    ]);
  });
});
