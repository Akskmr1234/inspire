import { request } from '@/lib/api';
import { listCustomers, createCustomer } from '@/lib/customers';
import { listSuppliers, createSupplier } from '@/lib/suppliers';
import {
  listMaster,
  createMaster,
  type WarehouseSummary,
  type CategorySummary,
  type UnitSummary,
  type BrandSummary,
} from '@/lib/inventory';
import { listProducts, createProduct } from '@/lib/products';
import { createSalesInvoice, postSalesInvoice, type SalesLineInput } from '@/lib/sales';
import { createPurchaseInvoice, postPurchaseInvoice, type PurchaseLineInput } from '@/lib/purchase';
import { createSalesOrder, createPurchaseOrder } from '@/lib/orders';
import { listLedgers } from '@/lib/ledgers';

export interface SampleDataSummary {
  readonly salesInvoicesCreated: number;
  readonly purchaseInvoicesCreated: number;
  readonly ordersCreated: number;
  readonly vouchersCreated: number;
}

/**
 * Populates realistic sample data across all transactions:
 * Sales Invoices, Sales Returns, Purchase Invoices, Purchase Returns,
 * Sales Orders, Purchase Orders, and Payment/Receipt Vouchers.
 */
export async function populateSampleTransactions(): Promise<SampleDataSummary> {
  const today = new Date().toISOString().slice(0, 10);

  // 1. Ensure Warehouses
  let warehouses = await listMaster<WarehouseSummary>('warehouses', false);
  if (warehouses.length === 0) {
    try {
      await createMaster('warehouses', {
        code: 'WH-MAIN',
        name: 'Main Central Warehouse',
        isDefault: true,
      });
      await createMaster('warehouses', {
        code: 'WH-RETAIL',
        name: 'Retail Showroom Store',
        isDefault: false,
      });
      warehouses = await listMaster<WarehouseSummary>('warehouses', false);
    } catch {
      // Ignore if exists
    }
  }
  const defaultWarehouseId = warehouses[0]?.id || '';

  // 2. Ensure Categories
  let categories = await listMaster<CategorySummary>('categories', false);
  if (categories.length === 0) {
    try {
      await createMaster('categories', { code: 'CAT-ELEC', name: 'Electronics & IT Hardware' });
      await createMaster('categories', { code: 'CAT-OFF', name: 'Office Equipment & Supplies' });
      categories = await listMaster<CategorySummary>('categories', false);
    } catch {
      // Ignore
    }
  }
  const defaultCategoryId = categories[0]?.id || '';

  // 3. Ensure Units
  let units = await listMaster<UnitSummary>('units', false);
  if (units.length === 0) {
    try {
      await createMaster('units', { code: 'PCS', name: 'Pieces' });
      await createMaster('units', { code: 'BOX', name: 'Box (Pack)' });
      units = await listMaster<UnitSummary>('units', false);
    } catch {
      // Ignore
    }
  }
  const defaultUnitId = units[0]?.id || '';

  // 4. Ensure Brands
  let brands = await listMaster<BrandSummary>('brands', false);
  if (brands.length === 0) {
    try {
      await createMaster('brands', { code: 'DELL', name: 'Dell Technologies' });
      await createMaster('brands', { code: 'LOGI', name: 'Logitech' });
      await createMaster('brands', { code: 'HP', name: 'HP Enterprise' });
      brands = await listMaster<BrandSummary>('brands', false);
    } catch {
      // Ignore
    }
  }
  const defaultBrandId = brands[0]?.id || null;

  // 5. Ensure Products
  let products = await listProducts('', '', false);
  if (products.length < 3 && defaultCategoryId && defaultUnitId) {
    const demoCatalog = [
      {
        code: 'PROD-LAPTOP-15',
        description: 'Dell Latitude 15.6" Business Laptop',
        categoryId: defaultCategoryId,
        unitId: defaultUnitId,
        brandId: defaultBrandId,
        itemType: 1,
        tracksSerialNumbers: true,
        tracksBatches: false,
        standardRate: 3450.0,
        mrp: 3800.0,
      },
      {
        code: 'PROD-MOUSE-W1',
        description: 'Logitech M185 Wireless Ergonomic Mouse',
        categoryId: defaultCategoryId,
        unitId: defaultUnitId,
        brandId: brands[1]?.id || defaultBrandId,
        itemType: 1,
        tracksSerialNumbers: false,
        tracksBatches: true,
        standardRate: 65.0,
        mrp: 75.0,
      },
      {
        code: 'PROD-MON-27',
        description: 'Dell 27" 4K UHD USB-C Hub Monitor',
        categoryId: defaultCategoryId,
        unitId: defaultUnitId,
        brandId: defaultBrandId,
        itemType: 1,
        tracksSerialNumbers: true,
        tracksBatches: false,
        standardRate: 1420.0,
        mrp: 1550.0,
      },
      {
        code: 'PROD-ADAPT-C',
        description: 'HP 7-in-1 Multiport USB-C Hub Adapter',
        categoryId: defaultCategoryId,
        unitId: defaultUnitId,
        brandId: brands[2]?.id || defaultBrandId,
        itemType: 1,
        tracksSerialNumbers: false,
        tracksBatches: false,
        standardRate: 185.0,
        mrp: 210.0,
      },
      {
        code: 'PROD-PAPER-A4',
        description: 'Premium A4 Multipurpose Copy Paper (Box 5 Reams)',
        categoryId: categories[1]?.id || defaultCategoryId,
        unitId: units[1]?.id || defaultUnitId,
        brandId: null,
        itemType: 1,
        tracksSerialNumbers: false,
        tracksBatches: false,
        standardRate: 95.0,
        mrp: 110.0,
      },
    ];

    for (const p of demoCatalog) {
      try {
        await createProduct(p);
      } catch {
        // Continue if already exists
      }
    }
    products = await listProducts('', '', false);
  }

  // 6. Ensure Customers
  let customers = await listCustomers('', false);
  if (customers.length < 2) {
    const demoCustomers = [
      {
        code: 'CUST-001',
        name: 'Al-Noor Trading LLC',
        contact: {
          phone: '+974 4411 2233',
          email: 'accounts@alnoortrading.qa',
          addressLine1: 'Building 44, Grand Hamad St, Doha',
        },
        terms: { creditDays: 30, creditLimit: 50000, isBillWise: true },
      },
      {
        code: 'CUST-002',
        name: 'Gulf Tech Solutions WLL',
        contact: {
          phone: '+974 4488 9900',
          email: 'finance@gulftech.qa',
          addressLine1: 'West Bay Commercial Tower 12, Doha',
        },
        terms: { creditDays: 45, creditLimit: 75000, isBillWise: true },
      },
      {
        code: 'CUST-003',
        name: 'Doha Horizon Superstores',
        contact: {
          phone: '+974 4455 6677',
          email: 'procurement@dohahorizon.qa',
          addressLine1: 'Salwa Road Industrial Zone, Doha',
        },
        terms: { creditDays: 15, creditLimit: 25000, isBillWise: true },
      },
    ];

    for (const c of demoCustomers) {
      try {
        await createCustomer(c);
      } catch {
        // Continue
      }
    }
    customers = await listCustomers('', false);
  }

  // 7. Ensure Suppliers
  let suppliers = await listSuppliers('', false);
  if (suppliers.length < 2) {
    const demoSuppliers = [
      {
        code: 'SUPP-001',
        name: 'Apex Hardware Supplies Ltd',
        contact: {
          phone: '+974 4422 1100',
          email: 'sales@apexsupplies.qa',
          addressLine1: 'Logistics Park South, Al Wakrah',
        },
        terms: { creditDays: 30, creditLimit: 100000, isBillWise: true },
      },
      {
        code: 'SUPP-002',
        name: 'Global IT Distribution Network',
        contact: {
          phone: '+974 4433 7788',
          email: 'orders@globalitdist.qa',
          addressLine1: 'Free Zone Area, Airport Road',
        },
        terms: { creditDays: 60, creditLimit: 150000, isBillWise: true },
      },
    ];

    for (const s of demoSuppliers) {
      try {
        await createSupplier(s);
      } catch {
        // Continue
      }
    }
    suppliers = await listSuppliers('', false);
  }

  // Pick ledgers for transactions
  const ledgers = await listLedgers(false);
  const customerLedger1 =
    customers[0]?.customerId ||
    ledgers.find((l) => l.name.toLowerCase().includes('customer') || l.groupName.toLowerCase().includes('debtor'))?.ledgerId ||
    '';
  const customerLedger2 =
    customers[1]?.customerId ||
    customerLedger1;
  const supplierLedger1 =
    suppliers[0]?.supplierId ||
    ledgers.find((l) => l.name.toLowerCase().includes('supplier') || l.groupName.toLowerCase().includes('creditor'))?.ledgerId ||
    '';
  const supplierLedger2 =
    suppliers[1]?.supplierId ||
    supplierLedger1;

  const prod1 = products[0]?.id || '';
  const prod2 = products[1]?.id || prod1;
  const prod3 = products[2]?.id || prod1;

  let salesInvoicesCreated = 0;
  let purchaseInvoicesCreated = 0;
  let ordersCreated = 0;
  let vouchersCreated = 0;

  // 8. Create Sample Sales Invoices
  if (customerLedger1 && defaultWarehouseId && prod1) {
    try {
      const lines1: SalesLineInput[] = [
        {
          productId: prod1,
          quantity: 2,
          rate: 3450.0,
          discount: 100.0,
          taxPercentage: 5,
        },
      ];
      if (prod2) {
        lines1.push({
          productId: prod2,
          quantity: 5,
          rate: 65.0,
          discount: 0,
          taxPercentage: 5,
        });
      }

      const inv1 = await createSalesInvoice({
        date: today,
        customerLedgerId: customerLedger1,
        warehouseId: defaultWarehouseId,
        lines: lines1,
        kind: 1, // Invoice
        referenceNumber: 'PO-ALNOOR-991',
        narration: 'Supply of office laptops and accessories as per agreed tender quote.',
      });
      salesInvoicesCreated++;

      // Post the first invoice to make it real
      if (inv1?.salesInvoiceId) {
        try {
          await postSalesInvoice(inv1.salesInvoiceId, 30);
        } catch {
          // If post has strict constraints (e.g. stock reservation), it remains draft
        }
      }
    } catch {
      // Continue
    }

    // Invoice 2
    try {
      const lines2: SalesLineInput[] = [
        {
          productId: prod3 || prod1,
          quantity: 3,
          rate: 1420.0,
          discount: 50.0,
          taxPercentage: 5,
        },
      ];
      await createSalesInvoice({
        date: today,
        customerLedgerId: customerLedger2,
        warehouseId: defaultWarehouseId,
        lines: lines2,
        kind: 1, // Invoice
        referenceNumber: 'GULFTECH-REQ-104',
        narration: 'Workstation 4K monitors deployment for engineering floor.',
      });
      salesInvoicesCreated++;
    } catch {
      // Continue
    }

    // Sales Return (Kind 2)
    try {
      const returnLines: SalesLineInput[] = [
        {
          productId: prod2 || prod1,
          quantity: 1,
          rate: 65.0,
          discount: 0,
          taxPercentage: 5,
        },
      ];
      await createSalesInvoice({
        date: today,
        customerLedgerId: customerLedger1,
        warehouseId: defaultWarehouseId,
        lines: returnLines,
        kind: 2, // Return
        referenceNumber: 'RET-BOX-DAMAGE',
        narration: 'Customer return: 1 unit unopened with damaged outer packaging.',
      });
      salesInvoicesCreated++;
    } catch {
      // Continue
    }
  }

  // 9. Create Sample Purchase Invoices
  if (supplierLedger1 && defaultWarehouseId && prod1) {
    try {
      const pLines1: PurchaseLineInput[] = [
        {
          productId: prod1,
          quantity: 10,
          rate: 2800.0,
          discount: 200.0,
          taxPercentage: 5,
        },
      ];
      if (prod3) {
        pLines1.push({
          productId: prod3,
          quantity: 15,
          rate: 1100.0,
          discount: 0,
          taxPercentage: 5,
        });
      }

      const pInv1 = await createPurchaseInvoice({
        date: today,
        supplierLedgerId: supplierLedger1,
        warehouseId: defaultWarehouseId,
        lines: pLines1,
        kind: 1, // Purchase Invoice
        supplierInvoiceNumber: 'APEX-INV-2026-908',
        narration: 'Stock replenishment order from Apex Hardware Supplies.',
      });
      purchaseInvoicesCreated++;

      if (pInv1?.purchaseInvoiceId) {
        try {
          await postPurchaseInvoice(pInv1.purchaseInvoiceId, 45);
        } catch {
          // Continue
        }
      }
    } catch {
      // Continue
    }

    // Purchase Invoice 2
    try {
      const pLines2: PurchaseLineInput[] = [
        {
          productId: prod2 || prod1,
          quantity: 50,
          rate: 42.0,
          discount: 0,
          taxPercentage: 5,
        },
      ];
      await createPurchaseInvoice({
        date: today,
        supplierLedgerId: supplierLedger2,
        warehouseId: defaultWarehouseId,
        lines: pLines2,
        kind: 1,
        supplierInvoiceNumber: 'GLOBAL-DO-5521',
        narration: 'Accessories batch consignment from Global IT Distribution.',
      });
      purchaseInvoicesCreated++;
    } catch {
      // Continue
    }

    // Purchase Return (Kind 2)
    try {
      const pRetLines: PurchaseLineInput[] = [
        {
          productId: prod2 || prod1,
          quantity: 2,
          rate: 42.0,
          discount: 0,
          taxPercentage: 5,
        },
      ];
      await createPurchaseInvoice({
        date: today,
        supplierLedgerId: supplierLedger2,
        warehouseId: defaultWarehouseId,
        lines: pRetLines,
        kind: 2,
        supplierInvoiceNumber: 'RMA-RETURN-042',
        narration: 'Defective batch items returned to supplier under warranty replacement.',
      });
      purchaseInvoicesCreated++;
    } catch {
      // Continue
    }
  }

  // 10. Create Sample Orders
  if (customerLedger1 && defaultWarehouseId && prod1) {
    try {
      await createSalesOrder({
        date: today,
        customerLedgerId: customerLedger1,
        warehouseId: defaultWarehouseId,
        lines: [
          {
            productId: prod1,
            quantity: 5,
            rate: 3450.0,
            discount: 250.0,
            taxPercentage: 5,
          },
        ],
        narration: 'Annual IT hardware upgrade order for Q4 delivery.',
      });
      ordersCreated++;
    } catch {
      // Continue
    }
  }

  if (supplierLedger1 && defaultWarehouseId && prod1) {
    try {
      await createPurchaseOrder({
        date: today,
        supplierLedgerId: supplierLedger1,
        warehouseId: defaultWarehouseId,
        lines: [
          {
            productId: prod1,
            quantity: 20,
            rate: 2800.0,
            discount: 500.0,
            taxPercentage: 5,
          },
        ],
        narration: 'Restock Purchase Order awaiting dispatch confirmation.',
      });
      ordersCreated++;
    } catch {
      // Continue
    }
  }

  // 11. Create Sample Vouchers (Payment & Receipt)
  const bankOrCashLedger =
    ledgers.find(
      (l) =>
        l.name.toLowerCase().includes('bank') ||
        l.name.toLowerCase().includes('cash') ||
        l.code.startsWith('10'),
    )?.ledgerId || ledgers[0]?.ledgerId || '';

  if (customerLedger1 && bankOrCashLedger) {
    try {
      // Receipt Voucher
      await request('/accounting/vouchers', {
        method: 'POST',
        body: {
          type: 1, // Receipt
          date: today,
          referenceNumber: 'REC-BANK-8821',
          narration: 'Bank transfer receipt against invoice INV-1001',
          paymentMode: 'Bank Transfer',
          postImmediately: true,
          lines: [
            {
              ledgerId: bankOrCashLedger,
              amount: 5000.0,
              isDebit: true,
              narration: 'Commercial Bank of Qatar deposit',
            },
            {
              ledgerId: customerLedger1,
              amount: 5000.0,
              isDebit: false,
              narration: 'Receipt from Al-Noor Trading LLC',
            },
          ],
        },
      });
      vouchersCreated++;
    } catch {
      // Continue
    }
  }

  if (supplierLedger1 && bankOrCashLedger) {
    try {
      // Payment Voucher
      await request('/accounting/vouchers', {
        method: 'POST',
        body: {
          type: 2, // Payment
          date: today,
          referenceNumber: 'PAY-CHQ-1009',
          narration: 'Payment to Apex Hardware Supplies for stock shipment',
          paymentMode: 'Cheque',
          postImmediately: true,
          lines: [
            {
              ledgerId: supplierLedger1,
              amount: 8500.0,
              isDebit: true,
              narration: 'Payment against purchase bill PI-1001',
            },
            {
              ledgerId: bankOrCashLedger,
              amount: 8500.0,
              isDebit: false,
              narration: 'Direct settlement from Bank Current Account',
            },
          ],
        },
      });
      vouchersCreated++;
    } catch {
      // Continue
    }
  }

  return {
    salesInvoicesCreated,
    purchaseInvoicesCreated,
    ordersCreated,
    vouchersCreated,
  };
}
