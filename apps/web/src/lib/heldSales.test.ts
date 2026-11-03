import { act, renderHook } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { discardHeldSale, holdSale, takeHeldSale, useHeldSales } from '@/lib/heldSales';

/*
  Bills on hold: put aside at the counter, listed for the rest of the day, taken up
  again or thrown away. What is worth pinning is that a bill comes back as it went,
  that taking one up takes it off the list, and that yesterday's are not offered.
*/

const SALE = 1;
const RETURN = 2;

function bill(party: string, kind = SALE): Parameters<typeof holdSale>[0] {
  return { kind, party, items: 2, total: 42.5, draft: { lines: [party] } };
}

describe('bills on hold', () => {
  beforeEach(() => {
    localStorage.clear();
    vi.useFakeTimers({ toFake: ['Date'] });
    vi.setSystemTime(new Date(2026, 10, 2, 10, 30));
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it('lists today’s bills of a kind, newest first', () => {
    holdSale(bill('Rashid'));
    vi.setSystemTime(new Date(2026, 10, 2, 11, 0));
    holdSale(bill('Amal'));
    holdSale(bill('Back again', RETURN));

    const { result } = renderHook(() => useHeldSales(SALE));

    expect(result.current.map((held) => held.party)).toEqual(['Amal', 'Rashid']);
  });

  it('gives a bill back as it was held, and takes it off the list', () => {
    const held = holdSale(bill('Rashid'));
    const { result } = renderHook(() => useHeldSales(SALE));

    let taken: ReturnType<typeof takeHeldSale>;
    act(() => {
      taken = takeHeldSale(held.id);
    });

    expect(taken!).toMatchObject({ party: 'Rashid', draft: { lines: ['Rashid'] } });
    expect(result.current).toHaveLength(0);
  });

  it('holds a bill taken up again under the name it had', () => {
    const first = holdSale(bill('Rashid'));
    takeHeldSale(first.id);
    const again = holdSale({ ...bill('Rashid Khan'), id: first.id });

    const { result } = renderHook(() => useHeldSales(SALE));

    expect(again.id).toBe(first.id);
    expect(result.current.map((held) => held.party)).toEqual(['Rashid Khan']);
  });

  it('throws a bill away', () => {
    const held = holdSale(bill('Rashid'));
    const { result } = renderHook(() => useHeldSales(SALE));

    act(() => discardHeldSale(held.id));

    expect(result.current).toHaveLength(0);
  });

  it('does not offer yesterday’s bills, and forgets them at the next hold', () => {
    holdSale(bill('Yesterday'));
    vi.setSystemTime(new Date(2026, 10, 3, 9, 0));

    const { result } = renderHook(() => useHeldSales(SALE));
    expect(result.current).toHaveLength(0);

    act(() => {
      holdSale(bill('Today'));
    });

    expect(result.current.map((held) => held.party)).toEqual(['Today']);
    expect(localStorage.getItem(localStorage.key(0) ?? '')).not.toContain('Yesterday');
  });
});
