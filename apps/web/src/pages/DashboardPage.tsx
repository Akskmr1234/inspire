import { useState } from 'react';
import { Link } from 'react-router-dom';
import { useQuery } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import clsx from 'clsx';
import { PageHeading } from '@/components/PageHeading';
import { NeedsAttention } from '@/components/NeedsAttention';
import { EmptyState, moneyAlways } from '@/components/ReportFrame';
import { IconCash, IconChart, IconLayers } from '@/components/icons';
import { DashboardCustomizer } from '@/components/DashboardCustomizer';
import { DashboardCharts } from '@/components/DashboardCharts';
import { ColumnChart } from '@/components/charts/ColumnChart';
import { request, type ApiError } from '@/lib/api';
import { QUANTITY_METRICS, WIDGET_KIND as KIND } from '@/lib/dashboards';
import { formatKey } from '@/lib/dashboardCharts';
import { useSession } from '@/stores/session';

interface DashboardWidget {
  readonly id: string;
  readonly metricCode: string | null;
  /** True when the panel runs a query of somebody's own rather than a named metric. */
  readonly isCustom: boolean;
  readonly title: string;
  readonly titleArabic: string | null;
  readonly kind: number;
  readonly span: number;
}

interface Dashboard {
  readonly id: string;
  readonly code: string;
  readonly name: string;
  readonly nameArabic: string | null;
  readonly widgets: readonly DashboardWidget[];
}

interface MetricPoint {
  readonly label: string;
  readonly value: number;
  /** A line under the label: a bill's date and customer, an item's reorder level. */
  readonly detail?: string | null;
  /** Where the row leads, when it leads anywhere. */
  readonly link?: string | null;
}

interface DashboardMetric {
  /** Keyed per panel: a custom panel has no metric code to key on. */
  readonly widgetId: string;
  readonly metricCode: string | null;
  readonly value: number;
  readonly count: number;
  readonly series: readonly MetricPoint[];
  /** False when the caller may not read this figure — drawn as withheld, not as nil. */
  readonly isPermitted: boolean;
  /** Why this panel could not be drawn, when it could not. */
  readonly error: string | null;
}

interface DashboardData {
  readonly dashboardId: string;
  readonly asAt: string;
  readonly currency: string;
  readonly metrics: readonly DashboardMetric[];
}

/**
 * The dashboard.
 *
 * Every figure here is one the reports already compute, drawn from the same reader —
 * a headline that disagreed with the report behind it would be worse than no headline
 * at all. The panels are data: which figures appear, in what order, and who sees them
 * are rows rather than code.
 */
