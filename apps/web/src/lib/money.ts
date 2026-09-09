import { useSettings } from '@/stores/settings';
import { currentBranchId } from '@/lib/api';

/**
 * How money is written on the screen.
 *
 * One place decides the number of decimal places, because a document that showed
 * three on a line and two in its total would be reporting a rounding that never
 * happened. The places come from the branch the session is working in, falling back
 * to the firm-wide setting where a branch has said nothing — branches already keep
 * their own numbering and print formats, and precision belongs with them: a
 * wholesale branch pricing by the thousand and a retail counter in the same firm
 * genuinely want different answers.
 *
 * Presentation only. Every figure here has been through the server at full
 * precision and is stored that way; this decides what a reader sees, and rounding
 * for the books stays where it belongs, on the server, in the currency's own
 * smallest unit.
 */

/** The places to show, given the settings and the branch in hand. */
export function decimalsFor(
  settings: {
    readonly decimals: number;
    readonly decimalsByBranch: Readonly<Record<string, number>>;
  },
  branchId: string | null,
): number {
  const branch = branchId === null ? undefined : settings.decimalsByBranch[branchId];
  const places = branch ?? settings.decimals;

  // Clamped rather than trusted. The value reaches here from localStorage, which an
  // older release, a hand edit or a half-finished sync can leave holding anything,
  // and `toLocaleString` throws a RangeError outside 0–20 rather than doing
  // something harmless with it — which would take down the screen that displayed a
  // figure rather than merely printing it oddly.
  return Number.isFinite(places) ? Math.min(20, Math.max(0, Math.trunc(places))) : 2;
}

/** The places the session is currently showing. */
export function currentDecimals(): number {
  return decimalsFor(useSettings.getState(), currentBranchId());
}

/**
 * A figure for a financial column, blanking zero so the eye follows the numbers.
 *
 * A column of figures reads by its exceptions, and a screenful of `0.00` in every
 * cell that has nothing in it hides them. Totals use `moneyAlways`: a total of zero
 * is a fact, and a blank there reads as a figure that failed to arrive.
 */
export function money(value: number, places = currentDecimals()): string {
  return value === 0 ? '' : moneyAlways(value, places);
}

/** A figure that must always show, including zero. */
export function moneyAlways(value: number, places = currentDecimals()): string {
  return value.toLocaleString(undefined, {
    minimumFractionDigits: places,
    maximumFractionDigits: places,
  });
}

/**
 * The formatters, bound to the current setting and re-rendering when it changes.
 *
 * The free functions above read the store at call time, which is right for a
 * report body that is re-rendered anyway. A screen that must repaint the moment
 * somebody changes the setting subscribes through this instead.
 */
export function useMoney(): {
  readonly places: number;
  readonly money: (value: number) => string;
  readonly moneyAlways: (value: number) => string;
} {
  const decimals = useSettings((state) => state.decimals);
  const byBranch = useSettings((state) => state.decimalsByBranch);
  const places = decimalsFor({ decimals, decimalsByBranch: byBranch }, currentBranchId());

  return {
    places,
    money: (value) => money(value, places),
    moneyAlways: (value) => moneyAlways(value, places),
  };
}
