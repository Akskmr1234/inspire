import { describe, expect, it } from 'vitest';
import { printableSale, type TotalKey } from '@/lib/printing';
import type { CustomerSummary } from '@/lib/customers';
import type { SalesInvoiceDetail } from '@/lib/sales';

const labels: Record<TotalKey, string> = {
  taxable: 'Taxable',
  tax: 'Tax',
  charges: 'Charges',
  roundOff: 'Round off',
};

const named = {
  customerId: 'c-1',
  code: '1001',
  name: 'Rashid Trading',
  nameArabic: null,
  contact: {
    mobileNumber: '5551 0000',
    phone: null,
    email: null,
    addressLine1: 'Street 4',
    addressLine2: 'Doha',
  },
  taxDetails: { registrationNumber: 'TRN-9' },
} as unknown as CustomerSummary;

const primary = {
  ...named,
  customerId: 'c-primary',
  code: '1000',
  name: 'Primary Customer',
  contact: {
    mobileNumber: null,
    phone: null,
    email: null,
    addressLine1: null,
    addressLine2: null,
  },
  taxDetails: { registrationNumber: null },
} as unknown as CustomerSummary;

function invoice(over: Partial<SalesInvoiceDetail>): SalesInvoiceDetail {
  return {
    header: {
      salesInvoiceId: 'i-1',
      number: 'SL/1',
      status: 2,
      taxable: 10,
      tax: 0,
      chargeTotal: 0,
      roundingDifference: 0,
      total: 10,
    },
    date: '2026-11-22',
    customerLedgerId: 'c-primary',
    warehouseId: 'w-1',
    mode: 2,
    currency: 'QAR',
    referenceNumber: null,
    narration: null,
    lines: [],
    charges: [],
    stockDocumentId: null,
    billId: null,
    journalVoucherId: null,
    kind: 1,
    returnsInvoiceId: null,
    paymentMode: 2,
    salesmanLedgerId: null,
    billingManLedgerId: null,
    createdAtUtc: '2026-11-22T00:00:00Z',
    amountReceived: 0,
    receivedIntoLedgerId: null,
    paymentReference: null,
    receiptVoucherId: null,
    ...over,
  } as SalesInvoiceDetail;
}

const lookups = { customers: [named, primary], products: [], warehouses: [] };

describe('who a printed sale is made out to', () => {
  it('is the walk-in customer the counter typed: name, address and mobile', () => {
    const printed = printableSale(
      invoice({
        billingName: 'Ahmed Ali',
        billingMobile: '5550 1234',
        billingAddress: 'Al Sadd, Doha',
      }),
      lookups,
      labels,
    );

    expect(printed.party?.name).toBe('Ahmed Ali');
    expect(printed.party?.lines).toEqual(['Al Sadd, Doha', '5550 1234']);
  });

  it('is a named customer with their own name, address and mobile', () => {
    const printed = printableSale(invoice({ customerLedgerId: 'c-1' }), lookups, labels);

    expect(printed.party?.name).toBe('Rashid Trading');
    expect(printed.party?.lines).toEqual(['Street 4, Doha', '5551 0000']);
  });
});
