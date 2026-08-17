/**
 * Responses shaped like the API's, keyed by the path the client asks for.
 *
 * These exist so a page can be rendered without a server. They are deliberately
 * built from the same DTO field names the backend declares — a fixture that guesses
 * a field name tests nothing except the guess, and worse, fails in a way that looks
 * exactly like an application bug.
 *
 * The lookup ignores the query string: no test here asserts on filtering, and
 * matching on it would mean rewriting a fixture key every time a screen adds a
 * parameter.
 */

const ledgers = [
  {
    ledgerId: 'l1',
    code: '1000',
    name: 'Cash in hand',
    kind: 2,
    groupCode: 'CA',
    groupName: 'Current assets',
    nature: 1,
    currency: 'AED',
    isBillWise: false,
  },
  {
    ledgerId: 'l2',
    code: '4000',
    name: 'Sales revenue',
    kind: 1,
    groupCode: 'IN',
    groupName: 'Income',
    nature: 4,
    currency: 'AED',
    isBillWise: false,
  },
];

const trialBalanceRow = {
  ledgerId: 'l1',
  ledgerCode: '1000',
  ledgerName: 'Cash in hand',
  groupCode: 'CA',
  groupName: 'Current assets',
  openingDebit: 125000,
  openingCredit: 0,
  periodDebit: 480250.75,
  periodCredit: 392100.5,
  closingDebit: 213150.25,
  closingCredit: 0,
};

const statementLine = {
  groupCode: 'IN',
  groupName: 'Income',
  ledgerCode: '4000',
  ledgerName: 'Sales revenue',
  amount: 1685400.9,
};

const chequeLine = {
  chequeId: 'q1',
  chequeNumber: '004512',
  partyCode: 'C-1042',
  partyName: 'Gulf Trading LLC',
  direction: 1,
  status: 1,
  instrumentDate: '2026-09-01',
  recordedOn: '2026-08-02',
  bankName: 'Mashreq',
  amount: 96500,
  daysUntilDue: 15,
  currency: 'AED',
};

/** Matches `BatchStockRow`: unitCost and purchaseRate, not averageCost. */
const batchRow = {
  batchId: 'b1',
  batchNumber: 'B-2608',
  productId: 'p1',
  productCode: 'P-0001',
  productDescription: 'A4 copier paper',
  stockUnitCode: 'BOX',
  warehouseId: 'w1',
  warehouseName: 'Main warehouse',
  quantity: 340,
  unitCost: 42.1,
  value: 14314,
  purchaseRate: 44,
  manufacturedOn: '2026-06-01',
  expiresOn: '2028-08-01',
  daysToExpiry: 715,
};

const product = {
  id: 'p1',
  code: 'P-0001',
  description: 'A4 copier paper',
  descriptionArabic: null,
  categoryName: 'Stationery',
  brandName: 'Acme',
  stockUnitCode: 'BOX',
  cost: 42.5,
  retailRate: 61,
  reorderLevel: 40,
  barcodeCount: 2,
  tracksBatches: true,
  tracksSerialNumbers: false,
  isActive: true,
  isDiscontinued: false,
};

const master = { id: 'm1', code: 'M-1', name: 'A master record', isActive: true };

const party = {
  code: 'C-1042',
  name: 'Gulf Trading LLC',
  nameArabic: null,
  contact: {
    contactPerson: null,
    mobileNumber: null,
    phone: null,
    email: null,
    addressLine1: null,
    addressLine2: null,
    city: null,
    country: null,
  },
  terms: { creditLimit: null, creditDays: null, isBillWise: false },
  taxDetails: { registrationNumber: null, stateCode: null },
  currency: 'AED',
  openingBalance: 0,
  isActive: true,
};

