import { useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import {
  DateRangeControls,
  EmptyState,
  ReportFrame,
  moneyAlways,
} from '@/components/ReportFrame';
import { request, type ApiError } from '@/lib/api';

/** The voucher types, keyed by the wire value, for the `voucherTypes.<name>` lookup. */
const TYPE_NAME: Record<number, string> = {
  1: 'CashReceipt',
  2: 'BankReceipt',
  3: 'CashPayment',
  4: 'BankPayment',
  5: 'Journal',
  6: 'Contra',
};

/** The voucher statuses, keyed by the wire value. */
const STATUS_NAME: Record<number, string> = { 1: 'Draft', 2: 'Posted', 3: 'Cancelled' };

interface TransactionSummaryType {
  readonly type: number;
  readonly voucherCount: number;
  readonly totalAmount: number;
  readonly countByStatus: Readonly<Record<string, number>>;
}

interface TransactionSummaryMonth {
  readonly year: number;
  readonly month: number;
  readonly voucherCount: number;
  readonly totalAmount: number;
}

interface TransactionSummary {
  readonly from: string;
  readonly to: string;
  readonly currency: string;
  readonly types: readonly TransactionSummaryType[];
  readonly months: readonly TransactionSummaryMonth[];
  readonly voucherCount: number;
  readonly totalAmount: number;
  readonly countByStatus: Readonly<Record<string, number>>;
}

function startOfYear(): string {
  return `${new Date().getFullYear()}-01-01`;
}

function endOfYear(): string {
  return `${new Date().getFullYear()}-12-31`;
}

/** Formats a year and month as `2026-03`, which sorts and reads unambiguously. */
function monthLabel(year: number, month: number): string {
  return `${year}-${String(month).padStart(2, '0')}`;
}

/**
 * The transaction summary: activity in totals, by voucher type and by month.
 *
 * No individual voucher appears — that is the day book and the voucher report. This
 * answers how much of each kind was raised and what it came to, which is the control
 * total an auditor ticks against and the shape of a period before deciding which
 * report to open next.
 *
 * The month figures are drawn as proportional bars rather than left as a column of
 * numbers, because the question asked of them is almost always comparative: which
 * month was busy, and is the trend up or down.
 */
export function TransactionSummaryPage(): React.JSX.Element {
  const { t } = useTranslation();
  const [from, setFrom] = useState(startOfYear());
  const [to, setTo] = useState(endOfYear());
  const [range, setRange] = useState({ from: startOfYear(), to: endOfYear() });

  const query = useQuery<TransactionSummary, ApiError>({
    queryKey: ['transaction-summary', range.from, range.to],
    queryFn: () =>
      request<TransactionSummary>(
        `/accounting/reports/transaction-summary?from=${range.from}&to=${range.to}`,
      ),
  });

  const controls = (
    <DateRangeControls
      from={from}
      to={to}
      onFromChange={setFrom}
      onToChange={setTo}
      onApply={() => setRange({ from, to })}
      busy={query.isFetching}
    />
  );

  return (
    <ReportFrame title={t('nav.transactionSummary')} controls={controls} query={query}>
      {(data) =>
        data.types.length === 0 ? (
          <EmptyState message={t('reports.noData')} />
        ) : (
          <div className="space-y-6">
            <section className="space-y-2">
              <h2 className="text-sm font-semibold text-ink">{t('reports.byType')}</h2>

              <div className="table-wrap table-wrap-tall">
                <table className="table min-w-[40rem]">
                  <thead>
                    <tr>
                      <th className="text-start">{t('reports.voucherType')}</th>
                      <th className="text-end">{t('reports.count')}</th>
                      <th className="text-start">{t('reports.status')}</th>
                      <th className="text-end">{t('reports.amount')}</th>
                    </tr>
                  </thead>

                  <tbody>
                    {data.types.map((row) => (
                      <tr key={row.type}>
                        <td>{t(`voucherTypes.${TYPE_NAME[row.type]}`)}</td>
                        <td className="cell-numeric">{row.voucherCount}</td>
                        <td className="text-xs text-ink-muted">
                          {Object.entries(row.countByStatus)
                            .map(([status, count]) => {
                              const name = STATUS_NAME[Number(status)] ?? status;
                              return `${t(`voucherStatus.${name}`)} ${count}`;
                            })
                            .join(' · ')}
                        </td>
                        <td className="cell-numeric">{moneyAlways(row.totalAmount)}</td>
                      </tr>
                    ))}
                  </tbody>

                  <tfoot>
                    <tr>
                      <td>{t('reports.totals')}</td>
                      <td className="cell-numeric">{data.voucherCount}</td>
                      <td />
                      <td className="cell-numeric">
                        {moneyAlways(data.totalAmount)} {data.currency}
                      </td>
                    </tr>
                  </tfoot>
                </table>
              </div>
            </section>

            {data.months.length > 0 && (
              <section className="space-y-2">
                <h2 className="text-sm font-semibold text-ink">{t('reports.byMonth')}</h2>

                <ul className="card card-body space-y-1.5">
                  {data.months.map((month, index) => {
                    // Scaled against the busiest month rather than the total, so the
                    // tallest bar is always full width and the comparison between
                    // months stays legible however many there are.
                    const busiest = Math.max(
                      ...data.months.map((candidate) => candidate.totalAmount),
                      1,
                    );
                    const width = Math.max((month.totalAmount / busiest) * 100, 1);

                    return (
                      <li
                        key={monthLabel(month.year, month.month)}
                        className="flex flex-wrap items-center gap-x-3 gap-y-1 text-sm"
                      >
                        <span className="w-20 shrink-0 font-mono text-ink-muted tabular-nums">
                          {monthLabel(month.year, month.month)}
                        </span>

                        {/*
                          The bar is given its own flexible track rather than being
                          sized against the row: without it a long month label on a
                          narrow screen squeezes every bar to nothing and the
                          comparison the section exists for disappears.
                        */}
                        <span className="order-last h-2 min-w-24 flex-1 basis-full overflow-hidden rounded-full bg-surface-3 sm:order-none sm:basis-auto">
                          <span
                            aria-hidden="true"
                            className="bar-grow block h-full rounded-full bg-gradient-to-r from-brand-500 to-brand-400"
                            style={{
                              width: `${width}%`,
                              animationDelay: `${Math.min(index * 40, 400)}ms`,
                            }}
                          />
                        </span>

                        <span className="shrink-0 font-mono tabular-nums">
                          {moneyAlways(month.totalAmount)}
                        </span>
                        <span className="shrink-0 text-xs text-ink-subtle">
                          {t('reports.voucherCount', { count: month.voucherCount })}
                        </span>
                      </li>
                    );
                  })}
                </ul>
              </section>
            )}
          </div>
        )
      }
    </ReportFrame>
  );
}
