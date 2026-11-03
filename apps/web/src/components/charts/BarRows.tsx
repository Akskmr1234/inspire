import clsx from 'clsx';

/** One row: what it is, its figure, and the bar that draws it. */
export interface BarRow {
  readonly key: string;
  readonly label: string;
  /** Quieter text after the label: a product's code. */
  readonly detail?: string | null;
  /**
   * How long the bar is. Against the longest row, or - for a share - already the
   * fraction of the whole, from 0 to 1.
   */
  readonly value: number;
  /** The figure as it is written. */
  readonly figure: string;
  /** Quieter text before the figure: a share, a quantity, a count of bills. */
  readonly note?: string | null;
  /** The bar's fill where it is not the chart's own colour: an age band's step. */
  readonly color?: string;
}

/**
 * Horizontal bars, one per row, each with its name and figure written above it.
 *
 * For rankings and splits, where the reader wants the names and the numbers as much
 * as the shape: every figure is printed, so nothing here hides behind a hover. As
 * bars they grow from the start edge - the right, in Arabic - with the data end
 * rounded and the baseline square; as shares they are meters, filled to their part
 * of a track in a lighter step of the same hue.
 */
export function BarRows({
  rows,
  share = false,
  color = 'var(--app-chart-1)',
  ordered = false,
}: {
  readonly rows: readonly BarRow[];
  /** Draw each row as its share of a whole, on a track, rather than against the longest. */
  readonly share?: boolean;
  readonly color?: string;
  /** A ranking, read in order: a numbered list to assistive technology. */
  readonly ordered?: boolean;
}): React.JSX.Element {
  const longest = Math.max(0, ...rows.map((row) => row.value));
  const List = ordered ? 'ol' : 'ul';

  return (
    <List className="space-y-3">
      {rows.map((row, index) => {
        const fraction = share
          ? Math.min(1, Math.max(0, row.value))
          : longest > 0
            ? Math.max(0, row.value) / longest
            : 0;

        return (
          <li key={row.key} className="min-w-0">
            {/*
              The name takes its own direction, so a product named in English keeps
              its beginning and loses its end to the ellipsis on an Arabic screen. The
              note and the figure are each isolated: side by side, "53%" and
              "182,000.00" are two numbers, and without the isolation a right-to-left
              line runs their digits together into one.
            */}
            <div className="flex items-baseline justify-between gap-3 text-sm">
              <span dir="auto" className="min-w-0 truncate text-ink">
                {row.label}
                {row.detail && (
                  <>
                    {' '}
                    <bdi className="text-xs text-ink-muted">{row.detail}</bdi>
                  </>
                )}
              </span>
              {/* Spaced by a gap rather than a margin: an isolated "53%" takes its own
                  left-to-right direction, which would put a logical margin on the
                  wrong side of it in Arabic. */}
              <span className="flex shrink-0 items-baseline gap-2 whitespace-nowrap tabular-nums">
                {row.note && <bdi className="text-xs text-ink-muted">{row.note}</bdi>}
                <bdi className="font-medium text-ink">{row.figure}</bdi>
              </span>
            </div>
            <div
              aria-hidden="true"
              className={clsx(
                'mt-1.5 h-2',
                share && 'overflow-hidden rounded-full bg-chart-track',
              )}
            >
              {fraction > 0 && (
                <div
                  className={clsx(
                    'h-2 animate-grow-width ltr:origin-left rtl:origin-right',
                    share ? 'rounded-full' : 'rounded-e-[4px]',
                  )}
                  style={{
                    width: `${Math.max(fraction * 100, 0.75)}%`,
                    backgroundColor: row.color ?? color,
                    animationDelay: `${Math.min(index * 45, 360)}ms`,
                  }}
                />
              )}
            </div>
          </li>
        );
      })}
    </List>
  );
}
