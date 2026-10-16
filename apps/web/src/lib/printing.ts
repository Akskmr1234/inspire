import { useQuery } from '@tanstack/react-query';
import { request, type ApiError } from '@/lib/api';
import type { CustomerSummary } from '@/lib/customers';
import type { SupplierSummary } from '@/lib/suppliers';
import type { ProductSummary } from '@/lib/products';
import type { WarehouseSummary } from '@/lib/inventory';
import type { LedgerSummary } from '@/lib/ledgers';
import type { SalesInvoiceDetail } from '@/lib/sales';
import type { PurchaseInvoiceDetail } from '@/lib/purchase';

/** The paper a template is laid out for. The two narrow ones are till rolls. */
export type PaperSize = 'A4' | 'A5' | '80mm' | '58mm';

/** The documents that print, each with its own print settings. */
export const PRINT_DOCUMENTS = [
  'salesInvoice',
  'salesReturn',
  'purchaseInvoice',
  'purchaseReturn',
  'receipt',
  'payment',
  'journal',
] as const;

export type PrintDocumentType = (typeof PRINT_DOCUMENTS)[number];

/** Tax or non-tax: a firm prints them differently, often on different printers. */
export type PrintMode = 'tax' | 'nonTax';

export const PRINT_MODES: readonly PrintMode[] = ['tax', 'nonTax'];

/**
 * A layout somebody designed: what the firm's documents look like on paper.
 *
 * Everything that was hard-coded in the old invoice view lives here instead - the
 * company's name and tax number, the terms, the signatures - so what prints is what the
 * firm wrote rather than placeholder text nobody could change.
 */
export interface PrintTemplate {
  readonly id: string;
  readonly name: string;
  readonly paper: PaperSize;
  readonly company: {
    readonly name: string;
    readonly nameArabic: string;
    /** Address, phone, email: one line each. */
    readonly details: string;
    readonly taxNumber: string;
    /** A small image as a data URL, or empty for none. */
    readonly logo: string;
  };
  /** Blank prints the document's own title: Tax invoice, Credit note, Receipt… */
  readonly title: string;
  readonly show: {
    readonly arabicTitle: boolean;
    readonly partyAddress: boolean;
    readonly partyTaxNumber: boolean;
    readonly warehouse: boolean;
    readonly reference: boolean;
    readonly narration: boolean;
    readonly productCode: boolean;
    readonly serials: boolean;
    readonly amountInWords: boolean;
    readonly signatures: boolean;
  };
  readonly columns: {
    readonly unit: boolean;
    readonly rate: boolean;
    readonly discount: boolean;
    readonly taxPercent: boolean;
    readonly tax: boolean;
  };
  readonly footer: {
    readonly terms: string;
    readonly note: string;
    readonly signatureLeft: string;
    readonly signatureRight: string;
  };
  readonly style: {
    readonly fontScale: 'small' | 'normal' | 'large';
    /** The colour of the title band and the rules, as #rrggbb. */
    readonly accent: string;
    readonly borders: boolean;
  };
}

/** One print run: which layout, the printer it is meant for, and how many copies. */
export interface PrintJob {
  readonly template: string;
  /**
   * The printer it is meant for, by the name people know it by. A browser chooses the
   * printer in its own dialog, so this is shown there as the one to pick; it cannot be
   * selected silently.
   */
  readonly printer: string;
  readonly copies: number;
}

/** How one kind of document prints under one mode. */
export interface PrintProfile {
  /** Show the document first; off prints straight away. */
  readonly preview: boolean;
  readonly first: PrintJob;
  /** A second run after the first - a till receipt and an A4 office copy, say. */
  readonly second: PrintJob & { readonly enabled: boolean };
}

export type ProfileKey = `${PrintDocumentType}.${PrintMode}`;

/** Everything the firm has set about printing, held on the server for every counter. */
export interface PrintSetup {
  readonly templates: readonly PrintTemplate[];
  readonly profiles: Readonly<Partial<Record<ProfileKey, PrintProfile>>>;
  /** A customer who always gets their own layout, by customer id. */
  readonly customerTemplates: Readonly<Record<string, string>>;
  /** Mark copies Original, Duplicate, Triplicate. */
  readonly copyLabels: boolean;
}

const BLANK_COMPANY = { name: '', nameArabic: '', details: '', taxNumber: '', logo: '' };

const FULL_SHOW: PrintTemplate['show'] = {
  arabicTitle: true,
  partyAddress: true,
  partyTaxNumber: true,
  warehouse: false,
  reference: true,
  narration: true,
  productCode: true,
  serials: true,
  amountInWords: true,
  signatures: true,
};

const FOOTER: PrintTemplate['footer'] = {
  terms: '',
  note: 'Thank you for your business.',
  signatureLeft: 'Received by',
  signatureRight: 'Authorised signatory',
};

