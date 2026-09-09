import { describe, expect, it } from 'vitest';
import i18next from '@/i18n';

/*
  Two failures found by opening the application and looking at it: the chart of
  accounts printed `ledgerKinds.undefined` in a column, and the day book printed
  `reports.voucherCount`. Both are the same shape — a key that does not resolve,
  rendered as a dotted path at the person using the screen.
*/

describe('translation coverage', () => {
  it('names every ledger kind the API can return', () => {
    // Eight in the domain enum. Charge accounts are seeded with the standard
    // chart, so a firm has them from its first day and the three that were
    // missing were not exotic.
    for (const kind of [
      'General',
      'Cash',
      'Bank',
      'Customer',
      'Supplier',
      'Employee',
      'Tax',
      'AdditionalCharge',
    ]) {
      const text = i18next.t(`ledgerKinds.${kind}`);

      expect(text).not.toContain('ledgerKinds.');
      expect(text.length).toBeGreaterThan(0);
    }
  });

  it('names every account nature', () => {
    for (const nature of ['Asset', 'Liability', 'Equity', 'Income', 'Expense']) {
      expect(i18next.t(`accountNatures.${nature}`)).not.toContain('accountNatures.');
    }
  });
});

describe('a key that does not resolve', () => {
  it('degrades to a phrase rather than a dotted path', () => {
    expect(i18next.t('nowhere.voucherCount')).toBe('Voucher count');
    expect(i18next.t('some.namespace.newInvoice')).toBe('New invoice');
  });

  it('shows a dash where the key itself ends in nothing meaningful', () => {
    // What `t(`ledgerKinds.${MAP[kind]}`)` produces when the map has no entry.
    expect(i18next.t('ledgerKinds.undefined')).toBe('—');
  });

  it('still resolves the keys that do exist', () => {
    expect(i18next.t('common.close')).toBe('Close');
  });
});
