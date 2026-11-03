import { useQuery } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import type { ApiError } from '@/lib/api';
import {
  PAYMENT_MODE_KEYS,
  allZero,
  fetchDashboardCharts,
  formatKey,
  sharesOf,
  type DashboardCharts as Charts,
  type MonthlyTradingPoint,
  type RankedSales,
  type ReceivableAge,
} from '@/lib/dashboardCharts';
import { useMoney } from '@/lib/money';
import { useSession } from '@/stores/session';
import { ChartCard, ChartTable } from '@/components/charts/ChartParts';
import { AreaChart } from '@/components/charts/AreaChart';
import { ColumnChart, type ColumnSeries } from '@/components/charts/ColumnChart';
import { BarRows } from '@/components/charts/BarRows';

const SALES = 'var(--app-chart-1)';
const PURCHASES = 'var(--app-chart-2)';
const LOSS = 'var(--app-chart-loss)';
// Written out in full rather than built from a template: the steps are plain custom
// properties, and a name assembled at run time is one nobody can search for.
const AGES = [
  'var(--app-chart-age-1)',
  'var(--app-chart-age-2)',
  'var(--app-chart-age-3)',
  'var(--app-chart-age-4)',
  'var(--app-chart-age-5)',
];

/**
 * The charts under the dashboard's figures: how trading has gone over time.
 *
 * One fixed set for every firm rather than panels somebody has to arrange, each one
 * drawn from the report it summarises - the sales and purchase registers and the
 * books - so a chart and that report always agree. A chart the reader may not open
 * the report for is not drawn at all, rather than drawn flat: a flat line says
 * nothing was sold.
 */
export function DashboardCharts(): React.JSX.Element | null {
  const { t } = useTranslation();
  const charts = useQuery<Charts, ApiError>({
    queryKey: ['dashboard-charts'],
    queryFn: fetchDashboardCharts,
  });

  if (charts.isPending) {
    return (
      <div
        className="grid grid-cols-1 gap-3 sm:gap-4 lg:grid-cols-2 xl:grid-cols-3"
        aria-hidden="true"
      >
        {Array.from({ length: 3 }, (_, index) => (
          <div
            key={index}
            className={`card card-body h-72 ${index === 0 ? 'lg:col-span-2' : ''}`}
          >
            <span className="skeleton block h-3 w-2/5 rounded" />
            <span className="skeleton mt-6 block h-44 w-full rounded" />
          </div>
        ))}
      </div>
    );
  }

  if (charts.isError) {
    return (
      <div role="alert" className="alert-error">
        {t('charts.loadFailed')}
      </div>
    );
  }

  return <ChartGrid charts={charts.data} />;
}