/** The layouts every firm starts with; they can be edited, copied or replaced. */
export const BUILT_IN_TEMPLATES: readonly PrintTemplate[] = [
  {
    id: 'classic-a4',
    name: 'Classic A4',
    paper: 'A4',
    company: BLANK_COMPANY,
    title: '',
    show: FULL_SHOW,
    columns: { unit: true, rate: true, discount: true, taxPercent: true, tax: true },
    footer: FOOTER,
    style: { fontScale: 'normal', accent: '#1e293b', borders: true },
  },
  {
    id: 'compact-a5',
    name: 'Compact A5',
    paper: 'A5',
    company: BLANK_COMPANY,
    title: '',
    show: { ...FULL_SHOW, partyAddress: false, productCode: false, signatures: false },
    columns: { unit: false, rate: true, discount: false, taxPercent: false, tax: true },
    footer: FOOTER,
    style: { fontScale: 'small', accent: '#1e293b', borders: true },
  },
  {
    id: 'thermal-80',
    name: 'Till roll 80 mm',
    paper: '80mm',
    company: BLANK_COMPANY,
    title: '',
    show: {
      ...FULL_SHOW,
      arabicTitle: false,
      partyAddress: false,
      partyTaxNumber: false,
      productCode: false,
      amountInWords: false,
      signatures: false,
    },
    columns: { unit: false, rate: true, discount: false, taxPercent: false, tax: false },
    footer: { ...FOOTER, signatureLeft: '', signatureRight: '' },
    style: { fontScale: 'small', accent: '#000000', borders: false },
  },
  {
    id: 'thermal-58',
    name: 'Till roll 58 mm',
    paper: '58mm',
    company: BLANK_COMPANY,
    title: '',
    show: {
      ...FULL_SHOW,
      arabicTitle: false,
      partyAddress: false,
      partyTaxNumber: false,
      productCode: false,
      reference: false,
      amountInWords: false,
      signatures: false,
    },
    columns: { unit: false, rate: false, discount: false, taxPercent: false, tax: false },
    footer: { ...FOOTER, signatureLeft: '', signatureRight: '' },
    style: { fontScale: 'small', accent: '#000000', borders: false },
  },
];

const DEFAULT_PROFILE: PrintProfile = {
  preview: true,
  first: { template: 'classic-a4', printer: '', copies: 1 },
  second: { enabled: false, template: 'thermal-80', printer: '', copies: 1 },
};

export const EMPTY_SETUP: PrintSetup = {
  templates: BUILT_IN_TEMPLATES,
  profiles: {},
  customerTemplates: {},
  copyLabels: true,
};

/** The profile a document prints with, falling back to the defaults. */
export function profileFor(
  setup: PrintSetup,
  type: PrintDocumentType,
  mode: PrintMode,
): PrintProfile {
  return setup.profiles[`${type}.${mode}`] ?? DEFAULT_PROFILE;
}

/** A template by id, falling back to the first one so a print never fails for want of one. */
export function templateFor(setup: PrintSetup, id: string): PrintTemplate {
  return (
    setup.templates.find((template) => template.id === id) ??
    setup.templates[0] ??
    BUILT_IN_TEMPLATES[0]!
  );
}

/** Reads a stored setup, keeping whatever parts of it still make sense. */
export function parseSetup(raw: string | null | undefined): PrintSetup {
  if (!raw) return EMPTY_SETUP;

  try {
    const stored = JSON.parse(raw) as Partial<PrintSetup>;
    const templates =
      Array.isArray(stored.templates) && stored.templates.length > 0
        ? stored.templates.map(withDefaults)
        : BUILT_IN_TEMPLATES;

    return {
      templates,
      profiles: stored.profiles ?? {},
      customerTemplates: stored.customerTemplates ?? {},
      copyLabels: stored.copyLabels ?? true,
    };
  } catch {
    return EMPTY_SETUP;
  }
}

/** Fills in anything a template saved by an older version did not have. */
function withDefaults(template: Partial<PrintTemplate>): PrintTemplate {
  const base = BUILT_IN_TEMPLATES[0]!;

  return {
    ...base,
    ...template,
    company: { ...base.company, ...template.company },
    show: { ...base.show, ...template.show },
    columns: { ...base.columns, ...template.columns },
    footer: { ...base.footer, ...template.footer },
    style: { ...base.style, ...template.style },
  } as PrintTemplate;
}

const SETTING = '/settings/printing';

/** The firm's print setup, read from the server. */
export function usePrintSetup(): {
  readonly setup: PrintSetup;
  readonly loading: boolean;
  readonly error: ApiError | null;
} {
  const query = useQuery<PrintSetup, ApiError>({
    queryKey: ['print-setup'],
    queryFn: async () =>
      parseSetup((await request<{ readonly value: string | null }>(SETTING)).value),
    staleTime: 60_000,
  });

  return {
    setup: query.data ?? EMPTY_SETUP,
    loading: query.isPending,
    error: query.error,
  };
}