export const fixtures: Readonly<Record<string, unknown>> = {
  '/auth/permissions': ['*'],
  '/menu': { items: [] },

  '/accounting/ledgers': ledgers,

  '/accounting/reports/trial-balance': {
    from: '2026-01-01',
    to: '2026-12-31',
    currency: 'AED',
    rows: [trialBalanceRow],
    totalOpeningDebit: 125000,
    totalOpeningCredit: 0,
    totalPeriodDebit: 480250.75,
    totalPeriodCredit: 392100.5,
    totalClosingDebit: 213150.25,
    totalClosingCredit: 0,
    isBalanced: true,
  },

  '/accounting/reports/profit-and-loss': {
    currency: 'AED',
    income: [statementLine],
    expenses: [{ ...statementLine, ledgerCode: '5000', ledgerName: 'Cost of sales' }],
    totalIncome: 1685400.9,
    totalExpenses: 1094320.4,
    netProfit: 591080.5,
  },

  '/accounting/reports/balance-sheet': {
    currency: 'AED',
    assets: [statementLine],
    liabilities: [statementLine],
    equity: [statementLine],
    totalAssets: 1,
    totalLiabilities: 1,
    totalEquity: 1,
    retainedEarnings: 0,
    totalLiabilitiesAndEquity: 1,
    isBalanced: true,
  },

  '/accounting/reports/account-group-summary': {
    from: '2026-01-01',
    to: '2026-12-31',
    currency: 'AED',
    groups: [
      {
        groupCode: 'CA',
        groupName: 'Current assets',
        nature: 1,
        openingDebit: 1,
        openingCredit: 0,
        periodDebit: 1,
        periodCredit: 0,
        closingDebit: 1,
        closingCredit: 0,
        ledgerCount: 1,
        ledgers: [trialBalanceRow],
      },
    ],
    totalOpeningDebit: 1,
    totalOpeningCredit: 0,
    totalPeriodDebit: 1,
    totalPeriodCredit: 0,
    totalClosingDebit: 1,
    totalClosingCredit: 0,
    isBalanced: true,
  },

  '/accounting/reports/day-book': {
    from: '2026-08-01',
    to: '2026-08-17',
    currency: 'AED',
    totalDebit: 31500,
    totalCredit: 31500,
    voucherCount: 1,
    entries: [
      {
        voucherId: 'v1',
        date: '2026-08-03',
        voucherNumber: 'JV-000241',
        voucherType: 'Journal',
        referenceNumber: null,
        narration: 'August retainer',
        amount: 31500,
        lines: [
          {
            ledgerId: 'l1',
            ledgerCode: '1000',
            ledgerName: 'Cash in hand',
            narration: null,
            debit: 31500,
            credit: 0,
          },
        ],
      },
    ],
  },

  '/accounting/reports/voucher-report': {
    from: '2026-08-01',
    to: '2026-08-17',
    currency: 'AED',
    vouchers: [
      {
        voucherId: 'v1',
        date: '2026-08-03',
        voucherNumber: 'JV-000241',
        type: 5,
        status: 2,
        referenceNumber: null,
        narration: null,
        currency: 'AED',
        exchangeRate: 1,
        documentAmount: 31500,
        baseAmount: 31500,
      },
    ],
    voucherCount: 1,
    totalBaseAmount: 31500,
    countByStatus: { 2: 1 },
    currencies: ['AED'],
  },

  '/accounting/reports/transaction-summary': {
    from: '2026-01-01',
    to: '2026-12-31',
    currency: 'AED',
    types: [{ type: 5, voucherCount: 1, totalAmount: 31500, countByStatus: { 2: 1 } }],
    months: [{ year: 2026, month: 8, voucherCount: 1, totalAmount: 31500 }],
    voucherCount: 1,
    totalAmount: 31500,
    countByStatus: { 2: 1 },
  },

  '/accounting/reports/cash-flow': {
    from: '2026-01-01',
    to: '2026-12-31',
    currency: 'AED',
    sections: [
      {
        category: 1,
        lines: [
          {
            ledgerId: 'l1',
            ledgerCode: '1000',
            ledgerName: 'Cash in hand',
            inflow: 1,
            outflow: 0,
            net: 1,
          },
        ],
        inflow: 1,
        outflow: 0,
        net: 1,
      },
    ],
    openingBalance: 0,
    closingBalance: 1,
    netChange: 1,
    isReconciled: true,
  },

  '/accounting/reports/cash-book': {
    from: '2026-08-01',
    to: '2026-08-17',
    currency: 'AED',
    accounts: [
      {
        ledgerId: 'l1',
        ledgerCode: '1000',
        ledgerName: 'Cash in hand',
        openingBalance: 0,
        closingBalance: 1,
        totalReceipts: 1,
        totalPayments: 0,
        lines: [
          {
            date: '2026-08-03',
            voucherId: 'v1',
            voucherNumber: 'CR-1',
            referenceNumber: null,
            narration: null,
            contraLedgerNames: ['Sales revenue'],
            debit: 1,
            credit: 0,
            runningBalance: 1,
          },
        ],
      },
    ],
    totalOpeningBalance: 0,
    totalClosingBalance: 1,
    totalReceipts: 1,
    totalPayments: 0,
  },

  '/accounting/reports/post-dated-cheques': {
    asAt: '2026-08-17',
    currency: 'AED',
    cheques: [chequeLine],
    totalReceivable: 96500,
    totalPayable: 0,
    currencies: ['AED'],
  },

  '/accounting/reports/cheque-register': {
    from: '2026-08-01',
    to: '2026-08-17',
    currency: 'AED',
    cheques: [chequeLine],
    totalReceived: 96500,
    totalIssued: 0,
    countByStatus: { 1: 1 },
    currencies: ['AED'],
  },

  '/accounting/reports/cheque-calendar': {
    from: '2026-08-17',
    to: '2026-09-30',
    currency: 'AED',
    days: [
      {
        date: '2026-09-01',
        receivable: 96500,
        payable: 0,
        net: 96500,
        cheques: [chequeLine],
      },
    ],
    totalReceivable: 96500,
    totalPayable: 0,
    currencies: ['AED'],
  },

  '/accounting/reports/tax-summary': {
    from: '2026-07-01',
    to: '2026-09-30',
    regime: 1,
    currency: 'AED',
    taxableSupplies: 1,
    zeroRatedSupplies: 0,
    taxablePurchases: 1,
    zeroRatedPurchases: 0,
    lines: [
      {
        component: 1,
        outputTax: 1,
        inputTax: 0,
        netPayable: 1,
        outputTaxPosted: 1,
        difference: 0,
        inputTaxPosted: 0,
        inputDifference: 0,
      },
    ],
    netPayable: 1,
    isReconciled: true,
  },

  '/dashboards': {
    dashboards: [
      {
        id: 'd1',
        code: 'MAIN',
        name: 'Overview',
        nameArabic: null,
        widgets: [
          {
            id: 'w1',
            metricCode: 'cash',
            isCustom: false,
            title: 'Cash position',
            titleArabic: null,
            kind: 1,
            span: 1,
          },
          {
            id: 'w2',
            metricCode: 'sales',
            isCustom: false,
            title: 'Sales by month',
            titleArabic: null,
            kind: 2,
            span: 2,
          },
        ],
      },
    ],
  },

  '/dashboards/d1/data': {
    dashboardId: 'd1',
    asAt: '2026-08-17',
    currency: 'AED',
    metrics: [
      {
        widgetId: 'w1',
        metricCode: 'cash',
        value: 1,
        count: 0,
        series: [],
        isPermitted: true,
        error: null,
      },
      {
        widgetId: 'w2',
        metricCode: 'sales',
        value: 0,
        count: 0,
        series: [{ label: '2026-08', value: 1 }],
        isPermitted: true,
        error: null,
      },
    ],
  },

  '/admin/menu': {
    items: [
      {
        id: 'g1',
        code: 'ACC',
        label: 'Accounting',
        labelArabic: null,
        icon: null,
        route: null,
        module: 'accounting',
        requiredPermission: null,
        sortOrder: 1,
        isEnabled: true,
        isSystem: true,
        children: [],
      },
    ],
  },

  '/inventory/products': [product],
  '/inventory/units': [{ ...master, symbol: 'pc', decimalPlaces: 0 }],
  '/inventory/categories': [{ ...master, parentId: null, parentName: null }],
  '/inventory/brands': [master],
  '/inventory/warehouses': [{ ...master, isDefault: true, address: null }],

  '/inventory/stock/documents': [
    {
      id: 'sd1',
      number: 'GRN-1',
      type: 1,
      date: '2026-08-04',
      warehouseName: 'Main warehouse',
      destinationWarehouseName: null,
      referenceNumber: null,
      narration: null,
      status: 2,
      lineCount: 1,
      totalQuantity: 1,
      totalValue: 1,
    },
  ],

  '/inventory/stock/valuation': {
    currency: 'AED',
    totalValue: 1,
    rows: [
      {
        productId: 'p1',
        productCode: 'P-0001',
        productDescription: 'A4 copier paper',
        categoryName: 'Stationery',
        warehouseId: 'w1',
        warehouseName: 'Main warehouse',
        stockUnitCode: 'BOX',
        quantity: 1,
        averageCost: 1,
        value: 1,
        reorderLevel: 0,
        isBelowReorderLevel: false,
      },
    ],
  },

  '/inventory/stock/ledger': {
    productCode: 'P-0001',
    productDescription: 'A4 copier paper',
    stockUnitCode: 'BOX',
    currency: 'AED',
    openingQuantity: 0,
    rows: [],
    closingQuantity: 0,
    totalIn: 0,
    totalOut: 0,
  },

  '/inventory/stock/movement': [
    {
      productId: 'p1',
      productCode: 'P-0001',
      productDescription: 'A4 copier paper',
      categoryName: 'Stationery',
      stockUnitCode: 'BOX',
      quantityIn: 1,
      quantityOut: 0,
      valueIn: 1,
      valueOut: 0,
      movements: 1,
      lastMovedOn: '2026-08-04',
    },
  ],

  '/inventory/stock/batch-stock': {
    currency: 'AED',
    totalValue: 14314,
    rows: [batchRow],
  },
  '/inventory/stock/expiry': [batchRow],
  '/inventory/stock/batches': [batchRow],
  '/inventory/stock/serials': [],

  '/sales/invoices': {
    items: [
      {
        salesInvoiceId: 'si1',
        number: 'INV-1',
        kind: 1,
        date: '2026-07-14',
        customerName: 'Gulf Trading LLC',
        referenceNumber: null,
        lineCount: 1,
        taxable: 1,
        tax: 0,
        total: 1,
        status: 2,
      },
    ],
    page: 1,
    pageSize: 50,
    totalCount: 1,
    totalPages: 1,
  },

  '/sales/customers': [{ ...party, customerId: 'cu1' }],

  '/purchase/invoices': {
    items: [
      {
        purchaseInvoiceId: 'pi1',
        number: 'PUR-1',
        kind: 1,
        date: '2026-07-09',
        supplierName: 'Emirates Paper Co',
        supplierInvoiceNumber: null,
        lineCount: 1,
        taxable: 1,
        tax: 0,
        total: 1,
        status: 2,
      },
    ],
    page: 1,
    pageSize: 50,
    totalCount: 1,
    totalPages: 1,
  },

  '/purchase/suppliers': [{ ...party, supplierId: 'su1', code: 'S-220' }],
};

/** The response for a path, or undefined when nothing is registered for it. */
export function fixtureFor(path: string): unknown {
  return fixtures[path.split('?')[0] ?? path];
}
