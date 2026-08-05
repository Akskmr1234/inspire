import { useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import clsx from 'clsx';
import { request, type ApiError } from '@/lib/api';

interface TrialBalanceRow {
  readonly ledgerId: string;
  readonly ledgerCode: string;
  readonly ledgerName: string;
  readonly groupCode: string;
  readonly groupName: string;
  readonly openingDebit: number;
  readonly openingCredit: number;
  readonly periodDebit: number;
  readonly periodCredit: number;
  readonly closingDebit: number;
  readonly closingCredit: number;
}

interface TrialBalance {
  readonly from: string;
  readonly to: string;
  readonly currency: string;
  readonly rows: readonly TrialBalanceRow[];
  readonly totalOpeningDebit: number;
  readonly totalOpeningCredit: number;
  readonly totalPeriodDebit: number;
  readonly totalPeriodCredit: number;
  readonly totalClosingDebit: number;
  readonly totalClosingCredit: number;
  readonly isBalanced: boolean;
}

/** Formats a figure for a financial column, blanking zero so the eye follows the numbers. */
function money(value: number): string {
  return value === 0
    ? ''
    : value.toLocaleString(undefined, {
        minimumFractionDigits: 2,
        maximumFractionDigits: 2,
      });
}

function startOfYear(): string {
  return `${new Date().getFullYear()}-01-01`;
}

function endOfYear(): string {
  return `${new Date().getFullYear()}-12-31`;
}

/** The trial balance screen. */
export function TrialBalancePage(): React.JSX.Element {
  const { t } = useTranslation();
  const [from, setFrom] = useState(startOfYear());
  const [to, setTo] = useState(endOfYear());
  const [range, setRange] = useState({ from: startOfYear(), to: endOfYear() });

  const query = useQuery<TrialBalance, ApiError>({
    queryKey: ['trial-balance', range.from, range.to],
    queryFn: () =>
      request<TrialBalance>(
        `/accounting/reports/trial-balance?from=${range.from}&to=${range.to}`,
      ),
  });

  return (
    <section className="space-y-4">
      <header className="flex flex-wrap items-end justify-between gap-4">
        <h1 className="text-xl font-semibold">{t('nav.trialBalance')}</h1>

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

      {query.data && (
        <>
          {/*
            The balance state is shown prominently and coloured, because a trial
            balance that does not balance means the books are broken. Printing the
            figures without saying so would leave a reader to compare two totals
            and hope they notice.
          */}
          <p
            className={clsx(
              'inline-block rounded-lg px-3 py-1.5 text-sm font-medium',
              query.data.isBalanced
                ? 'bg-emerald-50 text-emerald-800 dark:bg-emerald-950 dark:text-emerald-200'
                : 'bg-red-100 text-red-900 dark:bg-red-950 dark:text-red-100',
            )}
          >
            {query.data.isBalanced
              ? `${t('reports.balanced')} · ${query.data.currency}`
              : t('reports.notBalanced')}
          </p>

          {query.data.rows.length === 0 ? (
            <p className="text-sm text-slate-500">{t('reports.noData')}</p>
          ) : (
            <div className="overflow-x-auto rounded-xl border border-slate-200 dark:border-slate-800">
              <table className="w-full border-collapse text-sm">
                <thead className="bg-slate-100 dark:bg-slate-800">
                  <tr>
                    <th className="px-3 py-2 text-start font-semibold">
                      {t('reports.ledger')}
                    </th>
                    <th className="px-3 py-2 text-start font-semibold">
                      {t('reports.group')}
                    </th>
                    <th className="px-3 py-2 text-end font-semibold">
                      {t('reports.openingDebit')}
                    </th>
                    <th className="px-3 py-2 text-end font-semibold">
                      {t('reports.openingCredit')}
                    </th>
                    <th className="px-3 py-2 text-end font-semibold">
                      {t('reports.periodDebit')}
                    </th>
                    <th className="px-3 py-2 text-end font-semibold">
                      {t('reports.periodCredit')}
                    </th>
                    <th className="px-3 py-2 text-end font-semibold">
                      {t('reports.closingDebit')}
                    </th>
                    <th className="px-3 py-2 text-end font-semibold">
                      {t('reports.closingCredit')}
                    </th>
                  </tr>
                </thead>

                <tbody>
                  {query.data.rows.map((row) => (
                    <tr
                      key={row.ledgerId}
                      className="border-t border-slate-200 hover:bg-slate-50 dark:border-slate-800 dark:hover:bg-slate-800/50"
                    >
                      <td className="px-3 py-2">
                        <span className="font-medium">{row.ledgerCode}</span>
                        <span className="ms-2 text-slate-500">{row.ledgerName}</span>
                      </td>
                      <td className="px-3 py-2 text-slate-500">
                        {row.groupCode} {row.groupName}
                      </td>
                      <td className="cell-numeric">{money(row.openingDebit)}</td>
                      <td className="cell-numeric">{money(row.openingCredit)}</td>
                      <td className="cell-numeric">{money(row.periodDebit)}</td>
                      <td className="cell-numeric">{money(row.periodCredit)}</td>
                      <td className="cell-numeric">{money(row.closingDebit)}</td>
                      <td className="cell-numeric">{money(row.closingCredit)}</td>
                    </tr>
                  ))}
                </tbody>

                <tfoot className="border-t-2 border-slate-300 bg-slate-100 font-semibold dark:border-slate-700 dark:bg-slate-800">
                  <tr>
                    <td className="px-3 py-2" colSpan={2}>
                      {t('reports.totals')}
                    </td>
                    <td className="cell-numeric">
                      {money(query.data.totalOpeningDebit)}
                    </td>
                    <td className="cell-numeric">
                      {money(query.data.totalOpeningCredit)}
                    </td>
                    <td className="cell-numeric">{money(query.data.totalPeriodDebit)}</td>
                    <td className="cell-numeric">
                      {money(query.data.totalPeriodCredit)}
                    </td>
                    <td className="cell-numeric">
                      {money(query.data.totalClosingDebit)}
                    </td>
                    <td className="cell-numeric">
                      {money(query.data.totalClosingCredit)}
                    </td>
                  </tr>
                </tfoot>
              </table>
            </div>
          )}
        </>
      )}
    </section>
  );
}
