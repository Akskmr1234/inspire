import { describe, expect, it } from 'vitest';
import { roundTotal } from '@/lib/sales';

/*
  The sale's grand total, rounded as the server rounds it: to the firm's unit, nearest
  and half away from zero, then to the currency. The screen and the saved bill have to
  agree to the fils, or the counter asks for one figure and the books record another.
*/

describe('rounding a sale to the firm’s unit', () => {
  it.each([
    [10.1, 0.25, 10],
    [10.13, 0.25, 10.25],
    [66.95, 0.25, 67],
    [10.1, 0.5, 10],
    [10.49, 1, 10],
    [10.5, 1, 11],
    [12.4, 5, 10],
    [103.95, 0.25, 104],
  ])('%f to the nearest %f is %f', (total, unit, expected) => {
    expect(roundTotal(total, unit)).toBe(expected);
  });

  it('rounds only to the currency where no unit is set', () => {
    expect(roundTotal(10.13, 0)).toBe(10.13);
    // A sum of rounded figures that floating point left a hair off.
    expect(roundTotal(0.1 + 0.2, 0)).toBe(0.3);
  });

  it('lands on a whole multiple however the unit divides', () => {
    expect(roundTotal(1.15, 0.1)).toBe(1.2);
    expect(roundTotal(2.675, 0.05)).toBe(2.7);
  });
});
