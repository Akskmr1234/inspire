import { request, type ApiError } from '@/lib/api';
import { listCustomers, createCustomer } from '@/lib/customers';
import { listSuppliers, createSupplier } from '@/lib/suppliers';
import {
  listMaster,
  createMaster,
  setDefaultWarehouse,
  type WarehouseSummary,
  type CategorySummary,
  type UnitSummary,
  type BrandSummary,
} from '@/lib/inventory';
import { listProducts, createProduct, saveProductTab } from '@/lib/products';
import { createSalesInvoice, postSalesInvoice } from '@/lib/sales';
import { createPurchaseInvoice, postPurchaseInvoice } from '@/lib/purchase';
import { createSalesOrder, createPurchaseOrder } from '@/lib/orders';
import { isMoneyAccount, listLedgers } from '@/lib/ledgers';

export interface SampleDataSummary {
  readonly salesInvoicesCreated: number;
  readonly purchaseInvoicesCreated: number;
  readonly ordersCreated: number;
  readonly vouchersCreated: number;
  /**
   * What the server refused, step by step.
   *
   * The generator used to swallow every failure and report "0 Sales Invoices" with
   * nothing to say why — it had been written against request shapes the API does not
   * accept, so every call failed and the screen called that success. A demo that
   * cannot be built should say which step stopped it.
   */
  readonly problems: readonly string[];
}

/** Debit and credit, as the voucher API numbers them. */
const DEBIT = 1;
const CREDIT = 2;

const CATALOGUE = [
  {
    code: 'DEMO-LAPTOP',
    description: 'Business laptop 15.6"',
    cost: 2800,
    retail: 3450,
    mrp: 3800,
  },
  { code: 'DEMO-MOUSE', description: 'Wireless mouse', cost: 42, retail: 65, mrp: 75 },
  {
    code: 'DEMO-MONITOR',
    description: '27" 4K monitor',
    cost: 1100,
    retail: 1420,
    mrp: 1550,
  },
  {
    code: 'DEMO-HUB',
    description: 'USB-C multiport hub',
    cost: 120,
    retail: 185,
    mrp: 210,
  },
  {
    code: 'DEMO-PAPER',
    description: 'A4 copier paper (box of 5 reams)',
    cost: 70,
    retail: 95,
    mrp: 110,
  },
] as const;

/**
 * Populates demo masters and transactions, through the same API the screens use.
 *
 * In the order the books need them: stock is bought and posted before anything is
 * sold, so the sales post rather than failing on an empty shelf. Every record is
 * named `DEMO-` or `Demo` so it can be told apart from real data afterwards, and an
 * existing record with the same code is reused rather than duplicated.
 */
