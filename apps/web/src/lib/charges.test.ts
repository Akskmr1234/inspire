import { describe, expect, it } from 'vitest';
import { chargesFor, type DefaultCharge } from '@/lib/charges';
import { LedgerKind, type LedgerSummary } from '@/lib/ledgers';

/*
  The charges a new document starts with.

  What is worth pinning is the filtering, not the lookup: a head somebody set as a
  default and later took off the chart would otherwise arrive on every new document
  as a row whose picker cannot be satisfied, and the entry would be refused by the
  server naming a ledger nobody chose.
*/

function ledger(
  id: string,
  code: string,
  kind: number = LedgerKind.additionalCharge,
): LedgerSummary {
  return {
    ledgerId: id,
    code,
    name: code,
    kind,
    groupCode: 'IND',
    groupName: 'Indirect',
    nature: 1,
    currency: 'AED',
    isBillWise: false,
  };
}

const chart: readonly LedgerSummary[] = [
  ledger('freight', 'FREIGHT'),
  ledger('packing', 'PACKING'),
  ledger('sales', 'SALES', LedgerKind.general),
];

const defaults: Readonly<Record<string, readonly DefaultCharge[]>> = {
  purchase: [
    { ledgerId: 'freight', amount: '25' },
    { ledgerId: 'gone', amount: '10' },
  ],
  sales: [{ ledgerId: 'packing', amount: '' }],
};

describe('the charges a document starts with', () => {
  it('keeps the heads still on the chart and drops the ones that left it', () => {
    expect(chargesFor('purchase', defaults, chart)).toEqual([
      { ledgerId: 'freight', amount: '25' },
    ]);
  });

  it('keeps a head set without an amount, which is a head to be priced per document', () => {
    expect(chargesFor('sales', defaults, chart)).toEqual([
      { ledgerId: 'packing', amount: '' },
    ]);
  });

  it('gives nothing for a document kind nothing is set against', () => {
    expect(chargesFor('salesOrder', defaults, chart)).toEqual([]);
  });

  it('gives nothing before the chart has arrived, rather than rows with no account', () => {
    expect(chargesFor('purchase', defaults, [])).toEqual([]);
  });
});
