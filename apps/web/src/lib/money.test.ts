import { describe, expect, it } from 'vitest';
import { decimalsFor, money, moneyAlways } from '@/lib/money';
import { branchOf } from '@/lib/branch';

/*
  Two decisions worth pinning: which branch a session is in, and how many places its
  figures are shown to. Both are read rather than asked for — the branch off the same
  token claim the server scopes by, the places off a setting keyed by it — and both
  have to degrade to something sensible rather than throw, because the inputs are a
  token from the network and a blob from localStorage.
*/

/**
 * Builds a token with the given payload. Unsigned: nothing here verifies one.
 *
 * The JSON is turned into UTF-8 bytes before it is base64'd, which is what a real
 * token does and what `btoa` on its own cannot — it refuses any character above
 * U+00FF. Doing it properly is what lets the Arabic case below actually exercise
 * the decoder rather than fail in the fixture.
 */
function token(payload: Record<string, unknown>): string {
  const bytes = new TextEncoder().encode(JSON.stringify(payload));
  const binary = String.fromCharCode(...bytes);

  const body = btoa(binary).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');

  return `header.${body}.signature`;
}

describe('the branch a session is in', () => {
  it('reads the claim the server scopes by', () => {
    expect(branchOf(token({ branch_id: 'b-1' }))).toBe('b-1');
  });

  it('takes the first of several, as the server does with no header to go on', () => {
    expect(branchOf(token({ branch_id: ['b-1', 'b-2'] }))).toBe('b-1');
  });

  it('answers null rather than throwing for a user who holds no branch', () => {
    expect(branchOf(token({ sub: 'u-1' }))).toBeNull();
    expect(branchOf(null)).toBeNull();
    expect(branchOf('not a token')).toBeNull();
    expect(branchOf('header.@@@notbase64@@@.signature')).toBeNull();
  });

  it('survives a name written in a script that needs more than one byte', () => {
    expect(branchOf(token({ branch_id: 'فرع-١' }))).toBe('فرع-١');
  });
});

describe('how many places a branch shows', () => {
  const settings = { decimals: 2, decimalsByBranch: { 'b-1': 3, 'b-2': 0 } };

  it('takes the branch its own answer', () => {
    expect(decimalsFor(settings, 'b-1')).toBe(3);
    expect(decimalsFor(settings, 'b-2')).toBe(0);
  });

  it('falls back to the firm where a branch has not chosen', () => {
    expect(decimalsFor(settings, 'b-3')).toBe(2);
    expect(decimalsFor(settings, null)).toBe(2);
  });

  it('keeps a branch that chose zero, rather than reading it as unset', () => {
    // `?? ` and not `|| `: nought places is a real choice — a yen price list — and
    // an or would have quietly sent it back to the firm's two.
    expect(decimalsFor({ decimals: 2, decimalsByBranch: { b: 0 } }, 'b')).toBe(0);
  });

  it('clamps whatever localStorage happens to be holding', () => {
    // `toLocaleString` throws outside 0–20, which would take down the screen that
    // showed a figure rather than merely printing it oddly.
    const wild = { decimals: 2, decimalsByBranch: { a: -3, b: 99, c: 2.7, d: NaN } };

    expect(decimalsFor(wild, 'a')).toBe(0);
    expect(decimalsFor(wild, 'b')).toBe(20);
    expect(decimalsFor(wild, 'c')).toBe(2);
    expect(decimalsFor(wild, 'd')).toBe(2);
  });
});

describe('writing a figure', () => {
  it('shows the places it is given', () => {
    expect(moneyAlways(1234.5678, 3)).toBe('1,234.568');
    expect(moneyAlways(1234.5678, 0)).toBe('1,235');
  });

  it('shows a zero total, because a total of nothing is a fact', () => {
    expect(moneyAlways(0, 2)).toBe('0.00');
  });

  it('blanks a zero in a column, so the eye follows the figures that are there', () => {
    expect(money(0, 2)).toBe('');
    expect(money(12, 2)).toBe('12.00');
  });
});
