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
  controls,
  query,
  isEmpty,
  children,
}: {
  readonly title: string;
  readonly controls: React.ReactNode;
  readonly query: UseQueryResult<TData, ApiError>;
  readonly isEmpty?: (data: TData) => boolean;
  readonly children: (data: TData) => React.ReactNode;
}): React.JSX.Element {
  const { t } = useTranslation();

  return (
    <section className="space-y-4">
      <header className="flex flex-wrap items-end justify-between gap-4">
        <h1 className="text-xl font-semibold">{title}</h1>
        {controls}
      </header>

      {query.isError && (
        <div
          role="alert"
          className="rounded-lg border border-red-300 bg-red-50 px-4 py-3 text-sm text-red-800 dark:border-red-800 dark:bg-red-950 dark:text-red-200"
        >
          <p className="font-medium">{query.error.code}</p>
          <p>{query.error.detail}</p>
          <button
            type="button"
            onClick={() => void query.refetch()}
            className="mt-2 underline"
          >
            {t('common.retry')}
          </button>
        </div>
      )}

      {query.isPending && <p className="text-sm text-slate-500">{t('common.loading')}</p>}

      {query.data !== undefined &&
        (isEmpty?.(query.data) === true ? (
          <p className="text-sm text-slate-500">{t('reports.noData')}</p>
        ) : (
          children(query.data)
        ))}
    </section>
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
      className="flex flex-wrap items-end gap-3"
      onSubmit={(event) => {
        event.preventDefault();
        onApply();
      }}
    >
      <div>
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
      <div>
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
      className="flex flex-wrap items-end gap-3"
      onSubmit={(event) => {
        event.preventDefault();
        onApply();
      }}
    >
      <div>
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
        {busy ? t('reports.running') : t('reports.run')}
      </button>
    </form>
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
        'inline-block rounded-lg px-3 py-1.5 text-sm font-medium',
        isBalanced
          ? 'bg-emerald-50 text-emerald-800 dark:bg-emerald-950 dark:text-emerald-200'
          : 'bg-red-100 text-red-900 dark:bg-red-950 dark:text-red-100',
      )}
    >
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
