import { describe, expect, it } from 'vitest';
import {
  describeDevice,
  groupPermissions,
  humanise,
  passwordStrength,
} from '@/lib/account';
import {
  groupPaths,
  isPlainKind,
  LedgerKind,
  type AccountGroupSummary,
} from '@/lib/ledgers';
import { drCr } from '@/pages/LedgerStatementPage';

describe('describeDevice', () => {
  it('names the browser that claims to be several', () => {
    const edge =
      'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0 Safari/537.36 Edg/130.0';
    expect(describeDevice(edge)).toEqual({
      browser: 'Edge',
      system: 'Windows',
      mobile: false,
    });
  });

  it('knows a phone', () => {
    const iphone =
      'Mozilla/5.0 (iPhone; CPU iPhone OS 18_0 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/18.0 Mobile/15E148 Safari/604.1';
    expect(describeDevice(iphone)).toEqual({
      browser: 'Safari',
      system: 'iOS',
      mobile: true,
    });
  });

  it('says nothing it does not know', () => {
    expect(describeDevice(null)).toEqual({ browser: '', system: '', mobile: false });
  });
});

describe('passwordStrength', () => {
  it('rates a short password weak whatever it contains', () => {
    expect(passwordStrength('Ab1!')).toBe(1);
  });

  it('rates a long mixed one strong', () => {
    expect(passwordStrength('Correct-Horse-Battery-9')).toBe(4);
  });

  it('is empty for nothing', () => {
    expect(passwordStrength('')).toBe(0);
  });
});

describe('groupPermissions', () => {
  it('groups codes by module and resource, in order', () => {
    const grouped = groupPermissions([
      'sales:invoice:create',
      'accounting:report:view',
      'sales:invoice:view',
      'sales:customer:view',
      'not-a-code',
    ]);

    expect([...grouped.keys()]).toEqual(['accounting', 'sales']);
    expect([...grouped.get('sales')!.keys()]).toEqual(['customer', 'invoice']);
    expect(grouped.get('sales')!.get('invoice')).toEqual(['create', 'view']);
  });

  it('reads a code as words', () => {
    expect(humanise('stock-adjustment')).toBe('Stock adjustment');
  });
});

describe('groupPaths', () => {
  const group = (
    id: string,
    name: string,
    parentId: string | null,
  ): AccountGroupSummary => ({
    accountGroupId: id,
    code: id,
    name,
    nameArabic: null,
    nature: 1,
    parentId,
    isSystemGroup: parentId === null,
  });

  it('names a group by the groups above it', () => {
    const paths = groupPaths([
      group('1000', 'Assets', null),
      group('1100', 'Current Assets', '1000'),
      group('1110', 'Cash and Bank', '1100'),
    ]);

    expect(paths.get('1110')).toBe('Assets › Current Assets › Cash and Bank');
    expect(paths.get('1000')).toBe('Assets');
  });

  it('lets the chart edit only the ledgers without screens of their own', () => {
    expect(isPlainKind(LedgerKind.bank)).toBe(true);
    expect(isPlainKind(LedgerKind.customer)).toBe(false);
    expect(isPlainKind(LedgerKind.tax)).toBe(false);
  });
});

describe('drCr', () => {
  it('states a balance by its side', () => {
    expect(drCr(2481.25, 'Dr', 'Cr')).toBe('2,481.25 Dr');
    expect(drCr(-5000, 'Dr', 'Cr')).toBe('5,000.00 Cr');
    expect(drCr(0.001, 'Dr', 'Cr')).toBe('0.00');
  });
});
