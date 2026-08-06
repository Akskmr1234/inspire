import { useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { ReportFrame, moneyAlways } from '@/components/ReportFrame';
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
    <form
      className="flex flex-wrap items-end gap-3"
      onSubmit={(event) => {
        event.preventDefault();
        setRange({ from, to });
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
          onChange={(e) => setFrom(e.target.value)}
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
          onChange={(e) => setTo(e.target.value)}
        />
      </div>
      <button type="submit" disabled={query.isFetching} className="btn-primary">
        {query.isFetching ? t('reports.running') : t('reports.run')}
      </button>
    </form>
  );

  return (
    <ReportFrame title={t('nav.transactionSummary')} controls={controls} query={query}>
      {(data) =>
        data.types.length === 0 ? (
          <p className="text-sm text-slate-500">{t('reports.noData')}</p>
        ) : (
          <div className="space-y-8">
            <section className="space-y-2">
              <h2 className="font-semibold">{t('reports.byType')}</h2>

              <div className="overflow-x-auto rounded-xl border border-slate-200 dark:border-slate-800">
                <table className="w-full border-collapse text-sm">
                  <thead className="bg-slate-100 dark:bg-slate-800">
                    <tr>
                      <th className="px-3 py-2 text-start font-semibold">
                        {t('reports.voucherType')}
                      </th>
                      <th className="px-3 py-2 text-end font-semibold">
                        {t('reports.count')}
                      </th>
                      <th className="px-3 py-2 text-start font-semibold">
                        {t('reports.status')}
                      </th>
                      <th className="px-3 py-2 text-end font-semibold">
                        {t('reports.amount')}
                      </th>
                    </tr>
                  </thead>

                  <tbody>
                    {data.types.map((row) => (
                      <tr
                        key={row.type}
                        className="border-t border-slate-200 dark:border-slate-800"
                      >
                        <td className="px-3 py-2">
                          {t(`voucherTypes.${TYPE_NAME[row.type]}`)}
                        </td>
                        <td className="cell-numeric">{row.voucherCount}</td>
                        <td className="px-3 py-2 text-xs text-slate-500">
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

                  <tfoot className="border-t-2 border-slate-300 bg-slate-100 font-semibold dark:border-slate-700 dark:bg-slate-800">
                    <tr>
                      <td className="px-3 py-2">{t('reports.totals')}</td>
                      <td className="cell-numeric">{data.voucherCount}</td>
                      <td className="px-3 py-2"></td>
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
                <h2 className="font-semibold">{t('reports.byMonth')}</h2>

                <ul className="space-y-1">
                  {data.months.map((month) => {
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
                        className="flex items-center gap-3 text-sm"
                      >
                        <span className="w-20 shrink-0 tabular-nums text-slate-600 dark:text-slate-400">
                          {monthLabel(month.year, month.month)}
                        </span>
                        <span
                          aria-hidden="true"
                          className="h-4 rounded bg-sky-500/70 dark:bg-sky-400/60"
                          style={{ width: `${width}%` }}
                        />
                        <span className="shrink-0 tabular-nums">
                          {moneyAlways(month.totalAmount)}
                        </span>
                        <span className="shrink-0 text-xs text-slate-500">
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
