import { useState } from 'react';
import { axisFormatter, niceTicks } from '@/lib/dashboardCharts';
import {
  BAR_GAP,
  ChartTooltip,
  MAX_BAR,
  axisWidth,
  barPath,
  stepWithKeys,
  useWidth,
} from '@/components/charts/ChartParts';

/** One series of columns. */
export interface ColumnSeries {
  readonly key: string;
  readonly name: string;
  /** The fill, as a CSS colour: one of the chart tokens. */
  readonly color: string;
  /** One figure per column; null where this series has nothing to say. */
  readonly values: readonly (number | null)[];
}

/** The figures under the axis labels: a little more room for a second line. */
const LABEL_BAND = 18;
const SUBLABEL_BAND = 13;

/**
 * Columns along a category axis - months, days, buckets - one series or two side by
 * side, growing up from zero or, for a loss, down from it.
 *
 * Every column's band is its hover and focus target, not just its painted pixels, and
 * the readout lists every series in that band. The figures are also in the card's
 * table view, so the tooltip never gates a value.
 */
export function ColumnChart({
  labels,
  sublabels,
  titles,
  series,
  fillOf,
  nameOf,
  format,
  height = 200,
  label,
}: {
  /** What each column is called on the axis. */
  readonly labels: readonly string[];
  /**
   * A quieter second line for each column - the year of each month - written under
   * the first label drawn and wherever it changes.
   */
  readonly sublabels?: readonly (string | null)[];
  /** What each column is called in its readout. */
  readonly titles: readonly string[];
  readonly series: readonly ColumnSeries[];
  /** A column's fill where it depends on its figure, as a loss's does. */
  readonly fillOf?: (value: number, series: ColumnSeries) => string;
  /** What a column is called in its readout where that depends on its figure: a loss. */
  readonly nameOf?: (value: number, series: ColumnSeries) => string;
  readonly format: (value: number) => string;
  /** The whole chart's height, axis labels included, so the card never scrolls inside. */
  readonly height?: number;
  /** The chart's accessible name. */
  readonly label: string;
}): React.JSX.Element {
  const [ref, width] = useWidth<HTMLDivElement>();
  const [active, setActive] = useState<number | null>(null);

  const count = labels.length;
  const values = series.flatMap((one) =>
    one.values.filter((value): value is number => value !== null),
  );
  const ticks = niceTicks(values, height < 160 ? 2 : 4);
  const tickText = axisFormatter(ticks);
  const tickLabels = ticks.map(tickText);

  const top = 8;
  const left = axisWidth(tickLabels);
  const right = 4;
  const bottom = LABEL_BAND + (sublabels?.some(Boolean) ? SUBLABEL_BAND : 0);
  const plotWidth = Math.max(1, width - left - right);
  const plotHeight = Math.max(1, height - top - bottom);

  const low = ticks[0] ?? 0;
  const high = ticks[ticks.length - 1] ?? 1;
  const y = (value: number): number =>
    top + ((high - value) / (high - low || 1)) * plotHeight;
  const base = y(0);

  const band = plotWidth / Math.max(1, count);
  const drawn = series.length;
  const barWidth = Math.max(
    2,
    Math.min(MAX_BAR, (band * 0.72 - BAR_GAP * (drawn - 1)) / Math.max(1, drawn)),
  );
  const groupWidth = barWidth * drawn + BAR_GAP * (drawn - 1);
  const center = (index: number): number => left + band * index + band / 2;

  // Every label where they fit, otherwise every second or third - counted back from
  // the latest, so the column the reader cares most about is always named.
  const every = Math.max(1, Math.ceil(34 / band));
  const named = (index: number): boolean => (count - 1 - index) % every === 0;

  // The quieter line - a year - under the first label drawn and wherever it changes
  // from the one before, so the turn of a year is marked even when January's own
  // label is one of those skipped.
  const marks: (string | null)[] = [];
  let previous: string | null = null;

  for (let index = 0; index < count; index++) {
    const mark = named(index) ? (sublabels?.[index] ?? null) : null;
    marks.push(mark !== null && mark !== previous ? mark : null);
    if (mark !== null) previous = mark;
  }

  return (
    <div
      ref={ref}
      role="group"
      aria-label={label}
      tabIndex={0}
      onKeyDown={(event) => stepWithKeys(event, active, count, setActive)}
      onBlur={() => setActive(null)}
      onPointerLeave={() => setActive(null)}
      className="relative rounded-md outline-none focus-visible:ring-2 focus-visible:ring-brand-500/60"
    >
      <svg
        width={width}
        height={height}
        direction="ltr"
        aria-hidden="true"
        className="block"
      >
        {ticks.map((tick, index) => (
          <g key={tick}>
            <line
              x1={left}
              x2={width - right}
              y1={y(tick)}
              y2={y(tick)}
              className={tick === 0 ? 'stroke-line-strong' : 'stroke-line'}
              strokeWidth={1}
              shapeRendering="crispEdges"
            />
            <text
              x={left - 6}
              y={y(tick)}
              textAnchor="end"
              dominantBaseline="middle"
              className="fill-ink-subtle text-[11px] tabular-nums"
            >
              {tickLabels[index]}
            </text>
          </g>
        ))}

        {active !== null && (
          <rect
            x={left + band * active + 1}
            y={top}
            width={Math.max(0, band - 2)}
            height={plotHeight}
            rx={4}
            className="fill-surface-3"
            opacity={0.7}
          />
        )}

        {labels.map((name, index) => {
          const groupStart = center(index) - groupWidth / 2;

          return (
            <g key={`${name}-${index}`}>
              {series.map((one, place) => {
                const value = one.values[index];
                if (value === null || value === undefined) return null;

                const path = barPath(
                  groupStart + place * (barWidth + BAR_GAP),
                  barWidth,
                  base,
                  y(value),
                );
                if (!path) return null;

                return (
                  <path
                    key={one.key}
                    d={path}
                    className="animate-grow-bar"
                    style={{
                      fill: fillOf ? fillOf(value, one) : one.color,
                      transformBox: 'fill-box',
                      transformOrigin: value < 0 ? 'top' : 'bottom',
                      animationDelay: `${Math.min(index * 35, 420)}ms`,
                    }}
                  />
                );
              })}
              {named(index) && (
                <text
                  x={center(index)}
                  y={top + plotHeight + 13}
                  textAnchor="middle"
                  className="fill-ink-subtle text-[11px]"
                >
                  {name}
                </text>
              )}
              {marks[index] && (
                <text
                  x={center(index)}
                  y={top + plotHeight + 13 + SUBLABEL_BAND}
                  textAnchor="middle"
                  className="fill-ink-subtle text-[10px]"
                  opacity={0.8}
                >
                  {marks[index]}
                </text>
              )}
            </g>
          );
        })}

        {/* The hit targets: each column's whole band, over everything else. */}
        {labels.map((name, index) => (
          <rect
            key={`hit-${name}-${index}`}
            x={left + band * index}
            y={top}
            width={band}
            height={plotHeight + LABEL_BAND}
            fill="transparent"
            onPointerEnter={() => setActive(index)}
            onPointerMove={() => setActive(index)}
          />
        ))}
      </svg>

      {active !== null && (
        <ChartTooltip
          x={center(active)}
          width={width}
          top={top}
          title={titles[active] ?? labels[active] ?? ''}
          rows={series.flatMap((one) => {
            const value = one.values[active];
            return value === null || value === undefined
              ? []
              : [
                  {
                    key: one.key,
                    value: format(value),
                    name: nameOf ? nameOf(value, one) : one.name,
                    color: fillOf ? fillOf(value, one) : one.color,
                  },
                ];
          })}
        />
      )}
    </div>
  );
}