function ChartGrid({ charts }: { readonly charts: Charts }): React.JSX.Element | null {
  const { t } = useTranslation();
  const { language } = useSession();
  const { moneyAlways } = useMoney();
  const currency = charts.currency;
  const withCurrency = (note: string): string =>
    currency ? `${note} · ${currency}` : note;

  const cards: React.JSX.Element[] = [];

  if (charts.dailySales) {
    const days = charts.dailySales;
    const total = days.reduce((sum, day) => sum + day.sales, 0);
    const bills = days.reduce((sum, day) => sum + day.bills, 0);

    cards.push(
      <ChartCard
        key="daily"
        className="lg:col-span-2"
        title={t('charts.dailySales')}
        subtitle={withCurrency(t('charts.withTax'))}
        headline={
          <Headline
            figure={moneyAlways(total)}
            note={t('charts.bills', { count: bills, n: bills.toLocaleString() })}
          />
        }
        table={
          <ChartTable
            head={[t('charts.date'), t('charts.billsHeading'), t('charts.sales')]}
            rows={days.map((day) => [
              formatKey(day.date, language, {
                day: 'numeric',
                month: 'short',
                year: 'numeric',
              }),
              day.bills.toLocaleString(),
              moneyAlways(day.sales),
            ])}
          />
        }
      >
        {allZero(days.map((day) => day.sales)) ? (
          <Quiet message={t('charts.noSales')} />
        ) : (
          <AreaChart
            label={t('charts.dailySales')}
            format={moneyAlways}
            points={days.map((day) => ({
              key: day.date,
              value: day.sales,
              label: formatKey(day.date, language, { day: 'numeric', month: 'short' }),
              title: formatKey(day.date, language, {
                weekday: 'short',
                day: 'numeric',
                month: 'long',
              }),
              note: t('charts.bills', {
                count: day.bills,
                n: day.bills.toLocaleString(),
              }),
            }))}
          />
        )}
      </ChartCard>,
    );
  }

  if (charts.paymentModes) {
    const modes = [...charts.paymentModes].sort((a, b) => b.amount - a.amount);
    const shares = sharesOf(modes.map((mode) => mode.amount));
    const total = modes.reduce((sum, mode) => sum + mode.amount, 0);
    const bills = modes.reduce((sum, mode) => sum + mode.bills, 0);
    const name = (mode: number): string => {
      const key = PAYMENT_MODE_KEYS[mode];
      return key ? t(key) : String(mode);
    };
    const percent = (share: number | null): string =>
      share === null
        ? ''
        : share.toLocaleString(undefined, { style: 'percent', maximumFractionDigits: 0 });

    cards.push(
      <ChartCard
        key="modes"
        title={t('charts.paymentModes')}
        subtitle={withCurrency(t('charts.thisMonth'))}
        headline={
          modes.length > 0 ? (
            <Headline
              figure={moneyAlways(total)}
              note={t('charts.bills', { count: bills, n: bills.toLocaleString() })}
            />
          ) : undefined
        }
        table={
          <ChartTable
            head={[
              t('charts.mode'),
              t('charts.billsHeading'),
              t('charts.amount'),
              t('charts.share'),
            ]}
            rows={modes.map((mode, index) => [
              name(mode.mode),
              mode.bills.toLocaleString(),
              moneyAlways(mode.amount),
              percent(shares[index] ?? null),
            ])}
          />
        }
      >
        {modes.length === 0 ? (
          <Quiet message={t('charts.noSalesMonth')} />
        ) : (
          <BarRows
            share
            rows={modes.map((mode, index) => ({
              key: String(mode.mode),
              label: name(mode.mode),
              detail: t('charts.bills', {
                count: mode.bills,
                n: mode.bills.toLocaleString(),
              }),
              value: shares[index] ?? 0,
              figure: moneyAlways(mode.amount),
              note: percent(shares[index] ?? null),
            }))}
          />
        )}
      </ChartCard>,
    );
  }

  if (charts.months) {
    cards.push(
      <MonthCards
        key="months"
        months={charts.months}
        format={moneyAlways}
        withCurrency={withCurrency}
      />,
    );
  }

  if (charts.topProducts) {
    cards.push(
      <RankingCard
        key="products"
        title={t('charts.topProducts')}
        subtitle={withCurrency(t('charts.thisMonthWithTax'))}
        heading={t('charts.product')}
        rows={charts.topProducts}
        withCode
        format={moneyAlways}
      />,
    );
  }

  if (charts.topCategories) {
    cards.push(
      <RankingCard
        key="categories"
        title={t('charts.topCategories')}
        subtitle={withCurrency(t('charts.thisMonthWithTax'))}
        heading={t('charts.category')}
        rows={charts.topCategories}
        format={moneyAlways}
      />,
    );
  }

  if (charts.receivables) {
    cards.push(
      <AgeingCard
        key="receivables"
        ages={charts.receivables}
        subtitle={withCurrency(t('charts.byDaysOverdue'))}
        format={moneyAlways}
      />,
    );
  }

  if (cards.length === 0) return null;

  return (
    <section aria-label={t('charts.heading')} className="space-y-3">
      <h2 className="text-xs font-semibold tracking-wide text-ink-muted uppercase">
        {t('charts.heading')}
      </h2>
      {/*
        Dense, so a card that comes after a wide one fills the gap beside the card
        before it rather than leaving a hole, whichever charts this reader may see.
      */}
      <div className="grid grid-flow-row-dense grid-cols-1 gap-3 sm:gap-4 lg:grid-cols-2 xl:grid-cols-3">
        {cards}
      </div>
    </section>
  );
}

