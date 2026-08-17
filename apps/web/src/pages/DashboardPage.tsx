import { useQuery } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import clsx from 'clsx';
import { EmptyState, moneyAlways } from '@/components/ReportFrame';
import { request, type ApiError } from '@/lib/api';
import { useSession } from '@/stores/session';

/** How a panel draws its figure, keyed by the wire value. */
const KIND = { Kpi: 1, Series: 2, Breakdown: 3 } as const;

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
  const { language } = useSession();

  const dashboards = useQuery<{ readonly dashboards: readonly Dashboard[] }, ApiError>({
    queryKey: ['dashboards'],
    queryFn: () => request('/dashboards'),
  });

  const dashboard = dashboards.data?.dashboards[0];

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
    return <EmptyState message={t('dashboard.none')} />;
  }

  // Keyed by panel rather than by metric. A custom panel has no metric code, and two
  // panels may draw the same metric differently, so the widget is the only stable key.
  const byWidget = new Map(data.data?.metrics.map((metric) => [metric.widgetId, metric]));
  const currency = data.data?.currency ?? '';

  return (
    <section className="page">
      <header className="page-header">
        <h1 className="page-title">
          {language === 'ar' && dashboard.nameArabic
            ? dashboard.nameArabic
            : dashboard.name}
        </h1>
        {data.data && (
          <p className="page-subtitle">
            {t('reports.asAt')} {data.data.asAt}
          </p>
        )}
      </header>

      {data.isError && (
        <div role="alert" className="alert-error">
          {data.error.detail || data.error.code}
        </div>
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
    <div className={clsx('card card-body group/panel relative overflow-hidden', span)}>
      {/* A brand wash that surfaces under the pointer, so a dense grid of figures
          still acknowledges which one is being read. */}
      <span
        aria-hidden="true"
        className="pointer-events-none absolute inset-0 bg-gradient-to-br from-brand-500/[0.06] to-transparent opacity-0 transition-opacity duration-300 group-hover/panel:opacity-100"
      />

      <p className="relative text-xs font-medium tracking-wide text-ink-muted uppercase">
        {title}
      </p>

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
          <Series points={metric.series} />
        ) : widget.kind === KIND.Breakdown ? (
          <Breakdown points={metric.series} currency={currency} />
        ) : (
          <>
            <p className="mt-2 text-2xl font-semibold tracking-tight tabular-nums text-ink">
              {moneyAlways(metric.value)}{' '}
              <span className="text-sm font-normal text-ink-subtle">{currency}</span>
            </p>
            {metric.count > 0 && (
              <p className="mt-1 text-xs text-ink-muted">
                {t('dashboard.itemCount', { count: metric.count })}
              </p>
            )}
          </>
        )}
      </div>
    </div>
  );
}

function Series({
  points,
}: {
  readonly points: readonly MetricPoint[];
}): React.JSX.Element {
  const { t } = useTranslation();

  if (points.length === 0) {
    return <p className="mt-3 text-sm text-ink-subtle">{t('dashboard.noData')}</p>;
  }

  // Scaled against the tallest bar rather than the total, so the shape of the trend
  // is readable whatever the absolute figures are. A floor of 1 keeps an all-zero
  // series from dividing by nothing.
  const peak = Math.max(...points.map((point) => Math.abs(point.value)), 1);

  // No `items-end` on the row below. The columns have to stretch to its full height,
  // because each bar is sized as a percentage of the track above its label — and a
  // percentage height against a parent that has shrunk to fit its content resolves
  // to zero, which is a chart of invisible bars.
  return (
    <div className="mt-3 flex h-28 gap-1">
      {points.map((point, index) => (
        <div
          key={point.label}
          className="group/bar flex min-w-0 flex-1 flex-col items-center gap-1"
        >
          <div className="flex w-full flex-1 items-end">
            {/*
              Each bar grows from the axis on first paint, left to right. The delay
              is capped so a twelve-month series finishes in about half a second
              rather than making the reader watch it fill.
            */}
            <div
              title={`${point.label}: ${moneyAlways(point.value)}`}
              className="w-full origin-bottom animate-grow-bar rounded-t bg-gradient-to-t from-brand-600 to-brand-400 transition-[filter] duration-200 group-hover/bar:brightness-110 dark:from-brand-500 dark:to-brand-300"
              style={{
                height: `${Math.max((Math.abs(point.value) / peak) * 100, 2)}%`,
                animationDelay: `${Math.min(index * 45, 500)}ms`,
              }}
            />
          </div>
          {/* Only the month: twelve four-digit years side by side is unreadable at
              this width. */}
          <span className="truncate text-[10px] text-ink-subtle">
            {point.label.slice(5)}
          </span>
        </div>
      ))}
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
    <ul className="mt-3 space-y-1">
      {points.map((point) => (
        <li
          key={point.label}
          className="relative flex items-baseline justify-between gap-3 overflow-hidden rounded-md px-2 py-1 text-sm"
        >
          <span
            aria-hidden="true"
            className="absolute inset-y-0 start-0 rounded-md bg-brand-500/10"
            style={{ width: `${(Math.abs(point.value) / peak) * 100}%` }}
          />
          <span className="relative truncate text-ink">{point.label}</span>
          <span className="relative shrink-0 font-mono tabular-nums text-ink">
            {moneyAlways(point.value)}{' '}
            <span className="text-xs text-ink-subtle">{currency}</span>
          </span>
        </li>
      ))}
    </ul>
  );
}
