import { describe, expect, it } from 'vitest';
import {
  allZero,
  axisFormatter,
  formatKey,
  keyDate,
  niceStep,
  niceTicks,
  sharesOf,
} from '@/lib/dashboardCharts';
import { barPath } from '@/components/charts/ChartParts';

/*
  The arithmetic under the dashboard's charts. A chart that draws is not a chart that
  is right: an axis that started above zero, a share worked out against the wrong
  whole or a month that slipped a day in some time zone all render without a murmur.
*/

describe('niceStep', () => {
  it('rounds a step to 1, 2, 2.5 or 5 of a power of ten', () => {
    expect(niceStep(100, 4)).toBe(25);
    expect(niceStep(1000, 4)).toBe(250);
    expect(niceStep(90, 4)).toBe(25);
    expect(niceStep(7, 4)).toBe(2);
    expect(niceStep(0.9, 4)).toBe(0.25);
  });

  it('falls back to one for a span with nothing in it', () => {
    expect(niceStep(0, 4)).toBe(1);
    expect(niceStep(Number.NaN, 4)).toBe(1);
  });
});

describe('niceTicks', () => {
  it('starts at zero, so a bar is never drawn from somewhere else', () => {
    const ticks = niceTicks([180_000, 320_000]);

    expect(ticks[0]).toBe(0);
    expect(ticks[ticks.length - 1]).toBeGreaterThanOrEqual(320_000);
  });

  it('reaches below zero for a loss, with zero still among the ticks', () => {
    const ticks = niceTicks([-8_500, 45_600]);

    expect(ticks[0]).toBeLessThanOrEqual(-8_500);
    expect(ticks).toContain(0);
    expect(ticks[ticks.length - 1]).toBeGreaterThanOrEqual(45_600);
  });

  it('steps evenly, without floating-point crumbs', () => {
    const ticks = niceTicks([0.3]);

    expect(ticks).toEqual([0, 0.1, 0.2, 0.3]);
  });

  it('still gives a quiet month a scale to draw on', () => {
    expect(niceTicks([0, 0, 0])).toEqual([0, 1]);
    expect(niceTicks([])).toEqual([0, 1]);
  });
});

describe('axisFormatter', () => {
  it('writes figures in full under ten thousand', () => {
    const format = axisFormatter([0, 2_000, 4_000, 6_000]);

    expect(format(4_000)).toBe(
      (4_000).toLocaleString(undefined, { maximumFractionDigits: 0 }),
    );
  });

  it('writes every tick compact on an axis that reaches ten thousand', () => {
    const format = axisFormatter([0, 10_000, 20_000]);
    const compact = new Intl.NumberFormat(undefined, {
      notation: 'compact',
      maximumFractionDigits: 1,
    });

    expect(format(0)).toBe(compact.format(0));
    expect(format(20_000)).toBe(compact.format(20_000));
  });

  it('keeps the decimals a fractional step needs', () => {
    expect(axisFormatter([0, 0.25, 0.5])(0.25)).toBe((0.25).toLocaleString());
  });
});

describe('sharesOf', () => {
  it('divides each part by the whole', () => {
    expect(sharesOf([50, 30, 20])).toEqual([0.5, 0.3, 0.2]);
  });

  it('gives no share to a part that came out negative', () => {
    // More refunded by card than taken by card: not a share of anything.
    const shares = sharesOf([120, -20]);

    expect(shares[0]).toBeCloseTo(1.2);
    expect(shares[1]).toBeNull();
  });

  it('gives no shares at all of a whole that is not above zero', () => {
    expect(sharesOf([0, 0])).toEqual([null, null]);
    expect(sharesOf([10, -30])).toEqual([null, null]);
  });
});

describe('keys as dates', () => {
  it('reads a day and a month at midnight UTC, so no time zone moves them', () => {
    expect(keyDate('2026-11-03').toISOString()).toBe('2026-11-03T00:00:00.000Z');
    expect(keyDate('2026-07').toISOString()).toBe('2026-07-01T00:00:00.000Z');
  });

  it('names the month in the reader language with Western digits', () => {
    expect(formatKey('2026-07', 'en', { month: 'long', year: 'numeric' })).toBe(
      'July 2026',
    );
    expect(formatKey('2026-11-03', 'ar', { day: 'numeric' })).toBe('3');
  });
});

describe('allZero', () => {
  it('treats figures that are zero or missing as nothing to draw', () => {
    expect(allZero([0, null, 0])).toBe(true);
    expect(allZero([0, 0.01])).toBe(false);
  });
});

describe('barPath', () => {
  it('rounds the top of a column growing up and leaves its foot square', () => {
    const path = barPath(10, 20, 100, 40);

    expect(path.startsWith('M10,100V44A4,4 0 0 1 14,40')).toBe(true);
    expect(path.endsWith('V100Z')).toBe(true);
  });

  it('rounds the bottom of a loss growing down from the line', () => {
    const path = barPath(10, 20, 100, 160);

    expect(path.startsWith('M10,100V156A4,4 0 0 0 14,160')).toBe(true);
  });

  it('draws nothing for a column too short to see', () => {
    expect(barPath(10, 20, 100, 100.2)).toBe('');
  });

  it('never rounds a short column more than it is tall', () => {
    expect(barPath(0, 20, 100, 98)).toContain('A2,2');
  });
});
