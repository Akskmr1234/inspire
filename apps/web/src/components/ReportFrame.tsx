import type { UseQueryResult } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import clsx from 'clsx';
import type { ApiError } from '@/lib/api';

/**
 * The chrome every report screen shares: a heading, the date controls, and the
 * loading, error, and empty states.
 *
 * Extracted after the second report rather than the first. The three primary
 * statements differ only in their body, and repeating the error and empty
 * handling per screen is how one of them ends up silently rendering a blank page
 * on failure.
 */
export function ReportFrame<TData>({
  title,
  subtitle,
  controls,
  query,
  isEmpty,
  children,
}: {
  readonly title: string;
  /** A line under the heading, for a period or a scope the title cannot carry. */
  readonly subtitle?: string;
  readonly controls: React.ReactNode;
  readonly query: UseQueryResult<TData, ApiError>;
  readonly isEmpty?: (data: TData) => boolean;
  readonly children: (data: TData) => React.ReactNode;
}): React.JSX.Element {
  const { t } = useTranslation();

  return (
    <section className="page">
      {/*
        Title on its own line, filters in a bar beneath it.

        They used to share a row with the filters pinned to the opposite edge, which
        on a wide screen left the better part of a thousand pixels of nothing between
        a heading and the controls belonging to it — they read as two unrelated
        things. A filter bar is also what every screen here actually is: a period, a
        couple of pickers, and a Run.
      */}
      <header className="page-header">
        <div className="min-w-0">
          <h1 className="page-title">{title}</h1>
          {subtitle && <p className="page-subtitle mt-0.5">{subtitle}</p>}
        </div>

        {/*
          Printing is what half these screens are for, so it gets a control rather
          than being left to whoever remembers the keyboard shortcut. The stylesheet
          does the work; this only opens the dialog.
        */}
        <button
          type="button"
          onClick={() => window.print()}
          className="btn-icon shrink-0"
          title={t('reports.print')}
          aria-label={t('reports.print')}
        >
          <svg
            viewBox="0 0 24 24"
            fill="none"
            stroke="currentColor"
            strokeWidth={1.75}
            strokeLinecap="round"
            strokeLinejoin="round"
            className="size-[18px]"
            aria-hidden="true"
          >
            <path d="M6 9V3h12v6" />
            <path d="M6 18H4a2 2 0 0 1-2-2v-4a2 2 0 0 1 2-2h16a2 2 0 0 1 2 2v4a2 2 0 0 1-2 2h-2" />
            <path d="M6 14h12v7H6z" />
          </svg>
        </button>
      </header>

      {/*
        Rendered only where a screen actually has filters — an empty bar is a line of
        furniture saying nothing. The controls scroll on their own below `sm` rather
        than wrapping into a four-line stack that pushes the report off the screen.
      */}
      {controls && (
        <div className="filter-bar">
          <div className="-mx-3 min-w-0 overflow-x-auto px-3 pb-1 sm:mx-0 sm:overflow-visible sm:px-0 sm:pb-0">
            {controls}
          </div>
        </div>
      )}

      {/*
        A refetch of a report that is already on screen shows a hairline rather than
        replacing the figures with a spinner. Blanking a statement somebody is
        reading in order to say "loading" costs them their place.
      */}
      {query.isFetching && !query.isPending && <div className="progress-sweep" />}

      {query.isError && (
        <div role="alert" className="alert-error">
          <p className="font-semibold">{query.error.code}</p>
          <p className="mt-0.5 opacity-90">{query.error.detail}</p>
          <button
            type="button"
            onClick={() => void query.refetch()}
            className="mt-2 rounded-md px-2 py-1 text-sm font-semibold underline underline-offset-2 transition hover:bg-red-500/10"
          >
            {t('common.retry')}
          </button>
        </div>
      )}

      {query.isPending && <ReportSkeleton />}

      {query.data !== undefined &&
        (isEmpty?.(query.data) === true ? (
          <EmptyState message={t('reports.noData')} />
        ) : (
          children(query.data)
        ))}
    </section>
  );
}

/**
 * What a report looks like before it arrives.
 *
 * Shaped like the table that is coming rather than a generic spinner, so the screen
 * settles into place instead of jumping when the figures land.
 */
export function ReportSkeleton({
  rows = 6,
}: {
  readonly rows?: number;
}): React.JSX.Element {
  return (
    <div className="card overflow-hidden" aria-hidden="true">
      <div className="flex gap-4 border-b border-line bg-surface-3 px-4 py-3">
        {Array.from({ length: 4 }, (_, index) => (
          <span key={index} className="skeleton h-3 flex-1 rounded" />
        ))}
      </div>
      <div className="divide-y divide-line">
        {Array.from({ length: rows }, (_, row) => (
          <div key={row} className="flex gap-4 px-4 py-3.5">
            {Array.from({ length: 4 }, (_, column) => (
              <span
                key={column}
                className="skeleton h-3 flex-1 rounded"
                // Varied widths, because four identical bars per row reads as a
                // loading graphic and staggered ones read as text that has not
                // arrived yet.
                style={{
                  maxWidth: `${60 + ((row * 7 + column * 23) % 40)}%`,
                  animationDelay: `${(row * 4 + column) * 45}ms`,
                }}
              />
            ))}
          </div>
        ))}
      </div>
    </div>
  );
}