export function savePrintSetup(setup: PrintSetup): Promise<void> {
  return request<void>(SETTING, {
    method: 'PUT',
    body: { value: JSON.stringify(setup) },
  });
}

// ---------------------------------------------------------------------------------------
// What prints: one shape for every document, so one renderer serves them all.
// ---------------------------------------------------------------------------------------

export interface PrintParty {
  readonly name: string;
  readonly code: string | null;
  readonly lines: readonly string[];
  readonly taxNumber: string | null;
}

export interface PrintGoodsLine {
  readonly description: string;
  readonly code: string | null;
  readonly serials: readonly string[];
  readonly quantity: number;
  readonly unit: string | null;
  readonly rate: number;
  readonly discount: number;
  readonly taxPercent: number;
  readonly tax: number;
  readonly total: number;
}

export interface PrintEntryLine {
  readonly account: string;
  readonly narration: string | null;
  readonly debit: number;
  readonly credit: number;
}

export interface PrintableDocument {
  readonly type: PrintDocumentType;
  readonly mode: PrintMode;
  readonly number: string;
  readonly date: string;
  readonly reference: string | null;
  readonly narration: string | null;
  readonly currency: string;
  readonly partyLabel: 'billTo' | 'supplier' | 'receivedFrom' | 'paidTo' | null;
  readonly party: PrintParty | null;
  readonly warehouse: string | null;
  readonly goods: readonly PrintGoodsLine[];
  readonly entries: readonly PrintEntryLine[];
  readonly totals: readonly { readonly label: string; readonly amount: number }[];
  readonly grandTotal: number;
  /** Draft or cancelled, printed across the page so a copy is never mistaken for the real one. */
  readonly watermark: 'draft' | 'cancelled' | null;
  /** The customer, where one may have a template of their own. */
  readonly customerId: string | null;
}

const TAX_MODE_NON_TAX = 1;

function partyOf(
  party: CustomerSummary | SupplierSummary | undefined,
  fallback: string,
): PrintParty {
  if (!party) return { name: fallback, code: null, lines: [], taxNumber: null };

  const contact = party.contact;

  return {
    name: party.name,
    code: party.code,
    lines: [
      [contact.addressLine1, contact.addressLine2].filter(Boolean).join(', '),
      contact.mobileNumber || contact.phone || '',
    ].filter((line) => line !== ''),
    taxNumber: party.taxDetails.registrationNumber,
  };
}

function watermarkOf(status: number): PrintableDocument['watermark'] {
  return status === 1 ? 'draft' : status === 3 ? 'cancelled' : null;
}

/** A sales invoice or return, as it prints. */
export function printableSale(
  invoice: SalesInvoiceDetail,
  lookups: {
    readonly customers: readonly CustomerSummary[];
    readonly products: readonly ProductSummary[];
    readonly warehouses: readonly WarehouseSummary[];
  },
  labels: Readonly<Record<'taxable' | 'tax' | 'charges' | 'roundOff', string>>,
): PrintableDocument {
  const products = new Map(lookups.products.map((product) => [product.id, product]));
  const customer = lookups.customers.find(
    (c) => c.customerId === invoice.customerLedgerId,
  );
  const warehouse = lookups.warehouses.find((w) => w.id === invoice.warehouseId);

  return {
    type: invoice.kind === 2 ? 'salesReturn' : 'salesInvoice',
    mode: invoice.mode === TAX_MODE_NON_TAX ? 'nonTax' : 'tax',
    number: invoice.header.number,
    date: invoice.date,
    reference: invoice.referenceNumber,
    narration: invoice.narration,
    currency: invoice.currency,
    partyLabel: 'billTo',
    party: partyOf(customer, '—'),
    warehouse: warehouse ? warehouse.name : null,
    goods: invoice.lines.map((line) => {
      const product = products.get(line.productId);

      return {
        description: product?.description ?? '—',
        code: product?.code ?? null,
        serials: line.serialNumbers ?? [],
        quantity: line.quantity,
        unit: product?.stockUnitCode ?? null,
        rate: line.rate,
        discount: line.discount,
        taxPercent: line.components.reduce((sum, head) => sum + head.percentage, 0),
        tax: line.tax,
        total: line.taxable + line.tax,
      };
    }),
    entries: [],
    totals: [
      { label: labels.taxable, amount: invoice.header.taxable },
      ...(invoice.header.tax !== 0
        ? [{ label: labels.tax, amount: invoice.header.tax }]
        : []),
      ...(invoice.header.chargeTotal !== 0
        ? [{ label: labels.charges, amount: invoice.header.chargeTotal }]
        : []),
      ...(invoice.header.roundingDifference !== 0
        ? [{ label: labels.roundOff, amount: invoice.header.roundingDifference }]
        : []),
    ],
    grandTotal: invoice.header.total,
    watermark: watermarkOf(invoice.header.status),
    customerId: invoice.customerLedgerId,
  };
}

