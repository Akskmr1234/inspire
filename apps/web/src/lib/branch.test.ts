import { describe, expect, it } from 'vitest';
import { branchOf, branchesOf } from '@/lib/branch';

/** A token whose payload carries the given claims; the signature is never read here. */
function token(claims: Record<string, unknown>): string {
  const payload = btoa(JSON.stringify(claims))
    .replace(/\+/g, '-')
    .replace(/\//g, '_')
    .replace(/=+$/, '');
  return `header.${payload}.signature`;
}

describe('branchesOf', () => {
  it('lists every branch a token grants, in its order', () => {
    const many = token({ branch_id: ['b1', 'b2', 'b3'] });

    expect(branchesOf(many)).toEqual(['b1', 'b2', 'b3']);
    // The default the server takes when no branch is chosen: the first.
    expect(branchOf(many)).toBe('b1');
  });

  it('reads a token granting one branch, where the claim is not a list', () => {
    expect(branchesOf(token({ branch_id: 'b1' }))).toEqual(['b1']);
  });

  it('grants nothing without a token or a branch claim', () => {
    expect(branchesOf(null)).toEqual([]);
    expect(branchesOf(token({ sub: 'u1' }))).toEqual([]);
    expect(branchesOf('not-a-token')).toEqual([]);
  });
});