/** The figure a chart leads with, and a quieter line beside it. */
function Headline({
  figure,
  note,
}: {
  readonly figure: string;
  readonly note?: string | undefined;
}): React.JSX.Element {
  return (
    <p className="flex flex-wrap items-baseline gap-x-2">
      <span className="text-2xl font-semibold tracking-tight text-ink">{figure}</span>
      {note && <span className="text-xs text-ink-muted">{note}</span>}
    </p>
  );
}

/** What a chart with nothing to draw says instead of drawing a flat line. */
function Quiet({ message }: { readonly message: string }): React.JSX.Element {
  return (
    <p className="grid min-h-32 place-items-center rounded-lg bg-surface-2 px-4 text-center text-sm text-ink-subtle">
      {message}
    </p>
  );
}

/**
 * Sales and purchases month by month, and profit - two cards from one series of
 * months, since the books and the purchase register are read for the same twelve.
 * A fragment, so each card is its own cell of the grid.
 */
function MonthCards({
  months,
  format: moneyAlways,
  withCurrency,
}: {
  readonly months: readonly MonthlyTradingPoint[];
  readonly format: (value: number) => string;
  readonly withCurrency: (note: string) => string;
}): React.JSX.Element {
  const { t } = useTranslation();
  const { language } = useSession();
  const labels = months.map((month) =>
    formatKey(month.month, language, { month: 'short' }),
  );
  // Each month's year: the chart writes it where it changes, so a year that turns
  // inside the twelve is not read as one year.
  const sublabels = months.map((month) =>
    formatKey(month.month, language, { year: 'numeric' }),
  );
  const titles = months.map((month) =>
    formatKey(month.month, language, { month: 'long', year: 'numeric' }),
  );
  const monthNames = months.map((month) =>
    formatKey(month.month, language, { month: 'short', year: 'numeric' }),
  );

  const series: ColumnSeries[] = [];

  if (months.some((month) => month.sales !== null)) {
    series.push({
      key: 'sales',
      name: t('charts.sales'),
      color: SALES,
      values: months.map((month) => month.sales),
    });
  }

  if (months.some((month) => month.purchases !== null)) {
    series.push({
      key: 'purchases',
      name: t('charts.purchases'),
      color: PURCHASES,
      values: months.map((month) => month.purchases),
    });
  }

  const cards: React.JSX.Element[] = [];

  if (series.length > 0) {
    const title =
      series.length > 1
        ? t('charts.salesAndPurchases')
        : series[0]?.key === 'sales'
          ? t('charts.sales')
          : t('charts.purchases');

    cards.push(
      <ChartCard
        key="months"
        className="lg:col-span-2"
        title={title}
        subtitle={withCurrency(t('charts.lastYearBeforeTax'))}
        legend={series.map((one) => ({ name: one.name, color: one.color }))}
        table={
          <ChartTable
            head={[t('charts.month'), ...series.map((one) => one.name)]}
            rows={months.map((month, index) => [
              monthNames[index] ?? month.month,
              ...series.map((one) => moneyAlways(one.values[index] ?? 0)),
            ])}
          />
        }
      >
        {allZero(series.flatMap((one) => one.values)) ? (
          <Quiet message={t('charts.nothingYet')} />
        ) : (
          <ColumnChart
            label={title}
            labels={labels}
            sublabels={sublabels}
            titles={titles}
            series={series}
            format={moneyAlways}
            height={220}
          />
        )}
      </ChartCard>,
    );
  }

  if (months.some((month) => month.profit !== null)) {
    const profit: ColumnSeries = {
      key: 'profit',
      name: t('charts.profit'),
      color: SALES,
      values: months.map((month) => month.profit),
    };
    const total = months.reduce((sum, month) => sum + (month.profit ?? 0), 0);

    cards.push(
      <ChartCard
        key="profit"
        title={t('charts.profit')}
        subtitle={withCurrency(t('charts.lastYear'))}
        headline={
          <Headline figure={moneyAlways(total)} note={t('charts.twelveMonths')} />
        }
        legend={[
          { name: t('charts.profit'), color: SALES },
          { name: t('charts.loss'), color: LOSS },
        ]}
        table={
          <ChartTable
            head={[t('charts.month'), t('charts.profit')]}
            rows={months.map((month, index) => [
              monthNames[index] ?? month.month,
              moneyAlways(month.profit ?? 0),
            ])}
          />
        }
      >
        {allZero(profit.values) ? (
          <Quiet message={t('charts.nothingYet')} />
        ) : (
          <ColumnChart
            label={t('charts.profit')}
            labels={labels}
            sublabels={sublabels}
            titles={titles}
            series={[profit]}
            fillOf={(value) => (value < 0 ? LOSS : SALES)}
            // A loss is read as a loss of so much, not as a profit of minus so much.
            nameOf={(value) => (value < 0 ? t('charts.loss') : t('charts.profit'))}
            format={(value) => moneyAlways(Math.abs(value))}
            height={220}
          />
        )}
      </ChartCard>,
    );
  }

  return <>{cards}</>;
}