/** A purchase or purchase return, as it prints. */
export function printablePurchase(
  invoice: PurchaseInvoiceDetail,
  lookups: {
    readonly suppliers: readonly SupplierSummary[];
    readonly products: readonly ProductSummary[];
    readonly warehouses: readonly WarehouseSummary[];
  },
  labels: Readonly<Record<'taxable' | 'tax' | 'charges' | 'roundOff', string>>,
): PrintableDocument {
  const products = new Map(lookups.products.map((product) => [product.id, product]));
  const supplier = lookups.suppliers.find(
    (s) => s.supplierId === invoice.supplierLedgerId,
  );
  const warehouse = lookups.warehouses.find((w) => w.id === invoice.warehouseId);

  return {
    type: invoice.kind === 2 ? 'purchaseReturn' : 'purchaseInvoice',
    mode: invoice.mode === TAX_MODE_NON_TAX ? 'nonTax' : 'tax',
    number: invoice.header.number,
    date: invoice.date,
    reference: invoice.supplierInvoiceNumber,
    narration: invoice.narration,
    currency: invoice.currency,
    partyLabel: 'supplier',
    party: partyOf(supplier, '—'),
    warehouse: warehouse ? warehouse.name : null,
    goods: invoice.lines.map((line) => {
      const product = products.get(line.productId);

      return {
        description: product?.description ?? '—',
        code: product?.code ?? null,
        serials: line.serialNumbers,
        quantity: line.quantity,
        unit: product?.stockUnitCode ?? null,
        rate: line.rate,
        discount: line.discount,
        taxPercent: line.components.reduce((sum, head) => sum + head.percentage, 0),
        tax: line.tax,
        total: line.taxable + line.tax,
      };
    }),
    entries: [],
    totals: [
      { label: labels.taxable, amount: invoice.header.taxable },
      ...(invoice.header.tax !== 0
        ? [{ label: labels.tax, amount: invoice.header.tax }]
        : []),
      ...(invoice.header.chargeTotal !== 0
        ? [{ label: labels.charges, amount: invoice.header.chargeTotal }]
        : []),
      ...(invoice.header.roundingDifference !== 0
        ? [{ label: labels.roundOff, amount: invoice.header.roundingDifference }]
        : []),
    ],
    grandTotal: invoice.header.total,
    watermark: watermarkOf(invoice.header.status),
    customerId: null,
  };
}

/** A voucher as the server reads it back. */
export interface VoucherForPrint {
  readonly number: string;
  readonly type: number;
  readonly status: number;
  readonly date: string;
  readonly currency: string;
  readonly referenceNumber: string | null;
  readonly narration: string | null;
  readonly totalDebit: number;
  readonly lines: readonly {
    readonly ledgerId: string;
    readonly side: number;
    readonly debitAmount: number;
    readonly creditAmount: number;
    readonly narration: string | null;
  }[];
}

/** A receipt, payment or journal, as it prints. */
export function printableVoucher(
  voucher: VoucherForPrint,
  ledgers: readonly LedgerSummary[],
): PrintableDocument {
  const names = new Map(ledgers.map((ledger) => [ledger.ledgerId, ledger]));
  const isReceipt = voucher.type === 1 || voucher.type === 2;
  const isPayment = voucher.type === 3 || voucher.type === 4;

  // The party is the side that is not the money: credited on a receipt, debited on a
  // payment, and a party-kind ledger either way.
  const partyLine = voucher.lines.find((line) => {
    const ledger = names.get(line.ledgerId);
    return ledger !== undefined && (ledger.kind === 4 || ledger.kind === 5);
  });
  const party = partyLine ? names.get(partyLine.ledgerId) : undefined;

  return {
    type: isReceipt ? 'receipt' : isPayment ? 'payment' : 'journal',
    mode: 'tax',
    number: voucher.number,
    date: voucher.date,
    reference: voucher.referenceNumber,
    narration: voucher.narration,
    currency: voucher.currency,
    partyLabel: isReceipt ? 'receivedFrom' : isPayment ? 'paidTo' : null,
    party: party
      ? { name: party.name, code: party.code, lines: [], taxNumber: null }
      : null,
    warehouse: null,
    goods: [],
    entries: voucher.lines.map((line) => ({
      account: names.get(line.ledgerId)?.name ?? '—',
      narration: line.narration,
      debit: line.debitAmount,
      credit: line.creditAmount,
    })),
    totals: [],
    grandTotal: voucher.totalDebit,
    watermark: watermarkOf(voucher.status),
    customerId: null,
  };
}
