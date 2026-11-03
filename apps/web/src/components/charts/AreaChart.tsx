import { useState } from 'react';
import { axisFormatter, niceTicks } from '@/lib/dashboardCharts';
import {
  ChartTooltip,
  axisWidth,
  stepWithKeys,
  useWidth,
} from '@/components/charts/ChartParts';

/** One point of a trend. */
export interface TrendPoint {
  readonly key: string;
  readonly value: number;
  /** What the point is called on the axis. */
  readonly label: string;
  /** What it is called in its readout. */
  readonly title: string;
  /** A second line in the readout: how many bills made the figure. */
  readonly note?: string;
}

const LABEL_BAND = 18;

/**
 * One series over time: a line with a faint wash under it, and a crosshair that
 * snaps to the nearest point - the reader aims at a date, never at a two-pixel line.
 *
 * Straight segments between the points rather than a smoothed curve: a curve
 * overshoots between a busy day and a quiet one, drawing sales that never happened.
 */
export function AreaChart({
  points,
  format,
  color = 'var(--app-chart-1)',
  height = 200,
  label,
}: {
  readonly points: readonly TrendPoint[];
  readonly format: (value: number) => string;
  readonly color?: string;
  /** The whole chart's height, axis labels included. */
  readonly height?: number;
  /** The chart's accessible name. */
  readonly label: string;
}): React.JSX.Element {
  const [ref, width] = useWidth<HTMLDivElement>();
  const [active, setActive] = useState<number | null>(null);

  const count = points.length;
  const ticks = niceTicks(
    points.map((point) => point.value),
    4,
  );
  const tickText = axisFormatter(ticks);
  const tickLabels = ticks.map(tickText);

  // Room at either end for the end marker and its ring, so neither is clipped.
  const top = 10;
  const left = axisWidth(tickLabels);
  const right = 8;
  const plotWidth = Math.max(1, width - left - right);
  const plotHeight = Math.max(1, height - top - LABEL_BAND);

  const low = ticks[0] ?? 0;
  const high = ticks[ticks.length - 1] ?? 1;
  const y = (value: number): number =>
    top + ((high - value) / (high - low || 1)) * plotHeight;
  const step = count > 1 ? plotWidth / (count - 1) : 0;
  const x = (index: number): number =>
    count > 1 ? left + index * step : left + plotWidth / 2;
  const base = y(Math.max(low, Math.min(0, high)));

  const line = points
    .map((point, index) => `${index === 0 ? 'M' : 'L'}${x(index)},${y(point.value)}`)
    .join('');
  const area = count > 0 ? `${line}L${x(count - 1)},${base}L${x(0)},${base}Z` : '';

  // A label a week apart, counted back from the latest day so today is always named.
  const every = Math.max(1, Math.ceil(56 / Math.max(1, step)));
  const shown = active === null ? null : points[active];

  // The target starts half a step before the first point, so each point owns the
  // half-step either side of it and the nearest one is a whole number of steps in.
  const nearest = (event: React.PointerEvent<SVGRectElement>): void => {
    const box = event.currentTarget.getBoundingClientRect();
    const offset = event.clientX - box.left;
    setActive(
      count > 1 ? Math.max(0, Math.min(count - 1, Math.floor(offset / step))) : 0,
    );
  };

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

        {points.map(
          (point, index) =>
            (count - 1 - index) % every === 0 && (
              <text
                key={point.key}
                x={x(index)}
                y={top + plotHeight + 14}
                textAnchor={
                  index === 0 ? 'start' : index === count - 1 ? 'end' : 'middle'
                }
                className="fill-ink-subtle text-[11px]"
              >
                {point.label}
              </text>
            ),
        )}

        <path d={area} style={{ fill: color }} opacity={0.1} />
        <path
          d={line}
          fill="none"
          style={{ stroke: color }}
          strokeWidth={2}
          strokeLinejoin="round"
          strokeLinecap="round"
        />

        {shown && active !== null && (
          <line
            x1={x(active)}
            x2={x(active)}
            y1={top}
            y2={top + plotHeight}
            className="stroke-line-strong"
            strokeWidth={1}
            shapeRendering="crispEdges"
          />
        )}

        {/* The latest day's marker, and the one under the crosshair. */}
        {[...new Set([count - 1, active ?? count - 1])].map((index) => {
          const point = points[index];
          if (!point) return null;

          return (
            <circle
              key={point.key}
              cx={x(index)}
              cy={y(point.value)}
              r={4}
              style={{ fill: color }}
              strokeWidth={2}
              className="stroke-surface"
            />
          );
        })}

        <rect
          x={left - step / 2}
          y={top}
          width={plotWidth + step}
          height={plotHeight + LABEL_BAND}
          fill="transparent"
          onPointerEnter={nearest}
          onPointerMove={nearest}
        />
      </svg>

      {shown && active !== null && (
        <ChartTooltip
          x={x(active)}
          width={width}
          top={top}
          title={shown.title}
          rows={[
            {
              key: 'value',
              value: format(shown.value),
              ...(shown.note ? { name: shown.note } : {}),
            },
          ]}
        />
      )}
    </div>
  );
}
