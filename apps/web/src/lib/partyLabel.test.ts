import { describe, expect, it } from 'vitest';
import { LedgerKind } from '@/lib/ledgers';
import { ledgerLabel, partyLabel } from '@/lib/partyLabel';

describe('how a customer or supplier reads in a dropdown', () => {
  it('shows code and name, or the name alone, as General Settings says', () => {
    const primary = { code: '1000', name: 'Primary Customer' };

    expect(partyLabel(primary, 'codeAndName')).toBe('1000 — Primary Customer');
    expect(partyLabel(primary, 'nameOnly')).toBe('Primary Customer');
  });

  it('never repeats a code that is the same as the name', () => {
    expect(partyLabel({ code: 'Cash Sale', name: 'CASH SALE' }, 'codeAndName')).toBe(
      'CASH SALE',
    );
  });

  it('falls back to whichever of the two there is', () => {
    expect(partyLabel({ code: '', name: 'Ahmed' }, 'codeAndName')).toBe('Ahmed');
    expect(partyLabel({ code: '2001', name: ' ' }, 'nameOnly')).toBe('2001');
  });
});

describe('how any ledger reads in a dropdown', () => {
  it('follows the setting for customers and suppliers, and not for other ledgers', () => {
    const customer = { code: '1000', name: 'Primary Customer', kind: LedgerKind.customer };
    const supplier = { code: '1000', name: 'Primary Supplier', kind: LedgerKind.supplier };
    const cash = { code: 'CASH', name: 'Cash in hand', kind: LedgerKind.cash };

    expect(ledgerLabel(customer, 'nameOnly')).toBe('Primary Customer');
    expect(ledgerLabel(supplier, 'nameOnly')).toBe('Primary Supplier');
    expect(ledgerLabel(cash, 'nameOnly')).toBe('CASH — Cash in hand');
  });

  it('shows a code that is the same as the name once, whatever the ledger', () => {
    expect(
      ledgerLabel({ code: 'Primary Customer', name: 'Primary Customer', kind: LedgerKind.customer }, 'codeAndName'),
    ).toBe('Primary Customer');
    expect(ledgerLabel({ code: 'SALES', name: 'Sales', kind: LedgerKind.general }, 'codeAndName')).toBe(
      'Sales',
    );
  });
});