export async function populateSampleTransactions(): Promise<SampleDataSummary> {
  const today = new Date().toISOString().slice(0, 10);
  const problems: string[] = [];

  // Runs one step, recording what the server said if it refused. Never throws:
  // one failed step should not stop the ones that do not depend on it.
  const attempt = async <T>(step: string, run: () => Promise<T>): Promise<T | null> => {
    try {
      return await run();
    } catch (failure) {
      const api = failure as Partial<ApiError>;
      problems.push(`${step}: ${api.detail || api.code || String(failure)}`);
      return null;
    }
  };

  // 1. Masters the transactions need.
  const ensure = async <TRow extends { readonly code: string }>(
    kind: string,
    rows: readonly { readonly code: string; readonly name: string }[],
  ): Promise<readonly TRow[]> => {
    const have = await listMaster<TRow>(kind, true);

    for (const row of rows) {
      if (!have.some((existing) => existing.code === row.code)) {
        await attempt(`Create ${kind} ${row.code}`, () => createMaster(kind, row));
      }
    }

    return listMaster<TRow>(kind, false);
  };

  const warehouses = await ensure<WarehouseSummary>('warehouses', [
    { code: 'DEMO-WH', name: 'Demo main warehouse' },
  ]);

  if (!warehouses.some((warehouse) => warehouse.isDefault) && warehouses[0]) {
    const first = warehouses[0];
    await attempt('Set the default warehouse', () => setDefaultWarehouse(first.id));
  }

  const warehouseId =
    (warehouses.find((warehouse) => warehouse.isDefault) ?? warehouses[0])?.id ?? '';

  const categories = await ensure<CategorySummary>('categories', [
    { code: 'DEMO-IT', name: 'Demo IT hardware' },
  ]);
  const units = await ensure<UnitSummary>('units', [{ code: 'PCS', name: 'Pieces' }]);
  const brands = await ensure<BrandSummary>('brands', [
    { code: 'DEMO', name: 'Demo brand' },
  ]);

  const categoryId =
    categories.find((row) => row.code === 'DEMO-IT')?.id ?? categories[0]?.id;
  const unitId = units.find((row) => row.code === 'PCS')?.id ?? units[0]?.id;
  const brandId = brands.find((row) => row.code === 'DEMO')?.id ?? null;

  // 2. Products, with their rates. Not tracked by serial number or batch, so the demo
  // documents need nothing but a product and a quantity.
  let products = await listProducts('', '', true);

  if (categoryId && unitId) {
    for (const item of CATALOGUE) {
      if (products.some((product) => product.code === item.code)) {
        continue;
      }

      const id = await attempt(`Create product ${item.code}`, () =>
        createProduct({
          code: item.code,
          description: item.description,
          categoryId,
          stockUnitId: unitId,
          brandId,
        }),
      );

      if (id) {
        await attempt(`Price product ${item.code}`, () =>
          saveProductTab(id, 'rates', {
            costingMethod: 1,
            cost: item.cost,
            retailRate: item.retail,
            wholesaleRate: item.retail * 0.95,
            maximumRetailPrice: item.mrp,
          }),
        );
      }
    }

    products = await listProducts('', '', false);
  } else {
    problems.push('No category or unit to put the demo products under.');
  }

  const demo = (code: string): string =>
    products.find((product) => product.code === code)?.id ?? '';
  const laptop = demo('DEMO-LAPTOP');
  const mouse = demo('DEMO-MOUSE');
  const monitor = demo('DEMO-MONITOR');

  // 3. Parties.
  const customers = await listCustomers('', true);

  for (const [code, name] of [
    ['DEMO-C1', 'Demo customer: Al-Noor Trading'],
    ['DEMO-C2', 'Demo customer: Gulf Tech Solutions'],
  ] as const) {
    if (!customers.some((customer) => customer.code === code)) {
      await attempt(`Create customer ${code}`, () =>
        createCustomer({
          code,
          name,
          terms: { creditDays: 30, creditLimit: null, isBillWise: true },
        }),
      );
    }
  }

  const suppliers = await listSuppliers('', true);

  for (const [code, name] of [
    ['DEMO-S1', 'Demo supplier: Apex Hardware'],
    ['DEMO-S2', 'Demo supplier: Global IT Distribution'],
  ] as const) {
    if (!suppliers.some((supplier) => supplier.code === code)) {
      await attempt(`Create supplier ${code}`, () =>
        createSupplier({
          code,
          name,
          terms: { creditDays: 30, creditLimit: null, isBillWise: true },
        }),
      );
    }
  }

  const customer = (await listCustomers('', false)).find((row) => row.code === 'DEMO-C1');
  const customer2 = (await listCustomers('', false)).find(
    (row) => row.code === 'DEMO-C2',
  );
  const supplier = (await listSuppliers('', false)).find((row) => row.code === 'DEMO-S1');

  let salesInvoicesCreated = 0;
  let purchaseInvoicesCreated = 0;
  let ordersCreated = 0;
  let vouchersCreated = 0;

  if (!warehouseId || !laptop || !mouse || !monitor) {
    problems.push(
      'The demo warehouse or products are missing, so no documents were made.',
    );
    return {
      salesInvoicesCreated,
      purchaseInvoicesCreated,
      ordersCreated,
      vouchersCreated,
      problems,
    };
  }

  // 4. Buy the stock first, and post it, so there is something to sell.
  if (supplier) {
    const purchase = await attempt('Create a demo purchase', () =>
      createPurchaseInvoice({
        date: today,
        supplierLedgerId: supplier.supplierId,
        warehouseId,
        kind: 1,
        supplierInvoiceNumber: 'DEMO-APEX-908',
        narration: 'Demo: stock replenishment.',
        lines: [
          {
            productId: laptop,
            quantity: 10,
            rate: 2800,
            discount: 200,
            taxPercentage: 5,
          },
          { productId: monitor, quantity: 15, rate: 1100, discount: 0, taxPercentage: 5 },
          { productId: mouse, quantity: 50, rate: 42, discount: 0, taxPercentage: 5 },
        ],
      }),
    );

    if (purchase) {
      purchaseInvoicesCreated++;
      await attempt(`Post ${purchase.number}`, () =>
        postPurchaseInvoice(purchase.purchaseInvoiceId, 45),
      );
    }

    const purchaseReturn = await attempt('Create a demo purchase return', () =>
      createPurchaseInvoice({
        date: today,
        supplierLedgerId: supplier.supplierId,
        warehouseId,
        kind: 2,
        supplierInvoiceNumber: 'DEMO-RMA-042',
        narration: 'Demo: two faulty mice sent back.',
        lines: [
          { productId: mouse, quantity: 2, rate: 42, discount: 0, taxPercentage: 5 },
        ],
      }),
    );

    if (purchaseReturn) {
      purchaseInvoicesCreated++;
    }

    if (
      await attempt('Create a demo purchase order', () =>
        createPurchaseOrder({
          date: today,
          supplierLedgerId: supplier.supplierId,
          warehouseId,
          narration: 'Demo: restock awaiting confirmation.',
          lines: [
            {
              productId: laptop,
              quantity: 20,
              rate: 2800,
              discount: 500,
              taxPercentage: 5,
            },
          ],
        }),
      )
    ) {
      ordersCreated++;
    }
  }

  // 5. Then sell some of it.
  if (customer) {
    const sale = await attempt('Create a demo sales invoice', () =>
      createSalesInvoice({
        date: today,
        customerLedgerId: customer.customerId,
        warehouseId,
        kind: 1,
        referenceNumber: 'DEMO-PO-991',
        narration: 'Demo: laptops and accessories.',
        lines: [
          { productId: laptop, quantity: 2, rate: 3450, discount: 100, taxPercentage: 5 },
          { productId: mouse, quantity: 5, rate: 65, discount: 0, taxPercentage: 5 },
        ],
      }),
    );

    if (sale) {
      salesInvoicesCreated++;
      await attempt(`Post ${sale.number}`, () =>
        postSalesInvoice(sale.salesInvoiceId, 30),
      );
    }

    const salesReturn = await attempt('Create a demo sales return', () =>
      createSalesInvoice({
        date: today,
        customerLedgerId: customer.customerId,
        warehouseId,
        kind: 2,
        narration: 'Demo: one mouse returned unopened.',
        lines: [
          { productId: mouse, quantity: 1, rate: 65, discount: 0, taxPercentage: 5 },
        ],
      }),
    );

    if (salesReturn) {
      salesInvoicesCreated++;
    }

    if (
      await attempt('Create a demo sales order', () =>
        createSalesOrder({
          date: today,
          customerLedgerId: (customer2 ?? customer).customerId,
          warehouseId,
          narration: 'Demo: monitors for Q4 delivery.',
          lines: [
            {
              productId: monitor,
              quantity: 5,
              rate: 1420,
              discount: 250,
              taxPercentage: 5,
            },
          ],
        }),
      )
    ) {
      ordersCreated++;
    }
  }

  // 6. Money in and out through a bank account.
  const ledgers = await listLedgers(true);
  const bank = ledgers.find((ledger) => isMoneyAccount(ledger) && ledger.kind === 3);
  const voucherType = (receipt: boolean): number =>
    // Bank receipt and bank payment, as the API numbers voucher types.
    receipt ? 2 : 4;

  if (bank && customer) {
    if (
      await attempt('Post a demo receipt', () =>
        request('/accounting/vouchers', {
          method: 'POST',
          body: {
            type: voucherType(true),
            date: today,
            referenceNumber: 'DEMO-TT-8821',
            narration: 'Demo: bank transfer from Al-Noor Trading.',
            paymentMode: 'Bank transfer',
            postImmediately: true,
            lines: [
              { ledgerId: bank.ledgerId, side: DEBIT, amount: 5000 },
              { ledgerId: customer.customerId, side: CREDIT, amount: 5000 },
            ],
          },
        }),
      )
    ) {
      vouchersCreated++;
    }
  }

  if (bank && supplier) {
    if (
      await attempt('Post a demo payment', () =>
        request('/accounting/vouchers', {
          method: 'POST',
          body: {
            type: voucherType(false),
            date: today,
            referenceNumber: 'DEMO-CHQ-1009',
            narration: 'Demo: part payment to Apex Hardware.',
            paymentMode: 'Cheque',
            postImmediately: true,
            lines: [
              { ledgerId: supplier.supplierId, side: DEBIT, amount: 8500 },
              { ledgerId: bank.ledgerId, side: CREDIT, amount: 8500 },
            ],
          },
        }),
      )
    ) {
      vouchersCreated++;
    }
  } else if (!bank) {
    problems.push(
      'No bank ledger in the chart of accounts, so no demo receipt or payment.',
    );
  }

  return {
    salesInvoicesCreated,
    purchaseInvoicesCreated,
    ordersCreated,
    vouchersCreated,
    problems,
  };
}