export function DashboardPage(): React.JSX.Element {
  const { t } = useTranslation();
  const { language, can } = useSession();
  const [customizing, setCustomizing] = useState(false);
  const mayCustomize = can('reporting:dashboard:create');

  const customizer = customizing && (
    <DashboardCustomizer onClose={() => setCustomizing(false)} />
  );

  const customizeButton = mayCustomize && (
    <button
      type="button"
      onClick={() => setCustomizing(true)}
      className="btn-secondary btn-sm ms-auto"
    >
      {t('dashboard.customize')}
    </button>
  );

  const dashboards = useQuery<{ readonly dashboards: readonly Dashboard[] }, ApiError>({
    queryKey: ['dashboards'],
    queryFn: () => request('/dashboards'),
  });

  // Indexed through the optional chain, not after it. `data?.dashboards[0]` stops
  // guarding at `data` and then indexes whatever `dashboards` happens to be — so a
  // response missing the field takes the whole screen down with a TypeError rather
  // than showing the "no dashboard assigned" state written for exactly that case.
  const dashboard = dashboards.data?.dashboards?.[0];

  const data = useQuery<DashboardData, ApiError>({
    queryKey: ['dashboard-data', dashboard?.id],
    queryFn: () => request(`/dashboards/${dashboard!.id}/data`),
    enabled: Boolean(dashboard),
  });

  if (dashboards.isPending) {
    return (
      <div className="grid grid-cols-1 gap-3 sm:grid-cols-2 sm:gap-4 xl:grid-cols-4">
        {Array.from({ length: 8 }, (_, index) => (
          <div key={index} className="card card-body h-32" aria-hidden="true">
            <span className="skeleton block h-2.5 w-2/5 rounded" />
            <span className="skeleton mt-4 block h-7 w-3/5 rounded" />
            <span className="skeleton mt-3 block h-2 w-1/4 rounded" />
          </div>
        ))}
      </div>
    );
  }

  if (!dashboard) {
    return (
      <>
        <div className="flex">{customizeButton}</div>
        <EmptyState message={t('dashboard.none')} />
        {/* The charts are nobody's arrangement, so a reader with no dashboard still has them. */}
        <DashboardCharts />
        {customizer}
      </>
    );
  }

  // Keyed by panel rather than by metric. A custom panel has no metric code, and two
  // panels may draw the same metric differently, so the widget is the only stable key.
  const byWidget = new Map(data.data?.metrics.map((metric) => [metric.widgetId, metric]));
  const currency = data.data?.currency ?? '';

  return (
    <section className="page">
      {/*
        In the application bar with every other screen's, rather than opening the
        page: the tiles are what the dashboard is, and a row spent naming it is a
        row they do not get.
      */}
      <PageHeading
        title={
          language === 'ar' && dashboard.nameArabic
            ? dashboard.nameArabic
            : dashboard.name
        }
        subtitle={data.data ? `${t('reports.asAt')} ${data.data.asAt}` : undefined}
      />

      {data.isError && (
        <div role="alert" className="alert-error">
          {data.error.detail || data.error.code}
        </div>
      )}

      {/* Quick Navigation Action Chips */}
      <div className="flex flex-wrap items-center gap-2">
        <Link
          to="/accounting/journals/new"
          className="inline-flex items-center gap-1.5 rounded-lg border border-line bg-surface px-3 py-1.5 text-xs font-semibold text-ink shadow-xs transition hover:border-line-strong hover:bg-surface-3 hover:text-ink active:scale-95"
        >
          <span className="size-2 rounded-full bg-brand-500" />
          {t('voucherList.newJournal')}
        </Link>
        <Link
          to="/accounting/day-book"
          className="inline-flex items-center gap-1.5 rounded-lg border border-line bg-surface px-3 py-1.5 text-xs font-semibold text-ink shadow-xs transition hover:border-line-strong hover:bg-surface-3 hover:text-ink active:scale-95"
        >
          <span className="size-2 rounded-full bg-emerald-500" />
          {t('nav.dayBook')}
        </Link>
        <Link
          to="/sales/invoices"
          className="inline-flex items-center gap-1.5 rounded-lg border border-line bg-surface px-3 py-1.5 text-xs font-semibold text-ink shadow-xs transition hover:border-line-strong hover:bg-surface-3 hover:text-ink active:scale-95"
        >
          <span className="size-2 rounded-full bg-sky-500" />
          {t('nav.sales')}
        </Link>
        <Link
          to="/inventory/valuation"
          className="inline-flex items-center gap-1.5 rounded-lg border border-line bg-surface px-3 py-1.5 text-xs font-semibold text-ink shadow-xs transition hover:border-line-strong hover:bg-surface-3 hover:text-ink active:scale-95"
        >
          <span className="size-2 rounded-full bg-amber-500" />
          {t('nav.stockValuation')}
        </Link>
        {customizeButton}
      </div>

      {dashboard.widgets.length === 0 && (
        <EmptyState message={t('dashboard.noneWidgets')} />
      )}

      {/*
        Four across only at `xl`. A dashboard tile carries a currency figure of up
        to twelve characters plus its unit, and at `lg` on a laptop four of them
        leaves each one too narrow to hold that on a single line.
      */}
      <div className="stagger grid grid-cols-1 gap-3 sm:grid-cols-2 sm:gap-4 xl:grid-cols-4">
        {dashboard.widgets.map((widget) => (
          <Panel
            key={widget.id}
            widget={widget}
            metric={byWidget.get(widget.id)}
            currency={currency}
            loading={data.isPending}
            language={language}
          />
        ))}
      </div>

      {/*
        How trading has gone, under where it stands: the same figures over time, each
        chart drawn from the report it summarises.
      */}
      <DashboardCharts />

      {/*
        What is waiting, under what is true.

        The tiles say where the firm stands; this says what somebody is meant to do
        about it, which is the question the screen is actually opened with. It draws
        nothing at all when there is nothing outstanding — a panel headed "needs
        attention" listing nothing is a section of screen spent saying "you are up
        to date".
      */}
      <NeedsAttention />
      {customizer}
    </section>
  );
}

