import { request } from '@/lib/api';

/** One day's sales. */
export interface DailySalesPoint {
  readonly date: string;
  /** What was billed, with tax and charges, net of returns. */
  readonly sales: number;
  readonly bills: number;
}

/** One month's trading. A figure the caller may not read is null. */
export interface MonthlyTradingPoint {
  /** The month, as `yyyy-MM`. */
  readonly month: string;
  /** Before tax, net of returns: the sales accounts in the books. */
  readonly sales: number | null;
  /** Goods bought, before tax, net of returns. */
  readonly purchases: number | null;
  /** All income less all expense. */
  readonly profit: number | null;
}

/** How much of the month's sales was paid one way. */
export interface PaymentModeShare {
  /** The sales payment mode, as the server numbers it. */
  readonly mode: number;
  readonly amount: number;
  readonly bills: number;
}

/** A product or a category, and what it sold. */
export interface RankedSales {
  readonly label: string;
  /** A product's code; nothing for a category. */
  readonly detail: string | null;
  readonly amount: number;
  readonly quantity: number;
}

/** What customers owe that is a given number of days past its due date. */
export interface ReceivableAge {
  /** Zero for what is not yet due. */
  readonly fromDays: number;
  /** Inclusive; null for the open-ended last bucket. */
  readonly toDays: number | null;
  readonly amount: number;
}

/** The charts under the dashboard. A chart the caller may not read is null. */
export interface DashboardCharts {
  readonly asAt: string;
  readonly currency: string;
  readonly dailySales: readonly DailySalesPoint[] | null;
  readonly months: readonly MonthlyTradingPoint[] | null;
  readonly paymentModes: readonly PaymentModeShare[] | null;
  readonly topProducts: readonly RankedSales[] | null;
  readonly topCategories: readonly RankedSales[] | null;
  readonly receivables: readonly ReceivableAge[] | null;
}

export const fetchDashboardCharts = (): Promise<DashboardCharts> =>
  request<DashboardCharts>('/dashboards/charts');

/** The translation key naming each sales payment mode, by the server's number for it. */
export const PAYMENT_MODE_KEYS: Readonly<Record<number, string>> = {
  1: 'paymentModes.credit',
  2: 'paymentModes.cash',
  3: 'paymentModes.online',
  4: 'paymentModes.card',
};

/** The step between clean ticks: 1, 2, 2.5 or 5 of some power of ten. */
export function niceStep(span: number, count: number): number {
  if (!Number.isFinite(span) || span <= 0) return 1;

  const raw = span / Math.max(1, count);
  const power = 10 ** Math.floor(Math.log10(raw));
  const fraction = raw / power;
  const nice =
    fraction <= 1
      ? 1
      : fraction <= 2
        ? 2
        : fraction <= 2.5
          ? 2.5
          : fraction <= 5
            ? 5
            : 10;

  return nice * power;
}

/**
 * Clean ticks spanning the values, with zero always among them.
 *
 * A bar grows from zero, so an axis that started anywhere else would draw a small
 * difference as a large one. Values all at zero still get a scale to draw on - a
 * quiet month is a fact, and the chart should show it as one rather than fail.
 */
export function niceTicks(values: readonly number[], count = 4): readonly number[] {
  const finite = values.filter((value) => Number.isFinite(value));
  const low = Math.min(0, ...finite);
  const high = Math.max(0, ...finite);

  if (low === 0 && high === 0) return [0, 1];

  const step = niceStep(high - low, count);
  const first = Math.floor(low / step + 1e-9);
  const last = Math.ceil(high / step - 1e-9);
  const ticks: number[] = [];

  // Counted in whole steps and rounded, so 0.1 + 0.2 never puts 0.30000000000000004
  // on an axis.
  for (let index = first; index <= last; index++) {
    ticks.push(Number((index * step).toPrecision(12)));
  }

  return ticks;
}

/**
 * How an axis writes its figures: in full under ten thousand, compact on an axis
 * that reaches it - 0 / 10K / 20K - and one style for every tick on it.
 */
export function axisFormatter(ticks: readonly number[]): (value: number) => string {
  const reach = Math.max(0, ...ticks.map((tick) => Math.abs(tick)));
  const step = ticks.length > 1 ? Math.abs((ticks[1] ?? 0) - (ticks[0] ?? 0)) : 1;
  const format = new Intl.NumberFormat(
    undefined,
    reach >= 10_000
      ? { notation: 'compact', maximumFractionDigits: 1 }
      : { maximumFractionDigits: step < 1 ? 2 : 0 },
  );

  return (value) => format.format(value);
}

/**
 * Each part's share of the whole, or null where a share would mean nothing: a part
 * that came out negative - more refunded that way than taken - or a whole that is
 * not above zero.
 */
export function sharesOf(amounts: readonly number[]): readonly (number | null)[] {
  const whole = amounts.reduce((sum, amount) => sum + amount, 0);

  return amounts.map((amount) => (whole > 0 && amount >= 0 ? amount / whole : null));
}

/** A `yyyy-MM-dd` or `yyyy-MM` key as a date, at midnight UTC so no time zone moves it. */
export function keyDate(key: string): Date {
  const [year = 1970, month = 1, day = 1] = key.split('-').map(Number);

  return new Date(Date.UTC(year, month - 1, day));
}

/**
 * Formats a key as a date in the reader's language, in UTC like the key itself.
 *
 * Month names in the reader's language but Western digits whatever it is, because
 * every amount beside them is written with them: "3 نوفمبر" over a column of
 * 1,250.00 rather than "٣ نوفمبر".
 */
export function formatKey(
  key: string,
  language: string,
  options: Intl.DateTimeFormatOptions,
): string {
  return new Intl.DateTimeFormat(`${language}-u-nu-latn`, {
    ...options,
    timeZone: 'UTC',
  }).format(keyDate(key));
}

/** Whether every figure is zero: a chart with nothing to draw says so instead. */
export function allZero(values: readonly (number | null)[]): boolean {
  return values.every((value) => value === null || value === 0);
}
