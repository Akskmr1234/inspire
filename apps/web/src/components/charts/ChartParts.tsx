import { useId, useLayoutEffect, useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import clsx from 'clsx';

/**
 * The pieces every chart on the dashboard is made of: the card it sits in, with its
 * table view; its legend; its tooltip; and the shapes and sizes they share.
 *
 * Drawn by hand in SVG rather than with a charting library. Six charts do not earn a
 * dependency, and drawing them here keeps every mark on the app's own tokens - the
 * same surfaces, lines and ink as the rest of the screen, in both themes.
 */

/** How thick a bar or column is drawn at most. A band's leftover width is air. */
export const MAX_BAR = 24;

/** The surface-coloured gap between touching bars. */
export const BAR_GAP = 2;

/** The rounding on a bar's data end. Its baseline end stays square. */
export const BAR_RADIUS = 4;

/**
 * An element's width, kept up to date as it resizes.
 *
 * A chart is drawn at its real pixel width rather than scaled from a fixed one:
 * scaling a viewBox scales its text with it, and an axis set in eleven-pixel type
 * would read at six on a phone.
 */
export function useWidth<T extends HTMLElement>(
  fallback = 560,
): readonly [React.RefObject<T | null>, number] {
  const ref = useRef<T>(null);
  const [width, setWidth] = useState(fallback);

  useLayoutEffect(() => {
    const element = ref.current;
    if (!element) return undefined;

    const measure = (): void => setWidth(element.clientWidth || fallback);
    measure();

    if (typeof ResizeObserver === 'undefined') return undefined;

    const observer = new ResizeObserver(measure);
    observer.observe(element);
    return () => observer.disconnect();
  }, [fallback]);

  return [ref, width] as const;
}

/**
 * The outline of a bar growing from a baseline: its data end rounded, its baseline
 * end square, whichever way it grows. Empty for a bar too short to draw.
 */
export function barPath(
  x: number,
  width: number,
  base: number,
  end: number,
  radius = BAR_RADIUS,
): string {
  const length = Math.abs(end - base);
  if (length < 0.5 || width <= 0) return '';

  const r = Math.min(radius, width / 2, length);
  const right = x + width;

  // Upward, the rounded corners are at the top; downward - a loss below the line -
  // they are at the bottom, which is where that bar's figure is.
  return end < base
    ? `M${x},${base}V${end + r}A${r},${r} 0 0 1 ${x + r},${end}H${right - r}A${r},${r} 0 0 1 ${right},${end + r}V${base}Z`
    : `M${x},${base}V${end - r}A${r},${r} 0 0 0 ${x + r},${end}H${right - r}A${r},${r} 0 0 0 ${right},${end - r}V${base}Z`;
}

/** One series in a legend: a swatch shaped like its mark, and its name. */
export interface LegendItem {
  readonly name: string;
  /** The fill, as a CSS colour. */
  readonly color: string;
  readonly shape?: 'rect' | 'line';
}

export function Legend({
  items,
}: {
  readonly items: readonly LegendItem[];
}): React.JSX.Element {
  return (
    <ul className="flex flex-wrap items-center gap-x-4 gap-y-1 text-xs text-ink-muted">
      {items.map((item) => (
        <li key={item.name} className="flex items-center gap-1.5">
          <span
            aria-hidden="true"
            className={clsx(
              'shrink-0',
              item.shape === 'line'
                ? 'h-0.5 w-3.5 rounded-full'
                : 'size-2.5 rounded-[3px]',
            )}
            style={{ backgroundColor: item.color }}
          />
          {item.name}
        </li>
      ))}
    </ul>
  );
}

/**
 * A chart's card: its title, what its figures are, an optional headline figure and
 * legend, and the switch to its table - the same figures for anyone who cannot or
 * would rather not read them off a picture.
 */
export function ChartCard({
  title,
  subtitle,
  headline,
  legend,
  table,
  className,
  children,
}: {
  readonly title: string;
  readonly subtitle?: string;
  readonly headline?: React.ReactNode;
  readonly legend?: readonly LegendItem[];
  readonly table?: React.ReactNode;
  readonly className?: string;
  readonly children: React.ReactNode;
}): React.JSX.Element {
  const { t } = useTranslation();
  const [asTable, setAsTable] = useState(false);
  const titleId = useId();

  return (
    <figure
      aria-labelledby={titleId}
      className={clsx('card card-body flex min-w-0 flex-col gap-3', className)}
    >
      <div className="flex items-start justify-between gap-3">
        <div className="min-w-0">
          <h3 id={titleId} className="text-sm font-semibold text-ink">
            {title}
          </h3>
          {subtitle && <p className="mt-0.5 text-xs text-ink-muted">{subtitle}</p>}
        </div>
        {table !== undefined && (
          <button
            type="button"
            aria-pressed={asTable}
            onClick={() => setAsTable((shown) => !shown)}
            className="btn-ghost btn-xs shrink-0"
          >
            {asTable ? t('charts.showChart') : t('charts.showTable')}
          </button>
        )}
      </div>
      {headline}
      {/* A single series needs no legend: the title already says what it is. */}
      {legend && legend.length > 1 && !asTable && <Legend items={legend} />}
      <div className="min-w-0">{asTable ? table : children}</div>
    </figure>
  );
}

/** A chart's figures as a table: its first column names each row, the rest are figures. */
export function ChartTable({
  head,
  rows,
}: {
  readonly head: readonly string[];
  readonly rows: readonly (readonly string[])[];
}): React.JSX.Element {
  return (
    <div className="max-h-72 overflow-auto">
      <table className="w-full text-sm">
        <thead className="sticky top-0 bg-surface">
          <tr>
            {head.map((heading, index) => (
              <th
                key={heading}
                scope="col"
                className={clsx(
                  'border-b border-line py-1.5 text-xs font-semibold text-ink-muted',
                  index === 0 ? 'text-start' : 'ps-3 text-end',
                )}
              >
                {heading}
              </th>
            ))}
          </tr>
        </thead>
        <tbody className="divide-y divide-line">
          {rows.map((row, rowIndex) => (
            <tr key={`${row[0] ?? ''}-${rowIndex}`}>
              {row.map((cell, index) =>
                index === 0 ? (
                  <th
                    key={index}
                    scope="row"
                    className="py-1.5 text-start font-normal text-ink"
                  >
                    {cell}
                  </th>
                ) : (
                  <td key={index} className="py-1.5 ps-3 text-end tabular-nums text-ink">
                    {cell}
                  </td>
                ),
              )}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

/** One line of a tooltip: the figure first and strong, then what it is. */
export interface TooltipRow {
  readonly key: string;
  readonly value: string;
  readonly name?: string;
  /** The series colour, drawn as a short line beside the figure. */
  readonly color?: string;
}

/**
 * The readout for the mark under the pointer or the keyboard, set beside it - to its
 * right on the left half of the chart, to its left on the right half - and kept
 * inside the chart, so on a phone it never runs off the card.
 */
export function ChartTooltip({
  x,
  width,
  top,
  title,
  rows,
}: {
  readonly x: number;
  readonly width: number;
  readonly top: number;
  readonly title: string;
  readonly rows: readonly TooltipRow[];
}): React.JSX.Element {
  const box = useRef<HTMLDivElement>(null);
  const [own, setOwn] = useState(140);

  // Measured once drawn, since its width is its text's: an estimate places it on the
  // first frame and the measurement corrects it before the browser paints.
  useLayoutEffect(() => {
    if (box.current) setOwn(box.current.offsetWidth);
  }, [title, rows]);

  const beside = x < width / 2 ? x + 12 : x - 12 - own;
  const left = Math.max(0, Math.min(beside, width - own));

  return (
    <div
      ref={box}
      role="status"
      className="pointer-events-none absolute z-10 min-w-28 rounded-lg border border-line bg-surface px-3 py-2 text-xs shadow-raised"
      style={{ left, top }}
    >
      <p className="whitespace-nowrap text-ink-muted">{title}</p>
      {rows.map((row) => (
        <p key={row.key} className="mt-1 flex items-center gap-2 whitespace-nowrap">
          {row.color && (
            <span
              aria-hidden="true"
              className="h-0.5 w-3 shrink-0 rounded-full"
              style={{ backgroundColor: row.color }}
            />
          )}
          <span className="font-semibold tabular-nums text-ink">{row.value}</span>
          {row.name && <span className="text-ink-muted">{row.name}</span>}
        </p>
      ))}
    </div>
  );
}

/** The width an axis needs for its widest figure, at the eleven-pixel type it is set in. */
export function axisWidth(labels: readonly string[]): number {
  return Math.ceil(Math.max(1, ...labels.map((label) => label.length)) * 6.4) + 10;
}

/**
 * Steps the active mark with the left and right arrows, Home and End; Escape lets go
 * of it. Up and down are left to the page, so a reader tabbing through can still
 * scroll past the chart.
 */
export function stepWithKeys(
  event: React.KeyboardEvent,
  active: number | null,
  count: number,
  setActive: (index: number | null) => void,
): void {
  if (count === 0) return;

  if (event.key === 'Escape') {
    setActive(null);
    return;
  }

  const current = active ?? count - 1;
  const next =
    event.key === 'ArrowRight'
      ? Math.min(count - 1, current + 1)
      : event.key === 'ArrowLeft'
        ? Math.max(0, current - 1)
        : event.key === 'Home'
          ? 0
          : event.key === 'End'
            ? count - 1
            : undefined;

  if (next !== undefined) {
    event.preventDefault();
    setActive(next);
  }
}