function Panel({
  widget,
  metric,
  currency,
  loading,
  language,
}: {
  readonly widget: DashboardWidget;
  readonly metric: DashboardMetric | undefined;
  readonly currency: string;
  readonly loading: boolean;
  readonly language: string;
}): React.JSX.Element {
  const { t } = useTranslation();
  const title =
    language === 'ar' && widget.titleArabic ? widget.titleArabic : widget.title;

  // Tailwind needs whole class names at build time, so the span is mapped rather
  // than interpolated — `col-span-${n}` would be purged and silently do nothing.
  // Spans start at `sm`, since below that the grid is a single column and a tile
  // spanning four of one column is just a tile.
  const span =
    {
      1: '',
      2: 'sm:col-span-2',
      3: 'sm:col-span-2 xl:col-span-3',
      4: 'sm:col-span-2 xl:col-span-4',
    }[widget.span] ?? '';

  return (
    <div
      className={clsx(
        // `justify-between` with a floor height: a KPI tile holding one figure and a
        // chart tile holding twelve bars sit in the same row, and without a floor the
        // short one collapses to half the height of its neighbour.
        'card card-body group/panel relative flex min-h-28 flex-col justify-between overflow-hidden',
        span,
      )}
    >
      {/* A brand wash that surfaces under the pointer, so a dense grid of figures
          still acknowledges which one is being read. */}
      <span
        aria-hidden="true"
        className="pointer-events-none absolute inset-0 bg-gradient-to-br from-brand-500/[0.06] to-transparent opacity-0 transition-opacity duration-300 group-hover/panel:opacity-100"
      />

      <div className="relative flex items-center justify-between gap-2">
        <p className="text-xs font-semibold tracking-wide text-ink-muted uppercase">
          {title}
        </p>
        <span className="grid size-7 shrink-0 place-items-center rounded-lg bg-surface-3/80 text-ink-subtle">
          {widget.kind === KIND.Series ? (
            <IconChart className="size-3.5" />
          ) : widget.kind === KIND.Breakdown ? (
            <IconLayers className="size-3.5" />
          ) : (
            <IconCash className="size-3.5" />
          )}
        </span>
      </div>

      <div className="relative">
        {loading ? (
          <div aria-hidden="true">
            <span className="skeleton mt-3 block h-7 w-3/5 rounded" />
            <span className="skeleton mt-2.5 block h-2 w-1/4 rounded" />
          </div>
        ) : !metric ? (
          <p className="mt-3 text-sm text-ink-subtle">—</p>
        ) : metric.error ? (
          // One panel failing must not take the dashboard with it, so the failure is
          // reported here and the rest of the grid still draws.
          <p className="mt-3 text-sm text-red-600 dark:text-red-400">{metric.error}</p>
        ) : !metric.isPermitted ? (
          // Withheld rather than zero. A dashboard reporting nothing owing and one
          // refusing to say are different facts, and drawing them the same way would
          // make the second look like the first.
          <p className="mt-3 text-sm text-ink-subtle italic">{t('dashboard.withheld')}</p>
        ) : widget.kind === KIND.Series ? (
          <Series points={metric.series} title={title} language={language} />
        ) : widget.kind === KIND.Breakdown ? (
          <Breakdown points={metric.series} currency={currency} />
        ) : widget.kind === KIND.List ? (
          <List
            points={metric.series}
            currency={
              metric.metricCode && QUANTITY_METRICS.has(metric.metricCode)
                ? null
                : currency
            }
            total={metric.count}
          />
        ) : (
          <>
            <p className="mt-2 text-2xl font-semibold tracking-tight tabular-nums text-ink">
              {moneyAlways(metric.value)}{' '}
              <span className="text-sm font-normal text-ink-subtle">{currency}</span>
            </p>
            {/*
              The line under the figure keeps its height whether or not it has
              anything to say. The tiles sit in one row and are bottom-aligned, so
              a tile with no count drew its figure a line lower than its
              neighbours — four headline numbers on three different baselines.
            */}
            <p className="mt-1 min-h-4 text-xs text-ink-muted">
              {metric.count > 0 ? t('dashboard.itemCount', { count: metric.count }) : ''}
            </p>
          </>
        )}
      </div>
    </div>
  );
}

/**
 * A series label, short enough for an axis.
 *
 * The monthly series labels its points `2026-07`, and twelve four-digit years side
 * by side at this width is unreadable — so a date keeps its month and everything
 * else is left alone rather than blindly cut at the fifth character.
 */
function shortLabel(label: string): string {
  // A day keeps its day of the month; a month, its month.
  const day = /^\d{4}-\d{2}-(\d{2})$/.exec(label);
  if (day) return String(Number(day[1]));

  const month = /^\d{4}-(\d{2})/.exec(label);

  return month ? (month[1] ?? label) : label;
}

/** A series label on the axis: a month by its name, a day by its date, a name as it is. */
function axisLabel(label: string, language: string): string {
  return /^\d{4}-\d{2}$/.test(label)
    ? formatKey(label, language, { month: 'short' })
    : shortLabel(label);
}

/** The year of a month on the axis, written where it changes; nothing for anything else. */
function axisYear(label: string, language: string): string | null {
  return /^\d{4}-\d{2}$/.test(label)
    ? formatKey(label, language, { year: 'numeric' })
    : null;
}

/**
 * A series label in full, for the readout: `2026-07` as July 2026, a day as its date,
 * and anything else - a name the server sends - as it is.
 */