/** Says plainly that a query succeeded and matched nothing. */
export function EmptyState({
  message,
  hint,
}: {
  readonly message: string;
  readonly hint?: string;
}): React.JSX.Element {
  return (
    <div className="card animate-pop flex flex-col items-center gap-2 px-6 py-14 text-center">
      <div className="grid size-11 place-items-center rounded-full bg-surface-3 text-ink-subtle">
        <svg
          viewBox="0 0 24 24"
          fill="none"
          stroke="currentColor"
          strokeWidth={1.6}
          strokeLinecap="round"
          strokeLinejoin="round"
          className="size-5"
          aria-hidden="true"
        >
          <circle cx="11" cy="11" r="7" />
          <path d="m20 20-3.6-3.6" />
        </svg>
      </div>
      <p className="text-sm font-medium text-ink">{message}</p>
      {hint && <p className="max-w-sm text-xs text-ink-muted">{hint}</p>}
    </div>
  );
}

/** A from/to date range control that only applies on submit. */
export function DateRangeControls({
  from,
  to,
  onFromChange,
  onToChange,
  onApply,
  busy,
}: {
  readonly from: string;
  readonly to: string;
  readonly onFromChange: (value: string) => void;
  readonly onToChange: (value: string) => void;
  readonly onApply: () => void;
  readonly busy: boolean;
}): React.JSX.Element {
  const { t } = useTranslation();

  return (
    <form
      className="toolbar"
      onSubmit={(event) => {
        event.preventDefault();
        onApply();
      }}
    >
      <div className="field">
        <label htmlFor="from" className="field-label">
          {t('reports.from')}
        </label>
        <input
          id="from"
          type="date"
          className="field-input"
          value={from}
          onChange={(e) => onFromChange(e.target.value)}
        />
      </div>
      <div className="field">
        <label htmlFor="to" className="field-label">
          {t('reports.to')}
        </label>
        <input
          id="to"
          type="date"
          className="field-input"
          value={to}
          onChange={(e) => onToChange(e.target.value)}
        />
      </div>
      <button type="submit" disabled={busy} className="btn-primary">
        {busy && <Spinner />}
        {busy ? t('reports.running') : t('reports.run')}
      </button>
    </form>
  );
}

/** A single as-at date control. */
export function AsAtControls({
  asAt,
  onChange,
  onApply,
  busy,
}: {
  readonly asAt: string;
  readonly onChange: (value: string) => void;
  readonly onApply: () => void;
  readonly busy: boolean;
}): React.JSX.Element {
  const { t } = useTranslation();

  return (
    <form
      className="toolbar"
      onSubmit={(event) => {
        event.preventDefault();
        onApply();
      }}
    >
      <div className="field">
        <label htmlFor="asAt" className="field-label">
          {t('reports.asAt')}
        </label>
        <input
          id="asAt"
          type="date"
          className="field-input"
          value={asAt}
          onChange={(e) => onChange(e.target.value)}
        />
      </div>
      <button type="submit" disabled={busy} className="btn-primary">
        {busy && <Spinner />}
        {busy ? t('reports.running') : t('reports.run')}
      </button>
    </form>
  );
}

/** The ring that turns inside a button while its action is in flight. */
export function Spinner({
  className,
}: {
  readonly className?: string;
}): React.JSX.Element {
  return (
    <svg
      viewBox="0 0 24 24"
      className={clsx('size-4 shrink-0 animate-spin', className)}
      aria-hidden="true"
    >
      <circle
        cx="12"
        cy="12"
        r="9"
        fill="none"
        stroke="currentColor"
        strokeWidth="2.5"
        opacity="0.25"
      />
      <path
        d="M21 12a9 9 0 0 0-9-9"
        fill="none"
        stroke="currentColor"
        strokeWidth="2.5"
        strokeLinecap="round"
      />
    </svg>
  );
}

/**
 * The balance indicator.
 *
 * Coloured and prominent because a statement that does not balance means the
 * books are broken. Rendering the figures without saying so leaves a reader to
 * compare two totals and hope they notice.
 */
export function BalanceBadge({
  isBalanced,
  currency,
}: {
  readonly isBalanced: boolean;
  readonly currency: string;
}): React.JSX.Element {
  const { t } = useTranslation();

  return (
    <p
      className={clsx(
        'inline-flex animate-pop items-center gap-2 rounded-lg px-3 py-1.5 text-sm font-medium',
        isBalanced
          ? 'bg-emerald-50 text-emerald-800 dark:bg-emerald-500/12 dark:text-emerald-200'
          : 'bg-red-50 text-red-800 dark:bg-red-500/12 dark:text-red-200',
      )}
    >
      {/*
        A dot as well as the colour. Roughly one man in twelve cannot tell this
        green from this red, and "the books balance" is not a fact to encode in hue
        alone — so it pulses when it is wrong, and the words say which it is.
      */}
      <span
        aria-hidden="true"
        className={clsx(
          'size-2 rounded-full',
          isBalanced ? 'bg-emerald-500' : 'animate-breathe bg-red-500',
        )}
      />
      {isBalanced ? `${t('reports.balanced')} · ${currency}` : t('reports.notBalanced')}
    </p>
  );
}

/** Formats a figure for a financial column, blanking zero so the eye follows the numbers. */
export function money(value: number): string {
  return value === 0
    ? ''
    : value.toLocaleString(undefined, {
        minimumFractionDigits: 2,
        maximumFractionDigits: 2,
      });
}

/** Formats a figure that must always show, including zero. */
export function moneyAlways(value: number): string {
  return value.toLocaleString(undefined, {
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  });
}
