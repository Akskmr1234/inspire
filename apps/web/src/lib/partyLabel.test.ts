import { describe, expect, it } from 'vitest';
import { partyLabel } from '@/lib/partyLabel';

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