function longLabel(label: string, language: string): string {
  if (/^\d{4}-\d{2}-\d{2}$/.test(label)) {
    return formatKey(label, language, {
      day: 'numeric',
      month: 'short',
      year: 'numeric',
    });
  }

  return /^\d{4}-\d{2}$/.test(label)
    ? formatKey(label, language, { month: 'long', year: 'numeric' })
    : label;
}

function Series({
  points,
  title,
  language,
}: {
  readonly points: readonly MetricPoint[];
  readonly title: string;
  readonly language: string;
}): React.JSX.Element {
  const { t } = useTranslation();

  if (points.length === 0) {
    return <p className="mt-3 text-sm text-ink-subtle">{t('dashboard.noData')}</p>;
  }

  // Drawn by the same column chart as the charts under the tiles, so a trend reads
  // the same wherever it is: an axis to read it by, a readout under the pointer.
  return (
    <div className="mt-3">
      <ColumnChart
        label={title}
        labels={points.map((point) => axisLabel(point.label, language))}
        sublabels={points.map((point) => axisYear(point.label, language))}
        titles={points.map((point) => longLabel(point.label, language))}
        series={[
          {
            key: 'value',
            name: '',
            color: 'var(--app-chart-1)',
            values: points.map((point) => point.value),
          },
        ]}
        format={(value) => moneyAlways(value)}
        height={136}
      />
    </div>
  );
}

function Breakdown({
  points,
  currency,
}: {
  readonly points: readonly MetricPoint[];
  readonly currency: string;
}): React.JSX.Element {
  const { t } = useTranslation();

  if (points.length === 0) {
    return <p className="mt-3 text-sm text-ink-subtle">{t('dashboard.noData')}</p>;
  }

  // Every row carries a bar behind it, in proportion to the largest. A column of
  // figures tells you the numbers; the bars tell you the shape of them without the
  // reader having to compare digits.
  const peak = Math.max(...points.map((point) => Math.abs(point.value)), 1);

  return (
    <ul className="mt-3 space-y-1.5">
      {points.map((point) => (
        <li
          key={point.label}
          className="group/item relative flex items-baseline justify-between gap-3 overflow-hidden rounded-lg px-2.5 py-1.5 text-sm transition-colors hover:bg-surface-3/60"
        >
          <span
            aria-hidden="true"
            className="absolute inset-y-0 start-0 rounded-lg bg-gradient-to-r from-brand-500/15 to-brand-500/5 transition-all group-hover/item:from-brand-500/25"
            style={{ width: `${(Math.abs(point.value) / peak) * 100}%` }}
          />
          <span className="relative truncate font-medium text-ink">{point.label}</span>
          <span className="relative shrink-0 font-mono tabular-nums font-semibold text-ink">
            {moneyAlways(point.value)}{' '}
            <span className="text-xs font-normal text-ink-subtle">{currency}</span>
          </span>
        </li>
      ))}
    </ul>
  );
}

/**
 * A plain list - the latest bills, the items running low - each row with a line of
 * detail under it and its figure. Money where it is money; a count where it is not.
 */
function List({
  points,
  currency,
  total,
}: {
  readonly points: readonly MetricPoint[];
  readonly currency: string | null;
  readonly total: number;
}): React.JSX.Element {
  const { t } = useTranslation();

  if (points.length === 0) {
    return <p className="mt-3 text-sm text-ink-subtle">{t('dashboard.noData')}</p>;
  }

  return (
    <div className="mt-2">
      <ul className="max-h-80 divide-y divide-line overflow-y-auto">
        {points.map((point, index) => (
          <li
            key={`${point.label}-${index}`}
            className="flex items-baseline justify-between gap-3 py-1.5 text-sm"
          >
            <span className="min-w-0">
              {point.link ? (
                <Link
                  to={point.link}
                  className="block truncate font-medium text-brand-700 hover:underline dark:text-brand-300"
                >
                  {point.label}
                </Link>
              ) : (
                <span className="block truncate font-medium text-ink">{point.label}</span>
              )}
              {point.detail && (
                <span className="block truncate text-xs text-ink-muted">
                  {point.detail}
                </span>
              )}
            </span>
            <span className="shrink-0 font-mono font-semibold tabular-nums text-ink">
              {currency === null
                ? point.value.toLocaleString(undefined, { maximumFractionDigits: 3 })
                : moneyAlways(point.value)}
              {currency && (
                <span className="ms-1 text-xs font-normal text-ink-subtle">
                  {currency}
                </span>
              )}
            </span>
          </li>
        ))}
      </ul>
      {total > points.length && (
        <p className="mt-1 text-xs text-ink-muted">
          {t('dashboard.itemCount', { count: total })}
        </p>
      )}
    </div>
  );
}
