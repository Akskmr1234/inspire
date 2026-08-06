import { useQuery } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import clsx from 'clsx';
import { moneyAlways } from '@/components/ReportFrame';
import { request, type ApiError } from '@/lib/api';
import { useSession } from '@/stores/session';

/** How a panel draws its figure, keyed by the wire value. */
const KIND = { Kpi: 1, Series: 2, Breakdown: 3 } as const;

interface DashboardWidget {
  readonly id: string;
  readonly metricCode: string;
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
  readonly metricCode: string;
  readonly value: number;
  readonly count: number;
  readonly series: readonly MetricPoint[];
  /** False when the caller may not read this figure — drawn as withheld, not as nil. */
  readonly isPermitted: boolean;
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
    return <p className="text-sm text-slate-500">{t('common.loading')}</p>;
  }

  if (!dashboard) {
    return <p className="text-sm text-slate-500">{t('dashboard.none')}</p>;
  }

  const byCode = new Map(data.data?.metrics.map((metric) => [metric.metricCode, metric]));
  const currency = data.data?.currency ?? '';

  return (
    <section className="space-y-4">
      <header className="flex flex-wrap items-end justify-between gap-4">
        <h1 className="text-xl font-semibold">
          {language === 'ar' && dashboard.nameArabic ? dashboard.nameArabic : dashboard.name}
        </h1>
        {data.data && (
          <p className="text-sm text-slate-500">
            {t('reports.asAt')} {data.data.asAt}
          </p>
        )}
      </header>

      {data.isError && (
        <div
          role="alert"
          className="rounded-lg border border-red-300 bg-red-50 px-4 py-3 text-sm text-red-800 dark:border-red-800 dark:bg-red-950 dark:text-red-200"
        >
          {data.error.detail || data.error.code}
        </div>
      )}

      <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-4">
        {dashboard.widgets.map((widget) => (
          <Panel
            key={widget.id}
            widget={widget}
            metric={byCode.get(widget.metricCode)}
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
  const title = language === 'ar' && widget.titleArabic ? widget.titleArabic : widget.title;

  // Tailwind needs whole class names at build time, so the span is mapped rather
  // than interpolated — `col-span-${n}` would be purged and silently do nothing.
  const span =
    { 1: '', 2: 'sm:col-span-2', 3: 'lg:col-span-3', 4: 'lg:col-span-4' }[widget.span] ?? '';

  return (
    <div
      className={clsx(
        'rounded-xl border border-slate-200 p-4 dark:border-slate-800',
        span,
      )}
    >
      <p className="text-xs font-medium tracking-wide text-slate-500 uppercase">{title}</p>

      {loading ? (
        <p className="mt-3 text-sm text-slate-400">{t('common.loading')}</p>
      ) : !metric ? (
        <p className="mt-3 text-sm text-slate-400">—</p>
      ) : !metric.isPermitted ? (
        // Withheld rather than zero. A dashboard reporting nothing owing and one
        // refusing to say are different facts, and drawing them the same way would
        // make the second look like the first.
        <p className="mt-3 text-sm text-slate-400 italic">{t('dashboard.withheld')}</p>
      ) : widget.kind === KIND.Series ? (
        <Series points={metric.series} />
      ) : widget.kind === KIND.Breakdown ? (
        <Breakdown points={metric.series} currency={currency} />
      ) : (
        <>
          <p className="mt-2 text-2xl font-semibold tabular-nums">
            {moneyAlways(metric.value)}{' '}
            <span className="text-sm font-normal text-slate-400">{currency}</span>
          </p>
          {metric.count > 0 && (
            <p className="mt-1 text-xs text-slate-500">
              {t('dashboard.itemCount', { count: metric.count })}
            </p>
          )}
        </>
      )}
    </div>
  );
}

function Series({ points }: { readonly points: readonly MetricPoint[] }): React.JSX.Element {
  const { t } = useTranslation();

  if (points.length === 0) {
    return <p className="mt-3 text-sm text-slate-400">{t('dashboard.noData')}</p>;
  }

  // Scaled against the tallest bar rather than the total, so the shape of the trend
  // is readable whatever the absolute figures are. A floor of 1 keeps an all-zero
  // series from dividing by nothing.
  const peak = Math.max(...points.map((point) => Math.abs(point.value)), 1);

  return (
    <div className="mt-3 flex h-28 items-end gap-1">
      {points.map((point) => (
        <div key={point.label} className="flex flex-1 flex-col items-center gap-1">
          <div
            title={`${point.label}: ${moneyAlways(point.value)}`}
            className="w-full rounded-t bg-sky-500/70 dark:bg-sky-400/60"
            style={{ height: `${Math.max((Math.abs(point.value) / peak) * 100, 2)}%` }}
          />
          {/* Only the month, and only every third one: twelve four-digit years side
              by side is unreadable at this width. */}
          <span className="text-[10px] text-slate-400">{point.label.slice(5)}</span>
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
    return <p className="mt-3 text-sm text-slate-400">{t('dashboard.noData')}</p>;
  }

  return (
    <ul className="mt-3 space-y-1.5">
      {points.map((point) => (
        <li key={point.label} className="flex items-baseline justify-between gap-3 text-sm">
          <span className="truncate">{point.label}</span>
          <span className="shrink-0 tabular-nums">
            {moneyAlways(point.value)}{' '}
            <span className="text-xs text-slate-400">{currency}</span>
          </span>
        </li>
      ))}
    </ul>
  );
}