/** This month's best sellers, largest first, every figure written beside its bar. */
function RankingCard({
  title,
  subtitle,
  heading,
  rows,
  withCode = false,
  format,
}: {
  readonly title: string;
  readonly subtitle: string;
  readonly heading: string;
  readonly rows: readonly RankedSales[];
  readonly withCode?: boolean;
  readonly format: (value: number) => string;
}): React.JSX.Element {
  const { t } = useTranslation();
  const quantity = (value: number): string =>
    t('charts.quantity', {
      quantity: value.toLocaleString(undefined, { maximumFractionDigits: 3 }),
    });

  return (
    <ChartCard
      title={title}
      subtitle={subtitle}
      table={
        <ChartTable
          head={[
            heading,
            ...(withCode ? [t('charts.code')] : []),
            t('charts.quantityHeading'),
            t('charts.amount'),
          ]}
          rows={rows.map((row) => [
            row.label,
            ...(withCode ? [row.detail ?? ''] : []),
            row.quantity.toLocaleString(undefined, { maximumFractionDigits: 3 }),
            format(row.amount),
          ])}
        />
      }
    >
      {rows.length === 0 ? (
        <Quiet message={t('charts.noSalesMonth')} />
      ) : (
        <BarRows
          ordered
          rows={rows.map((row, index) => ({
            key: `${row.label}-${index}`,
            label: row.label,
            value: row.amount,
            figure: format(row.amount),
            note: quantity(row.quantity),
          }))}
        />
      )}
    </ChartCard>
  );
}

/**
 * What customers owe, cut as the age-wise debtors report cuts it, in that order:
 * one hue stepping darker - lighter, in the dark theme - as the debt gets older.
 */
function AgeingCard({
  ages,
  subtitle,
  format,
}: {
  readonly ages: readonly ReceivableAge[];
  readonly subtitle: string;
  readonly format: (value: number) => string;
}): React.JSX.Element {
  const { t } = useTranslation();
  const total = ages.reduce((sum, age) => sum + age.amount, 0);
  const overdue = ages
    .filter((age) => age.fromDays > 0)
    .reduce((sum, age) => sum + age.amount, 0);

  const name = (age: ReceivableAge): string =>
    age.fromDays === 0
      ? t('charts.notDue')
      : age.toDays === null
        ? t('charts.overDays', { days: age.fromDays - 1 })
        : t('charts.dayRange', { from: age.fromDays, to: age.toDays });

  // Spread over the five steps whatever the number of buckets, oldest darkest.
  const colorOf = (index: number): string =>
    AGES[
      ages.length > 1 ? Math.round((index * (AGES.length - 1)) / (ages.length - 1)) : 0
    ] ?? SALES;

  return (
    <ChartCard
      title={t('charts.receivables')}
      subtitle={subtitle}
      headline={
        <Headline
          figure={format(total)}
          note={
            overdue > 0 ? t('charts.overdue', { amount: format(overdue) }) : undefined
          }
        />
      }
      table={
        <ChartTable
          head={[t('charts.daysOverdue'), t('charts.amount')]}
          rows={ages.map((age) => [name(age), format(age.amount)])}
        />
      }
    >
      {allZero(ages.map((age) => age.amount)) ? (
        <Quiet message={t('charts.nothingOwed')} />
      ) : (
        <BarRows
          rows={ages.map((age, index) => ({
            key: `${age.fromDays}`,
            label: name(age),
            value: age.amount,
            figure: format(age.amount),
            color: colorOf(index),
          }))}
        />
      )}
    </ChartCard>
  );
}
